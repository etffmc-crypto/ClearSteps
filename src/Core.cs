// ClearSteps - catalog, paths and the Equalizer APO config writer. C# 5 compatible.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ClearSteps
{
    public class Option { public string Id, Name, Tag, Text; public override string ToString() { return string.IsNullOrEmpty(Tag) ? Name : Name + "  (" + Tag + ")"; } }
    public class GameSetting { public string Name, Value, Level, Why; }

    public class Catalog
    {
        public int Version;
        public string Updated, Video, VideoTitle, OriginalGuide, Repo, LibraryRelease, InstallerUrl, Discord, PaidApp;
        public string Title, Note, Device, Folder, FilePattern, TargetFile, FlatEq, SquigUrl;
        public string DefaultStyle, DefaultGun, DefaultHead;
        public List<Option> Styles = new List<Option>(), Guns = new List<Option>(), Heads = new List<Option>();
        public List<GameSetting> GameSettings = new List<GameSetting>();

        static string S(IDictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
        static IDictionary<string, object> D(IDictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as IDictionary<string, object> : null; }
        static List<Option> Opts(IDictionary<string, object> d, string k)
        {
            var list = new List<Option>(); object v;
            if (d == null || !d.TryGetValue(k, out v) || !(v is IEnumerable)) return list;
            foreach (object o in (IEnumerable)v)
            {
                var x = o as IDictionary<string, object>; if (x == null) continue;
                list.Add(new Option { Id = S(x, "id"), Name = S(x, "name"), Tag = S(x, "tag"), Text = S(x, "text") });
            }
            return list;
        }

        public static Catalog Parse(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var c = new Catalog();
            int.TryParse(S(root, "catalogVersion"), out c.Version);
            c.Updated = S(root, "updated");
            var src = D(root, "source");
            c.Video = S(src, "video"); c.VideoTitle = S(src, "videoTitle"); c.OriginalGuide = S(src, "originalGuide");
            c.Repo = S(src, "repo"); c.LibraryRelease = S(src, "libraryRelease"); c.InstallerUrl = S(src, "installerUrl");
            c.Discord = S(src, "discord"); c.PaidApp = S(src, "paidApp");
            var g = D(root, "game");
            c.Title = S(g, "title"); c.Note = S(g, "note"); c.Device = S(g, "device"); c.Folder = S(g, "folder");
            c.FilePattern = S(g, "filePattern"); c.TargetFile = S(g, "targetFile"); c.FlatEq = S(g, "flatEq"); c.SquigUrl = S(g, "squigUrl");
            var def = D(g, "defaults");
            c.DefaultStyle = S(def, "style"); c.DefaultGun = S(def, "gun"); c.DefaultHead = S(def, "head");
            c.Styles = Opts(g, "styles"); c.Guns = Opts(g, "guns"); c.Heads = Opts(g, "heads");
            object gs;
            if (g != null && g.TryGetValue("gameSettings", out gs) && gs is IEnumerable)
                foreach (object o in (IEnumerable)gs)
                {
                    var x = o as IDictionary<string, object>; if (x == null) continue;
                    c.GameSettings.Add(new GameSetting { Name = S(x, "name"), Value = S(x, "value"), Level = S(x, "level"), Why = S(x, "why") });
                }
            if (c.Styles.Count == 0 || c.Guns.Count == 0 || c.Heads.Count == 0 || c.FilePattern == "") throw new InvalidDataException("catalog is missing the tune lists");
            return c;
        }

        public string TuneFile(string style, string gun, string head)
        {
            return FilePattern.Replace("{style}", style).Replace("{gun}", gun).Replace("{head}", head);
        }
    }

    public static class Paths
    {
        public static string EapoRoot
        {
            get
            {
                try
                {
                    using (var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\EqualizerAPO"))
                    {
                        var v = k == null ? null : k.GetValue("InstallPath") as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
                catch { }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
            }
        }
        public static string ConfigDir { get { return Path.Combine(EapoRoot, "config"); } }
        public static string ConfigFile { get { return Path.Combine(ConfigDir, "config.txt"); } }
        public static string ArtTuneDB { get { return Path.Combine(ConfigDir, "ArtTuneDB"); } }
        public static string Library { get { return Path.Combine(ArtTuneDB, "library"); } }
        public static string BoostFile { get { return Path.Combine(ArtTuneDB, "boost.txt"); } }
        /// Our own folder inside the E-APO config dir. A library refresh never touches it, so EQs kept here survive updates.
        public static string OurDir { get { return Path.Combine(ConfigDir, "ClearSteps"); } }
        public static string OurEqDir { get { return Path.Combine(OurDir, "eq"); } }
        public static string CharlieDll { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"VSTPlugins\ArtTuneKit\atk_spatial_engine_charlie_v1_1_0.dll"); } }
        public static string BackupDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClearSteps Backups"); } }
        public static string LibraryVersion
        {
            get { try { return File.ReadAllText(Path.Combine(Library, "version.txt")).Trim(); } catch { return ""; } }
        }
    }

    public class TuneChoice { public string Style, Gun, Head, EqInclude; public double BoostDb; }

    public static class ConfigWriter
    {
        public const string Marker16 = "# ---- 16ch profile (Art Tune +)";

        static string Backup(string file)
        {
            if (!File.Exists(file)) return "";
            Directory.CreateDirectory(Paths.BackupDir);
            string dest = Path.Combine(Paths.BackupDir, Path.GetFileNameWithoutExtension(file) + "-" + DateTime.Now.ToString("yyyy-MM-dd_HHmmss") + ".txt");
            File.Copy(file, dest, true);
            return dest;
        }

        /// Include path as E-APO wants it: relative to the config folder.
        public static string TuneInclude(Catalog c, TuneChoice t)
        {
            return @"ArtTuneDB\library\" + c.Folder + @"\" + c.TuneFile(t.Style, t.Gun, t.Head);
        }
        public static string FlatEqInclude(Catalog c) { return @"ArtTuneDB\library\" + c.Folder + @"\" + c.FlatEq; }

        /// Rewrites only the 16-channel (Art Tune +) section of config.txt; everything above it is kept byte for byte.
        /// plusGuid is the Art Tune + render endpoint GUID with braces, used when the section has to be created.
        public static string Apply(Catalog c, TuneChoice t, string plusGuid)
        {
            string tuneRel = TuneInclude(c, t);
            if (!File.Exists(Path.Combine(Paths.ConfigDir, tuneRel))) throw new FileNotFoundException("That tune file is not in the installed library. Update the library first.", tuneRel);
            string eqRel = string.IsNullOrEmpty(t.EqInclude) ? FlatEqInclude(c) : t.EqInclude;
            if (!File.Exists(Path.Combine(Paths.ConfigDir, eqRel))) throw new FileNotFoundException("The headphone EQ file is missing.", eqRel);

            var lines = new List<string>();
            if (File.Exists(Paths.ConfigFile)) lines.AddRange(File.ReadAllLines(Paths.ConfigFile));
            int start = lines.FindIndex(delegate(string l) { return l.StartsWith(Marker16, StringComparison.OrdinalIgnoreCase); });
            string deviceLine = null;
            if (start >= 0)
            {
                for (int i = start; i < lines.Count; i++)
                    if (lines[i].StartsWith("Device:", StringComparison.OrdinalIgnoreCase)) { deviceLine = lines[i]; break; }
                lines.RemoveRange(start, lines.Count - start);
            }
            if (deviceLine == null)
            {
                if (string.IsNullOrEmpty(plusGuid)) throw new InvalidOperationException("The Art Tune + device was not found, so the tune cannot be attached to it.");
                deviceLine = "Device: Art Tune + VB-Audio Virtual Cable " + plusGuid.ToLowerInvariant();
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Trim() == "") lines.RemoveAt(lines.Count - 1);
            if (lines.Count == 0) lines.Add("# ArtTuneDB config.txt");
            lines.Add("");
            lines.Add(Marker16 + " - no HeSuVi, needs the bundled VST ----");
            lines.Add("# Written by ClearSteps " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ". Tune by ArtIsWar (youtube.com/artiswar).");
            lines.Add(deviceLine);
            lines.Add("# 16ch TUNE");
            lines.Add("Include: " + tuneRel);
            lines.Add("# EQ");
            lines.Add("Include: " + eqRel);
            lines.Add("# OUTPUT BOOST");
            lines.Add(@"Include: ArtTuneDB\boost.txt");

            string backup = Backup(Paths.ConfigFile);
            File.WriteAllLines(Paths.ConfigFile, lines.ToArray(), new UTF8Encoding(false));
            WriteBoost(t.BoostDb);
            return backup;
        }

        public static void WriteBoost(double db)
        {
            if (db < 0) db = 0; if (db > 20) db = 20;
            Directory.CreateDirectory(Paths.ArtTuneDB);
            File.WriteAllLines(Paths.BoostFile, new[] {
                "# ArtTuneDB Output Boost -- set your dB below and save. 0 dB = off.",
                "# config.txt includes this last on every chain; E-APO reloads it live.",
                "Preamp: " + db.ToString("0.#", CultureInfo.InvariantCulture) + " dB" }, new UTF8Encoding(false));
        }

        public static double ReadBoost()
        {
            try
            {
                foreach (string l in File.ReadAllLines(Paths.BoostFile))
                {
                    string s = l.Trim();
                    if (!s.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase)) continue;
                    s = s.Substring(7).Replace("dB", "").Trim();
                    double d; if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                }
            }
            catch { }
            return 0;
        }

        /// Reads back what the 16ch section currently points at (null entries when unset).
        public static TuneChoice ReadCurrent(Catalog c)
        {
            var t = new TuneChoice { BoostDb = ReadBoost() };
            if (!File.Exists(Paths.ConfigFile)) return t;
            string[] lines = File.ReadAllLines(Paths.ConfigFile);
            int start = Array.FindIndex(lines, delegate(string l) { return l.StartsWith(Marker16, StringComparison.OrdinalIgnoreCase); });
            if (start < 0) return t;
            string prefix = (@"ArtTuneDB\library\" + c.Folder + @"\").ToLowerInvariant();
            int includeNo = 0;
            for (int i = start; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (!l.StartsWith("Include:", StringComparison.OrdinalIgnoreCase)) continue;
                string path = l.Substring(8).Trim(); includeNo++;
                if (includeNo == 1 && path.ToLowerInvariant().StartsWith(prefix))
                {
                    string file = Path.GetFileNameWithoutExtension(path);
                    foreach (Option s in c.Styles) foreach (Option g in c.Guns) foreach (Option h in c.Heads)
                        if (string.Equals(Path.GetFileNameWithoutExtension(c.TuneFile(s.Id, g.Id, h.Id)), file, StringComparison.OrdinalIgnoreCase))
                        { t.Style = s.Id; t.Gun = g.Id; t.Head = h.Id; }
                }
                else if (includeNo == 2 && !path.EndsWith(@"library\", StringComparison.OrdinalIgnoreCase)) t.EqInclude = path;
            }
            return t;
        }

        /// Copies a squig.link export into our EQ folder with its Preamp line switched off (so the tune is not made
        /// quieter, as the guide says) and returns the Include path relative to the config folder.
        public static string ImportEq(string sourceFile, string niceName)
        {
            string[] src = File.ReadAllLines(sourceFile);
            bool looksLikeEq = false;
            var outLines = new List<string>();
            outLines.Add("# Headphone EQ imported by ClearSteps from " + Path.GetFileName(sourceFile));
            foreach (string l in src)
            {
                string s = l.Trim();
                if (s.StartsWith("Filter", StringComparison.OrdinalIgnoreCase)) looksLikeEq = true;
                if (s.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase)) outLines.Add("# " + s + "   (switched off on purpose)");
                else outLines.Add(l);
            }
            if (!looksLikeEq) throw new InvalidDataException("That file does not look like an EQ export (no Filter lines).");
            var sb = new StringBuilder();
            foreach (char ch in niceName) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            string name = sb.Length == 0 ? "MyHeadset" : sb.ToString();
            Directory.CreateDirectory(Paths.OurEqDir);
            File.WriteAllLines(Path.Combine(Paths.OurEqDir, name + ".txt"), outLines.ToArray(), new UTF8Encoding(false));
            return @"ClearSteps\eq\" + name + ".txt";
        }
    }
}
