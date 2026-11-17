// ClearSteps - attaches Equalizer APO to a playback device, the same way its Device Selector does in
// "LFX/GFX" troubleshooting mode (values taken from Equalizer APO's own source, DeviceAPOInfo.cpp, GPL-2.0).
// Needs administrator rights. C# 5 compatible.
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ClearSteps
{
    public static class Eapo
    {
        const string PreMixClsid = "{EACD2258-FCAC-4FF4-B36D-419E924A6D79}";
        const string PostMixClsid = "{EC1CC9CE-FAED-4822-828A-82A81A6F018F}";
        const string FxGuid = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
        const string DisableSysFx = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
        const string RenderRoot = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\";

        static readonly UIntPtr HKLM = new UIntPtr(0x80000002u);
        const int KEY_QUERY_VALUE = 0x1, KEY_SET_VALUE = 0x2, KEY_WOW64_64KEY = 0x100, REG_SZ = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegOpenKeyExW(UIntPtr root, string subKey, int options, int sam, out IntPtr key);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegSetValueExW(IntPtr key, string name, int reserved, int type, byte[] data, int cb);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegDeleteValueW(IntPtr key, string name);
        [DllImport("advapi32.dll")] static extern int RegCloseKey(IntPtr key);

        static RegistryKey Hklm64() { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }

        static string ReadFx(string guid, string name)
        {
            using (var k = Hklm64().OpenSubKey(RenderRoot + guid + @"\FxProperties"))
                return k == null ? null : k.GetValue(name) as string;
        }

        /// True when Equalizer APO is attached to this render endpoint (same test its Configuration Editor uses).
        public static bool IsInstalledOn(string guid)
        {
            foreach (string pid in new[] { "1", "2", "5", "6", "7" })
            {
                string v = ReadFx(guid, FxGuid + "," + pid);
                if (v == null) continue;
                if (string.Equals(v, PreMixClsid, StringComparison.OrdinalIgnoreCase) || string.Equals(v, PostMixClsid, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static bool EnhancementsDisabled(string guid)
        {
            using (var k = Hklm64().OpenSubKey(RenderRoot + guid + @"\FxProperties"))
            {
                object v = k == null ? null : k.GetValue(DisableSysFx);
                return v is int && (int)v != 0;
            }
        }

        public static bool IsEapoPresent()
        {
            using (var k = Hklm64().OpenSubKey(@"SOFTWARE\Classes\AudioEngine\AudioProcessingObjects\" + PreMixClsid)) return k != null;
        }

        /// Writes the LFX/GFX registration for one render endpoint. Does not restart audio; call RestartAudio() once after.
        public static void InstallOn(string guid)
        {
            string fxPath = RenderRoot + guid + @"\FxProperties";
            using (var probe = Hklm64().OpenSubKey(fxPath))
                if (probe == null) throw new InvalidOperationException("This device has no FxProperties key; use Equalizer APO's Device Selector for it.");

            // Remember what was there, the way Device Selector does, so its own uninstall can restore it.
            string[] pids = { "1", "2", "5", "6", "7" };
            string[] originals = new string[pids.Length];
            for (int i = 0; i < pids.Length; i++) originals[i] = ReadFx(guid, FxGuid + "," + pids[i]);
            bool alreadyOurs = IsInstalledOn(guid);

            if (!alreadyOurs)
            {
                using (var ch = Hklm64().CreateSubKey(@"SOFTWARE\EqualizerAPO\Child APOs\" + guid))
                {
                    for (int i = 0; i < pids.Length; i++) ch.SetValue(FxGuid + "," + pids[i], originals[i] ?? "!VALUE", RegistryValueKind.String);
                    ch.SetValue("PreMixChild", originals[0] ?? "", RegistryValueKind.String);
                    ch.SetValue("PostMixChild", originals[1] ?? "", RegistryValueKind.String);
                    ch.SetValue("AllowSilentBufferModification", "false", RegistryValueKind.String);
                    ch.SetValue("Version", "2", RegistryValueKind.String);
                }
            }

            // The endpoint keys only grant administrators "set value", so ask for exactly that and nothing more.
            IntPtr key;
            int rc = RegOpenKeyExW(HKLM, fxPath, 0, KEY_SET_VALUE | KEY_QUERY_VALUE | KEY_WOW64_64KEY, out key);
            if (rc != 0) throw new Win32Exception(rc, "Could not open the device's effects key for writing");
            try
            {
                SetSz(key, FxGuid + ",1", PreMixClsid);
                SetSz(key, FxGuid + ",2", PostMixClsid);
                foreach (string pid in new[] { "5", "6", "7" }) RegDeleteValueW(key, FxGuid + "," + pid);
                RegDeleteValueW(key, DisableSysFx);   // "Enable audio enhancements" must stay on or the tune is bypassed
            }
            finally { RegCloseKey(key); }
        }

        /// Detaches Equalizer APO from one render endpoint (used by the self-test to prove the guardian re-attaches it).
        public static void RemoveFrom(string guid)
        {
            IntPtr key;
            int rc = RegOpenKeyExW(HKLM, RenderRoot + guid + @"\FxProperties", 0, KEY_SET_VALUE | KEY_QUERY_VALUE | KEY_WOW64_64KEY, out key);
            if (rc != 0) throw new Win32Exception(rc, "Could not open the device's effects key for writing");
            try { RegDeleteValueW(key, FxGuid + ",1"); RegDeleteValueW(key, FxGuid + ",2"); }
            finally { RegCloseKey(key); }
        }

        static void SetSz(IntPtr key, string name, string value)
        {
            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            int rc = RegSetValueExW(key, name, 0, REG_SZ, data, data.Length);
            if (rc != 0) throw new Win32Exception(rc, "Could not write " + name);
        }

        /// Restarts the Windows Audio service (and whatever depends on it), as Device Selector does.
        public static void RestartAudio()
        {
            using (var sc = new ServiceController("Audiosrv"))
            {
                var wait = TimeSpan.FromSeconds(30);
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    foreach (ServiceController dep in sc.DependentServices)
                        if (dep.Status == ServiceControllerStatus.Running) { try { dep.Stop(); dep.WaitForStatus(ServiceControllerStatus.Stopped, wait); } catch { } }
                    sc.Stop(); sc.WaitForStatus(ServiceControllerStatus.Stopped, wait);
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try { sc.Start(); sc.WaitForStatus(ServiceControllerStatus.Running, wait); break; }
                    catch { System.Threading.Thread.Sleep(1000); sc.Refresh(); if (sc.Status == ServiceControllerStatus.Running) break; }
                }
            }
            System.Threading.Thread.Sleep(1500);
        }
    }
}
