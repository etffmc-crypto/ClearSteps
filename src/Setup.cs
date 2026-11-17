// ClearSteps - status checks, the official-installer runner, and the self-updater. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace ClearSteps
{
    public class Status
    {
        public bool Stack, Library, Engine, EapoOnPlus, Format, OldDisabled, Mixer, TuneSet, EqSet;
        public Endpoint Plus, Old, Normal;
        public string LibraryVersion = "";
        public TuneChoice Current = new TuneChoice();
        public bool ReadyForHeadset { get { return Stack && Library && Engine && EapoOnPlus && Format; } }
    }

    public static class Setup
    {
#if OLDVER
        public const string AppVersion = "0.9.0";   // test build only: proves the self-update path
#else
        public const string AppVersion = "1.2.0";
#endif
        public const string RepoRaw = "https://raw.githubusercontent.com/etffmc-crypto/ClearSteps/main/";
        public const string RepoPage = "https://github.com/etffmc-crypto/ClearSteps";

        public static string DataDir
        {
            get { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClearSteps"); Directory.CreateDirectory(d); return d; }
        }

        static string Http(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "ClearSteps"; wc.Encoding = Encoding.UTF8; return wc.DownloadString(url).TrimStart('\uFEFF'); }
        }

        static string Embedded(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        /// Newest catalog wins: online copy, else the cached one, else the copy built into the exe.
        public static Catalog LoadCatalog(bool tryOnline)
        {
            Catalog best = Catalog.Parse(Embedded("catalog.json"));
            string cache = Path.Combine(DataDir, "catalog.json");
            try { if (File.Exists(cache)) { Catalog c = Catalog.Parse(File.ReadAllText(cache)); if (c.Version > best.Version) best = c; } } catch { }
            if (tryOnline)
            {
                try
                {
                    string json = Http(RepoRaw + "catalog.json");
                    Catalog c = Catalog.Parse(json);
                    if (c.Version > best.Version) { best = c; File.WriteAllText(cache, json, new UTF8Encoding(false)); }
                }
                catch { }
            }
            return best;
        }

        public static Status Check(Catalog cat)
        {
            var s = new Status();
            List<Endpoint> eps = Audio.List();
            s.Plus = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Art Tune +" && e.State != 4; });
            s.Old = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Art Tune" && e.State != 4; });
            s.Normal = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Normal Audio" && e.State == 1; });
            s.Stack = s.Plus != null && Eapo.IsEapoPresent() && File.Exists(Paths.ConfigFile);
            s.LibraryVersion = Paths.LibraryVersion;
            s.Library = Directory.Exists(Path.Combine(Paths.Library, cat.Folder));
            s.Engine = File.Exists(Paths.CharlieDll);
            s.EapoOnPlus = s.Plus != null && Eapo.IsInstalledOn(s.Plus.Guid) && !Eapo.EnhancementsDisabled(s.Plus.Guid);
            s.Format = s.Plus != null && s.Plus.State == 1 && s.Plus.Channels == 16 && s.Plus.Bits == 24 && s.Plus.SampleRate == 48000;
            s.OldDisabled = s.Old == null || s.Old.State != 1;
            s.Mixer = Voicemeeter.IsInstalled && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoiceMeeterDefault.xml"));
            if (s.Stack) { try { s.Current = ConfigWriter.ReadCurrent(cat); } catch { } }
            s.TuneSet = s.Current.Style != null;
            s.EqSet = s.Current.EqInclude != null && !s.Current.EqInclude.EndsWith("Flat_EQ.txt", StringComparison.OrdinalIgnoreCase);
            return s;
        }

        /// Runs ArtIsWar's official guided installer, fetched from his site, in its own window.
        /// choice: "1" install, "3" refresh the library. Blank answers accept each prompt's safe default.
        public static int RunOfficialInstaller(Catalog cat, string choice, Action<string> say)
        {
            say("Downloading ArtIsWar's installer...");
            string script = Http(cat.InstallerUrl);
            if (script.IndexOf("ArtTune", StringComparison.OrdinalIgnoreCase) < 0 || script.Length < 50000) throw new InvalidDataException("The installer download did not look right. Try again in a minute.");
            string file = Path.Combine(DataDir, "Install-ArtTune.ps1");
            File.WriteAllText(file, script, new UTF8Encoding(false));
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\WindowsPowerShell\v1.0\powershell.exe");
            var psi = new ProcessStartInfo(ps, "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"");
            psi.UseShellExecute = false; psi.RedirectStandardInput = true; psi.CreateNoWindow = false;
            say("Running it (a blue window opens; leave it alone unless a small pop-up asks Yes/OK)...");
            using (Process p = Process.Start(psi))
            {
                p.StandardInput.WriteLine(choice);
                for (int i = 0; i < 8; i++) p.StandardInput.WriteLine("");
                p.StandardInput.WriteLine("q");
                p.StandardInput.Close();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// Everything the guide does by hand in the Sound panel and Device Selector, for the Art Tune + device.
        public static void ConfigureDevices(Catalog cat, Action<string> say)
        {
            Status s = Check(cat);
            if (s.Plus == null) throw new InvalidOperationException("The Art Tune + device is missing. Run the install step again, and restart the PC if it still does not appear.");
            if (!s.EapoOnPlus)
            {
                say("Attaching Equalizer APO to Art Tune +...");
                Eapo.InstallOn(s.Plus.Guid);
                say("Restarting Windows audio (sound drops for a few seconds)...");
                Eapo.RestartAudio();
                if (!Eapo.IsInstalledOn(s.Plus.Guid)) { Eapo.InstallOn(s.Plus.Guid); Eapo.RestartAudio(); }
            }
            say("Setting Art Tune + to 16 channel, 24 bit, 48000 Hz...");
            int hr = -1;
            for (int i = 0; i < 5 && hr != 0; i++) { hr = Audio.SetFormat(s.Plus.Id, 16, 24, 48000, 0); if (hr != 0) System.Threading.Thread.Sleep(1500); }
            if (hr != 0) throw new InvalidOperationException("Windows refused the 16 channel format (0x" + hr.ToString("X8") + "). Restart the PC and run this step again.");
            if (s.Old != null && s.Old.State == 1) { say("Disabling the old Art Tune device (MW4 needs only Art Tune +)..."); Audio.SetEnabled(s.Old.Id, false); }
            Endpoint cap = Audio.List().Find(delegate(Endpoint e) { return !e.IsRender && e.Name == "Art Tune Unified Output" && e.State == 1; });
            if (cap != null && (cap.Channels != 2 || cap.Bits != 24 || cap.SampleRate != 48000)) Audio.SetFormat(cap.Id, 2, 24, 48000, 3);
            if (!s.TuneSet)
            {
                say("Loading the recommended tune...");
                ConfigWriter.Apply(cat, new TuneChoice { Style = cat.DefaultStyle, Gun = cat.DefaultGun, Head = cat.DefaultHead, BoostDb = ConfigWriter.ReadBoost() }, s.Plus.Guid);
            }
        }

        // ---- self update -------------------------------------------------------------------------------------
        public class UpdateInfo { public string Version, Url, Notes; }

        static int[] Parts(string v) { string[] p = v.Split('.'); var r = new int[3]; for (int i = 0; i < 3 && i < p.Length; i++) int.TryParse(p[i], out r[i]); return r; }
        public static bool IsNewer(string candidate, string current)
        {
            int[] a = Parts(candidate), b = Parts(current);
            for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
            return false;
        }

        public static UpdateInfo CheckForUpdate()
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Http(RepoRaw + "update.json"));
            var u = new UpdateInfo { Version = Convert.ToString(d["version"]), Url = Convert.ToString(d["url"]), Notes = d.ContainsKey("notes") ? Convert.ToString(d["notes"]) : "" };
            return IsNewer(u.Version, AppVersion) ? u : null;
        }

        /// Downloads the new exe beside this one, swaps it in after we exit, and relaunches.
        public static void ApplyUpdate(UpdateInfo u)
        {
            if (!u.Url.StartsWith("https://github.com/etffmc-crypto/ClearSteps/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Update link is not from the ClearSteps page.");
            string me = Assembly.GetExecutingAssembly().Location;
            string fresh = me + ".new";
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "ClearSteps"; wc.DownloadFile(u.Url, fresh); }
            byte[] head = new byte[2];
            using (FileStream f = File.OpenRead(fresh)) f.Read(head, 0, 2);
            if (head[0] != 'M' || head[1] != 'Z' || new FileInfo(fresh).Length < 20000) { File.Delete(fresh); throw new InvalidDataException("The update download was not a valid program file."); }
            string cmd = "/c ping -n 3 127.0.0.1 >nul & move /y \"" + fresh + "\" \"" + me + "\" & start \"\" \"" + me + "\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false });
            Environment.Exit(0);
        }
    }
}
