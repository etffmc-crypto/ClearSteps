// ClearSteps - configures Voicemeeter Standard through its official Remote API so nobody has to click through it.
// Input 1 = Art Tune Unified Output, A1 = the chosen headset, WDM buffer 256, tray + run at startup + auto restart.
// C# 5 compatible. Must run in a 64-bit process.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ClearSteps
{
    public class VmDevice { public int Type; public string Name; public override string ToString() { return Name; } }

    public static class Voicemeeter
    {
        const string D = "VoicemeeterRemote64.dll";
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport(D)] static extern int VBVMR_Login();
        [DllImport(D)] static extern int VBVMR_Logout();
        [DllImport(D)] static extern int VBVMR_GetVoicemeeterType(out int type);
        [DllImport(D)] static extern int VBVMR_IsParametersDirty();
        [DllImport(D)] static extern int VBVMR_SetParameterFloat([MarshalAs(UnmanagedType.LPStr)] string name, float v);
        [DllImport(D)] static extern int VBVMR_SetParameterStringW([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string v);
        [DllImport(D)] static extern int VBVMR_GetParameterStringW([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder v);
        [DllImport(D)] static extern int VBVMR_Input_GetDeviceNumber();
        [DllImport(D)] static extern int VBVMR_Output_GetDeviceNumber();
        [DllImport(D, CharSet = CharSet.Unicode)] static extern int VBVMR_Input_GetDeviceDescW(int i, out int type, StringBuilder name, StringBuilder hwId);
        [DllImport(D, CharSet = CharSet.Unicode)] static extern int VBVMR_Output_GetDeviceDescW(int i, out int type, StringBuilder name, StringBuilder hwId);

        public const int Wdm = 3, Ks = 4;
        static bool loaded;

        public static string InstallDir
        {
            get
            {
                foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\VB:Voicemeeter {17359A74-1236-5467}"))
                        {
                            string u = k == null ? null : k.GetValue("UninstallString") as string;
                            if (!string.IsNullOrEmpty(u)) { string dir = Path.GetDirectoryName(u.Trim('"')); if (Directory.Exists(dir)) return dir; }
                        }
                    }
                    catch { }
                }
                return null;
            }
        }
        public static string Exe { get { string d = InstallDir; return d == null ? null : Path.Combine(d, "voicemeeter_x64.exe"); } }
        public static bool IsInstalled { get { string e = Exe; return e != null && File.Exists(e); } }
        static Process[] Running() { return Process.GetProcessesByName("voicemeeter_x64"); }

        static void Load()
        {
            if (loaded) return;
            string dll = Path.Combine(InstallDir ?? "", D);
            if (LoadLibraryW(dll) == IntPtr.Zero) throw new InvalidOperationException("Voicemeeter's remote library could not be loaded from " + dll);
            loaded = true;
        }

        static void Drain() { for (int i = 0; i < 100 && VBVMR_IsParametersDirty() == 1; i++) Thread.Sleep(20); }

        static void WriteOptions()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"VB-Audio\VoiceMeeter"))
            {
                k.SetValue("SysTray", 1, RegistryValueKind.DWord);
                k.SetValue("RunAtStart", 1, RegistryValueKind.DWord);
                k.SetValue("SysTrayShowOnRun", 0, RegistryValueKind.DWord);
                k.SetValue("AutoRestart", 1, RegistryValueKind.DWord);
                k.SetValue("AutoRestartAll", 1, RegistryValueKind.DWord);
                k.SetValue("BufferingWDM", 256, RegistryValueKind.DWord);
            }
            // "Run on Windows startup" is a Startup-folder shortcut in Voicemeeter, not a Run key.
            string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Voicemeeter (VB-Audio).LNK");
            if (!File.Exists(lnk))
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Exe });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { InstallDir });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
        }

        /// Starts Voicemeeter if needed and logs in. Returns when its audio engine is up.
        static void Connect(Action<string> say)
        {
            Load();
            int rc = VBVMR_Login();
            if (rc < 0) throw new InvalidOperationException("Voicemeeter login failed (" + rc + ")");
            if (Running().Length == 0)
            {
                say("Starting Voicemeeter...");
                Process.Start(new ProcessStartInfo(Exe) { WorkingDirectory = InstallDir, UseShellExecute = true });
            }
            int type = 0; bool up = false;
            for (int i = 0; i < 100; i++) { if (VBVMR_GetVoicemeeterType(out type) == 0) { up = true; break; } Thread.Sleep(200); }
            if (!up) { VBVMR_Logout(); throw new InvalidOperationException("Voicemeeter did not start. A restart of the PC after the install usually fixes this."); }
            Thread.Sleep(6500);   // Voicemeeter waits about 5 s before starting its audio engine
            Drain();
        }

        public static List<VmDevice> OutputDevices()
        {
            var list = new List<VmDevice>();
            Connect(delegate(string s) { });
            try
            {
                int n = VBVMR_Output_GetDeviceNumber();
                for (int i = 0; i < n; i++)
                {
                    int type; var name = new StringBuilder(512); var hw = new StringBuilder(512);
                    if (VBVMR_Output_GetDeviceDescW(i, out type, name, hw) == 0) list.Add(new VmDevice { Type = type, Name = name.ToString() });
                }
            }
            finally { VBVMR_Logout(); }
            return list;
        }

        static void Check(int rc, string what) { if (rc != 0) throw new InvalidOperationException("Voicemeeter refused: " + what + " (" + rc + ")"); }

        /// Applies the whole guide configuration. headsetName must be a name from OutputDevices(); type is Wdm or Ks.
        public static string Configure(string headsetName, int type, Action<string> say)
        {
            if (!IsInstalled) throw new InvalidOperationException("Voicemeeter is not installed.");
            // Options live in the registry and are only read at launch, so set them with Voicemeeter closed.
            if (Running().Length > 0)
            {
                Load();
                if (VBVMR_Login() >= 0) { VBVMR_SetParameterFloat("Command.Shutdown", 1f); Thread.Sleep(300); VBVMR_Logout(); }
                for (int i = 0; i < 50 && Running().Length > 0; i++) Thread.Sleep(200);
                foreach (Process p in Running()) { try { p.Kill(); } catch { } }
            }
            WriteOptions();
            Connect(say);
            try
            {
                string input = null;
                int n = VBVMR_Input_GetDeviceNumber();
                for (int i = 0; i < n; i++)
                {
                    int t; var name = new StringBuilder(512); var hw = new StringBuilder(512);
                    if (VBVMR_Input_GetDeviceDescW(i, out t, name, hw) == 0 && t == Wdm && name.ToString().StartsWith("Art Tune Unified Output", StringComparison.OrdinalIgnoreCase)) { input = name.ToString(); break; }
                }
                if (input == null) throw new InvalidOperationException("Voicemeeter cannot see the Art Tune Unified Output device yet. Restart the PC and run this step again.");
                say("Routing the tune to " + headsetName + "...");
                Check(VBVMR_SetParameterFloat("Option.buffer.wdm", 256f), "buffer size");
                Check(VBVMR_SetParameterStringW("Strip[0].device.wdm", input), "input device");
                Check(VBVMR_SetParameterStringW(type == Ks ? "Bus[0].device.ks" : "Bus[0].device.wdm", headsetName), "output device");
                Thread.Sleep(2500); Drain();
                Check(VBVMR_SetParameterFloat("Strip[0].A1", 1f), "routing");
                VBVMR_SetParameterFloat("Strip[0].B1", 0f);
                VBVMR_SetParameterFloat("Strip[0].Mute", 0f);
                VBVMR_SetParameterFloat("Strip[0].Gain", 0f);
                VBVMR_SetParameterFloat("Strip[2].A1", 1f);    // virtual input = Normal Audio (Discord, browser) to the headset too
                VBVMR_SetParameterFloat("Strip[2].Mute", 0f);
                VBVMR_SetParameterFloat("Bus[0].Mute", 0f);
                VBVMR_SetParameterFloat("Bus[0].Gain", 0f);
                Thread.Sleep(500); Drain();
                VBVMR_SetParameterFloat("Command.Restart", 1f);
                Thread.Sleep(1500); Drain();
                var a = new StringBuilder(1024); var b = new StringBuilder(1024);
                VBVMR_GetParameterStringW("Strip[0].device.name", a);
                VBVMR_GetParameterStringW("Bus[0].device.name", b);
                return "input=" + a + " | output=" + b;
            }
            finally { VBVMR_Logout(); }
        }
    }
}
