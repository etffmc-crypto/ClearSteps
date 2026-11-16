// ClearSteps command-line test tool (not shipped to friends; used to practise each step and log the result).
using System;
using System.Collections.Generic;
using System.IO;

namespace ClearSteps
{
    public static class Cli
    {
        static StreamWriter log;
        static void Say(string s) { Console.WriteLine(s); if (log != null) { log.WriteLine(s); log.Flush(); } }

        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "status";
            string logPath = args.Length > 1 ? args[1] : null;
            if (logPath != null) log = new StreamWriter(logPath, false);
            try
            {
                Catalog cat = Catalog.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.json")));
                List<Endpoint> eps = Audio.List();
                Endpoint plus = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Art Tune +" && e.State != 4; });
                Endpoint art = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Art Tune" && e.State != 4; });
                if (cmd == "devices")
                {
                    if (plus == null) { Say("FAIL: Art Tune + not found"); return 2; }
                    Say("eapo present: " + Eapo.IsEapoPresent() + ", on Art Tune + before: " + Eapo.IsInstalledOn(plus.Guid));
                    Eapo.InstallOn(plus.Guid);
                    Say("registered; restarting audio...");
                    Eapo.RestartAudio();
                    Say("on Art Tune + after restart: " + Eapo.IsInstalledOn(plus.Guid) + ", enhancements disabled: " + Eapo.EnhancementsDisabled(plus.Guid));
                    Say("set format hr=0x" + Audio.SetFormat(plus.Id, 16, 24, 48000, 0).ToString("X8"));
                    if (art != null) Say("disable Art Tune hr=0x" + Audio.SetEnabled(art.Id, false).ToString("X8"));
                }
                if (cmd == "vmlist") { foreach (VmDevice d in Voicemeeter.OutputDevices()) Say(d.Type + "  " + d.Name); return 0; }
                if (cmd == "vm")
                {
                    string want = args.Length > 2 ? args[2] : "Speakers";
                    VmDevice pick = Voicemeeter.OutputDevices().Find(delegate(VmDevice d) { return d.Type == Voicemeeter.Wdm && d.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0 && d.Name.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) < 0; });
                    if (pick == null) { Say("FAIL: no output matching " + want); return 2; }
                    Say("result: " + Voicemeeter.Configure(pick.Name, pick.Type, Say));
                    Say("default before: " + Audio.DefaultId(true, 0));
                    Endpoint normal = eps.Find(delegate(Endpoint e) { return e.IsRender && e.Name == "Normal Audio" && e.State == 1; });
                    if (normal != null) Say("set default Normal Audio hr=0x" + Audio.SetDefault(normal.Id, true).ToString("X8"));
                }
                if (cmd == "devices" || cmd == "config")
                {
                    var t = new TuneChoice { Style = cat.DefaultStyle, Gun = cat.DefaultGun, Head = cat.DefaultHead, BoostDb = 0 };
                    if (cmd == "config" && args.Length > 4) { t.Style = args[2]; t.Gun = args[3]; t.Head = args[4]; }
                    string backup = ConfigWriter.Apply(cat, t, plus == null ? null : plus.Guid);
                    Say("config written, backup: " + backup);
                }
                foreach (Endpoint e in Audio.List()) if (e.State != 4 && (e.Name.StartsWith("Art Tune") || e.Name == "Normal Audio")) Say(e.ToString());
                TuneChoice cur = ConfigWriter.ReadCurrent(cat);
                Say("current tune: " + cur.Style + " / " + cur.Gun + " / " + cur.Head + " eq=" + cur.EqInclude + " boost=" + cur.BoostDb);
                Say("library: " + Paths.LibraryVersion + ", charlie dll: " + File.Exists(Paths.CharlieDll));
                Say("OK");
                return 0;
            }
            catch (Exception ex) { Say("ERROR: " + ex); return 1; }
            finally { if (log != null) log.Close(); }
        }
    }
}
