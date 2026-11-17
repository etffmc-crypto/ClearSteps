// ClearSteps Guardian - audits the whole audio chain, fixes what drifts, and keeps a log. C# 5 compatible.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ClearSteps
{
    public class Check
    {
        public string Id, Title, Detail; public Health State;
        public bool AutoFix;      // safe for the guardian to repair by itself
        public bool NeedsSetup;   // needs the full "Set up this PC" run
        public bool NeedsHeadset; // needs the user to pick a headset
    }

    public static class Guardian
    {
        // ---- settings ----
        public static bool AutoFixOn = true, KeepInTray = true, StartWithWindows = false;
        public static string Headset = "";          // the Voicemeeter output the user picked, so we can tell when it goes missing
        public static bool HeadsetPresent = true;   // result of the last audit
        public static string Window = "";           // last window position: x,y,w,h,maximized
        static string SettingsFile { get { return Path.Combine(Setup.DataDir, "settings.ini"); } }
        public static string LogFile { get { return Path.Combine(Setup.DataDir, "guardian.log"); } }
        const string TaskName = "ClearSteps Guardian";

        public static void LoadSettings()
        {
            try
            {
                foreach (string l in File.ReadAllLines(SettingsFile))
                {
                    int eq = l.IndexOf('='); if (eq < 1) continue;
                    string[] kv = { l.Substring(0, eq), l.Substring(eq + 1) }; bool v = kv[1].Trim() == "1";
                    if (kv[0] == "headset") { Headset = kv[1].Trim(); continue; }
                    if (kv[0] == "window") { Window = kv[1].Trim(); continue; }
                    if (kv[0] == "autofix") AutoFixOn = v; else if (kv[0] == "tray") KeepInTray = v; else if (kv[0] == "autostart") StartWithWindows = v;
                }
            }
            catch { }
        }
        public static void SaveSettings()
        {
            try { File.WriteAllLines(SettingsFile, new[] { "autofix=" + (AutoFixOn ? 1 : 0), "tray=" + (KeepInTray ? 1 : 0), "autostart=" + (StartWithWindows ? 1 : 0), "headset=" + Headset, "window=" + Window }); } catch { }
        }

        /// Start with Windows uses a scheduled task so the app can start with admin rights without a prompt at every logon.
        public static bool SetAutostart(bool on)
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string args = on ? "/create /f /tn \"" + TaskName + "\" /sc onlogon /rl highest /tr \"\\\"" + exe + "\\\" --tray\"" : "/delete /f /tn \"" + TaskName + "\"";
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false };
                using (Process p = Process.Start(psi)) { p.WaitForExit(15000); if (p.ExitCode != 0 && on) return false; }
                StartWithWindows = on; SaveSettings(); return true;
            }
            catch { return false; }
        }

        public static void Log(string s)
        {
            try
            {
                var fi = new FileInfo(LogFile);
                if (fi.Exists && fi.Length > 512 * 1024) File.Delete(LogFile);
                File.AppendAllText(LogFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | " + s + "\r\n");
            }
            catch { }
        }

        public static bool GameRunning { get { try { return Process.GetProcessesByName("cod").Length > 0; } catch { return false; } } }

        static bool MixerRunning()
        {
            foreach (string n in new[] { "voicemeeter_x64", "voicemeeter", "voicemeeterpro_x64", "voicemeeterpro", "voicemeeter8x64", "voicemeeter8" })
                if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }

        static string Describe(Catalog cat, TuneChoice t)
        {
            Option s = cat.Styles.Find(delegate(Option o) { return o.Id == t.Style; }), g = cat.Guns.Find(delegate(Option o) { return o.Id == t.Gun; }), h = cat.Heads.Find(delegate(Option o) { return o.Id == t.Head; });
            if (s == null || g == null || h == null) return "";
            return s.Name + ", " + g.Name + " gun, " + h.Name;
        }
        public static string DescribeTune(Catalog cat, TuneChoice t) { return Describe(cat, t); }

        public static List<Check> Audit(Catalog cat, out Status st)
        {
            st = Setup.Check(cat);
            var list = new List<Check>();
            list.Add(new Check { Id = "tools", Title = "Audio tools", State = st.Stack ? Health.Ok : Health.Bad, NeedsSetup = !st.Stack,
                Detail = st.Stack ? "VB-CABLE, Equalizer APO and the Art Tune + device are installed." : "Not installed yet. Press Set up this PC." });
            bool data = st.Library && st.Engine;
            list.Add(new Check { Id = "data", Title = "Tune library and MW4 sound engine", State = data ? Health.Ok : Health.Bad, NeedsSetup = !data,
                Detail = data ? "Library " + st.LibraryVersion + " with the Charlie engine." : "Missing. Press Set up this PC." });

            if (st.Plus == null) list.Add(new Check { Id = "device", Title = "Art Tune + device", State = Health.Off, Detail = "Appears after setup." });
            else
            {
                bool ok = st.Format;
                list.Add(new Check { Id = "device", Title = "Art Tune + device", State = ok ? Health.Ok : Health.Warn, AutoFix = !ok,
                    Detail = ok ? "16 channel, 24 bit, 48000 Hz." : (st.Plus.State != 1 ? "The device is disabled." : "Wrong format (" + st.Plus.Channels + " channel, " + st.Plus.Bits + " bit, " + st.Plus.SampleRate + " Hz), so the game cannot send all 16 channels.") });
                list.Add(new Check { Id = "eapo", Title = "Equalizer APO on Art Tune +", State = st.EapoOnPlus ? Health.Ok : Health.Warn, AutoFix = !st.EapoOnPlus,
                    Detail = st.EapoOnPlus ? "Attached, enhancements on." : "Not attached, so the tune is bypassed and you hear stock audio." });
                list.Add(new Check { Id = "old", Title = "Old Art Tune device", State = st.OldDisabled ? Health.Ok : Health.Warn, AutoFix = !st.OldDisabled,
                    Detail = st.OldDisabled ? "Disabled, as MW4 needs." : "Still enabled. MW4 needs only Art Tune +." });
            }

            if (!Voicemeeter.IsInstalled) list.Add(new Check { Id = "mixer", Title = "Voicemeeter", State = Health.Off, Detail = st.Stack ? "Not installed (you kept your own mixer). Route Art Tune Unified Output to your headset there." : "Appears after setup." });
            else if (!st.Mixer) list.Add(new Check { Id = "mixer", Title = "Voicemeeter", State = Health.Warn, NeedsHeadset = true, Detail = "No headset picked yet, so the tuned sound goes nowhere." });
            else
            {
                bool run = MixerRunning();
                list.Add(new Check { Id = "mixer", Title = "Voicemeeter", State = run ? Health.Ok : Health.Warn, AutoFix = !run, Detail = run ? "Running and carrying the tune to your headset." : "Closed, so you hear nothing from the game." });
            }

            if (st.Stack)
            {
                bool fileOk = st.TuneSet && File.Exists(Path.Combine(Paths.ConfigDir, ConfigWriter.TuneInclude(cat, st.Current)));
                list.Add(new Check { Id = "tune", Title = "Active tune", State = fileOk ? Health.Ok : Health.Warn, AutoFix = !fileOk && data,
                    Detail = fileOk ? Describe(cat, st.Current) + (st.EqSet ? ", with your headset EQ." : ", flat EQ.") : "No tune is loaded." });
            }
            else list.Add(new Check { Id = "tune", Title = "Active tune", State = Health.Off, Detail = "Appears after setup." });

            if (st.Mixer && RouteChecked && !RouteOk)
                list.Add(new Check { Id = "route", Title = "Voicemeeter routing", State = Health.Warn, AutoFix = Headset != "", NeedsHeadset = Headset == "",
                    Detail = "Voicemeeter has lost its devices (this happens if it crashed or was force-closed), so the tune reaches nothing." });
            HeadsetPresent = true;
            if (st.Mixer && Headset != "")
            {
                string want = Headset;
                Endpoint hs = Audio.List().Find(delegate(Endpoint x) { return x.IsRender && string.Equals(x.FullName, want, StringComparison.OrdinalIgnoreCase); });
                HeadsetPresent = hs != null && hs.State == 1;
                list.Add(new Check { Id = "headset", Title = "Your headset", State = HeadsetPresent ? Health.Ok : Health.Warn, AutoFix = hs != null && hs.State == 2,
                    Detail = HeadsetPresent ? want + " is connected." : hs == null ? want + " is gone from Windows. If you replaced it, pick the new one on the Headset page." : hs.State == 2 ? want + " is disabled in Windows." : want + " is unplugged or switched off. It reconnects by itself when it comes back." });
            }
            if (st.Normal != null && st.Mixer)
            {
                bool isDef = string.Equals(Audio.DefaultId(true, 0), st.Normal.Id, StringComparison.OrdinalIgnoreCase);
                list.Add(new Check { Id = "default", Title = "Windows default device", State = isDef ? Health.Ok : Health.Warn, AutoFix = !isDef,
                    Detail = isDef ? "Normal Audio (everything except the game, untuned)." : "Not Normal Audio. Other apps may land on the wrong device." });
            }
            return list;
        }

        /// Repairs every check marked AutoFix. Returns what it did. Never runs the big installer by itself.
        public static List<string> Heal(Catalog cat, List<Check> checks, Status st, Action<string> say)
        {
            var done = new List<string>();
            bool game = GameRunning;
            foreach (Check c in checks)
            {
                if (!c.AutoFix || c.State == Health.Ok) continue;
                try
                {
                    if (c.Id == "eapo")
                    {
                        if (game) { if (!deferredSaid) { deferredSaid = true; done.Add("Equalizer APO needs re-attaching, but the game is running; it will be fixed when you close the game."); } continue; }
                        deferredSaid = false;
                        say("Re-attaching Equalizer APO to Art Tune + (sound drops for a few seconds)...");
                        Eapo.InstallOn(st.Plus.Guid); Eapo.RestartAudio(); done.Add("Re-attached Equalizer APO to Art Tune +.");
                        Audio.SetFormat(st.Plus.Id, 16, 24, 48000, 0);
                    }
                    else if (c.Id == "device")
                    {
                        if (st.Plus.State != 1) Audio.SetEnabled(st.Plus.Id, true);
                        int hr = Audio.SetFormat(st.Plus.Id, 16, 24, 48000, 0);
                        done.Add(hr == 0 ? "Set Art Tune + back to 16 channel, 24 bit, 48000 Hz." : "Could not set the Art Tune + format (0x" + hr.ToString("X8") + ").");
                    }
                    else if (c.Id == "old") { Audio.SetEnabled(st.Old.Id, false); done.Add("Disabled the old Art Tune device."); }
                    else if (c.Id == "mixer") { Process.Start(new ProcessStartInfo(Voicemeeter.Exe) { WorkingDirectory = Voicemeeter.InstallDir, UseShellExecute = true }); done.Add("Started Voicemeeter."); }
                    else if (c.Id == "tune")
                    {
                        TuneChoice t = st.Current; if (t.Style == null) { t.Style = cat.DefaultStyle; t.Gun = cat.DefaultGun; t.Head = cat.DefaultHead; }
                        if (!File.Exists(Path.Combine(Paths.ConfigDir, ConfigWriter.TuneInclude(cat, t)))) { t.Style = cat.DefaultStyle; t.Gun = cat.DefaultGun; t.Head = cat.DefaultHead; }
                        if (t.EqInclude != null && !File.Exists(Path.Combine(Paths.ConfigDir, t.EqInclude))) t.EqInclude = null;
                        ConfigWriter.Apply(cat, t, st.Plus == null ? null : st.Plus.Guid); done.Add("Reloaded the tune: " + Describe(cat, t) + ".");
                    }
                    else if (c.Id == "route")
                    {
                        say("Voicemeeter lost its routing. Setting it up again for " + Headset + "...");
                        Voicemeeter.Configure(Headset, Voicemeeter.Wdm, say);
                        if (st.Normal != null) Audio.SetDefault(st.Normal.Id, true);
                        RouteOk = true; done.Add("Rebuilt the Voicemeeter routing to " + Headset + ".");
                    }
                    else if (c.Id == "headset")
                    {
                        string want = Headset;
                        Endpoint hs = Audio.List().Find(delegate(Endpoint x) { return x.IsRender && string.Equals(x.FullName, want, StringComparison.OrdinalIgnoreCase); });
                        if (hs != null) { Audio.SetEnabled(hs.Id, true); done.Add("Re-enabled your headset in Windows."); }
                    }
                    else if (c.Id == "default") { Audio.SetDefault(st.Normal.Id, true); done.Add("Set Windows default playback back to Normal Audio."); }
                }
                catch (Exception ex) { done.Add("Could not fix " + c.Title + ": " + ex.Message); }
            }
            return done;
        }

        static bool deferredSaid;
        public static bool RouteChecked, RouteOk = true;

        /// Asks Voicemeeter what it is really routing (a little slower than the other checks, so not done on every beat).
        public static void CheckRoute()
        {
            try
            {
                if (!Voicemeeter.IsInstalled || !Voicemeeter.IsRunning) return;
                string[] r = Voicemeeter.ReadRoute();
                RouteOk = r[0].StartsWith("Art Tune Unified Output", StringComparison.OrdinalIgnoreCase) && r[1] != "";
                RouteChecked = true;
                if (RouteOk && Headset == "") { Headset = r[1]; SaveSettings(); }
            }
            catch { }
        }

        public static string Summary(List<Check> checks)
        {
            var parts = new List<string>();
            foreach (Check c in checks) parts.Add(c.Id + ": " + (c.State == Health.Ok ? "OK" : c.State == Health.Off ? "n/a" : "ATTENTION"));
            return string.Join(" | ", parts.ToArray());
        }
    }
}
