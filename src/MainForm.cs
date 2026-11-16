// ClearSteps - the window. C# 5 compatible WinForms, no designer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClearSteps
{
    public class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(18, 20, 24), Panel2 = Color.FromArgb(28, 31, 37), Ink = Color.FromArgb(232, 234, 238),
            Dim = Color.FromArgb(150, 156, 168), Accent = Color.FromArgb(72, 214, 138), Warn = Color.FromArgb(240, 180, 70);

        Catalog cat; Status st = new Status();
        readonly List<Button> nav = new List<Button>(); readonly List<Panel> pages = new List<Panel>();
        TextBox log; Label[] checks; Button setupBtn;
        ComboBox headsetBox; Label headsetState;
        ComboBox styleBox, gunBox, headBox; Label styleText, gunText, headText, tuneState; NumericUpDown boost;
        Label eqState, aboutState; bool loading;

        public MainForm()
        {
            Text = "ClearSteps " + Setup.AppVersion + "  -  MW4 footstep audio setup";
            ClientSize = new Size(940, 640); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg; ForeColor = Ink; Font = new Font("Segoe UI", 10f);
            cat = Setup.LoadCatalog(false);

            var side = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Panel2 };
            side.Controls.Add(new Label { Text = "ClearSteps", Font = new Font("Segoe UI Semibold", 17f), ForeColor = Accent, Location = new Point(18, 16), AutoSize = true });
            side.Controls.Add(new Label { Text = "Free MW4 audio tune by ArtIsWar,\nset up for you.", ForeColor = Dim, Font = new Font("Segoe UI", 8.5f), Location = new Point(20, 52), AutoSize = true });
            string[] names = { "1  Set up this PC", "2  Pick your headset", "3  Choose your tune", "4  Headphone EQ", "5  In-game settings", "Updates and credits" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = new Button { Text = names[i], FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(10, 104 + i * 46), Size = new Size(190, 40), ForeColor = Ink, BackColor = Panel2, Padding = new Padding(8, 0, 0, 0) };
                b.FlatAppearance.BorderSize = 0; b.Click += delegate { ShowPage(idx); };
                nav.Add(b); side.Controls.Add(b);
            }
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(26, 20, 26, 20) };
            Controls.Add(host); Controls.Add(side);
            pages.Add(BuildSetup()); pages.Add(BuildHeadset()); pages.Add(BuildTune()); pages.Add(BuildEq()); pages.Add(BuildGame()); pages.Add(BuildAbout());
            foreach (Panel p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }
            Shown += delegate { RefreshState(); ShowPage(st.ReadyForHeadset ? (st.Mixer ? 2 : 1) : 0); Background(delegate { CheckOnline(true); }, false); };
        }

        // ---- small builders ----
        Label H(Panel p, string text) { var l = new Label { Text = text, Font = new Font("Segoe UI Semibold", 15f), Location = new Point(0, 0), AutoSize = true }; p.Controls.Add(l); return l; }
        Label T(Panel p, string text, int y, int h) { var l = new Label { Text = text, ForeColor = Dim, Location = new Point(0, y), Size = new Size(660, h) }; p.Controls.Add(l); return l; }
        Label L(Panel p, string text, int y) { var l = new Label { Text = text, Location = new Point(0, y), AutoSize = true }; p.Controls.Add(l); return l; }
        Button B(Panel p, string text, int x, int y, int w, bool primary)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, Location = new Point(x, y), Size = new Size(w, 38), BackColor = primary ? Accent : Panel2, ForeColor = primary ? Color.Black : Ink };
            b.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(70, 76, 88); p.Controls.Add(b); return b;
        }
        ComboBox C(Panel p, int y, int w) { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(0, y), Width = w, BackColor = Panel2, ForeColor = Ink, FlatStyle = FlatStyle.Flat }; p.Controls.Add(c); return c; }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Count; k++) { pages[k].Visible = k == i; nav[k].BackColor = k == i ? Color.FromArgb(44, 49, 58) : Panel2; nav[k].ForeColor = k == i ? Accent : Ink; }
        }

        void Ui(Action a) { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); }
        void Say(string s) { Ui(delegate { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n"); }); }

        void Background(Action work, bool lockUi)
        {
            if (lockUi) foreach (Panel p in pages) p.Enabled = false;
            var t = new Thread(delegate()
            {
                string err = null;
                try { work(); } catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    if (lockUi) foreach (Panel p in pages) p.Enabled = true;
                    RefreshState();
                    if (err != null && lockUi) { log.AppendText("PROBLEM: " + err + "\r\n"); MessageBox.Show(this, err, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                });
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        // ---- page 1: set up ----
        Panel BuildSetup()
        {
            var p = new Panel();
            H(p, "Set up this PC");
            T(p, "One button installs ArtIsWar's free audio stack from his own site, then does the Windows sound settings his guide has you do by hand. Takes about 3 minutes. Your sound will cut out briefly.", 36, 44);
            string[] items = { "Audio tools installed (VB-CABLE, Equalizer APO, Voicemeeter)", "Tune library downloaded", "MW4 sound engine installed", "Equalizer APO attached to Art Tune +", "Art Tune + set to 16 channel, 24 bit, 48000 Hz", "Old Art Tune device disabled" };
            checks = new Label[items.Length];
            for (int i = 0; i < items.Length; i++) { checks[i] = new Label { Text = items[i], Tag = items[i], Location = new Point(0, 90 + i * 26), AutoSize = true }; p.Controls.Add(checks[i]); }
            setupBtn = B(p, "Set up this PC", 0, 256, 240, true);
            setupBtn.Click += delegate
            {
                Background(delegate
                {
                    Status s = Setup.Check(cat);
                    if (!s.Stack || !s.Library || !s.Engine)
                    {
                        Say("If a small HeSuVi pop-up appears, click Yes, then close HeSuVi and the browser tab it opens.");
                        Setup.RunOfficialInstaller(cat, "1", Say);
                        Say("Installer finished.");
                    }
                    Setup.ConfigureDevices(cat, Say);
                    Say("This PC is set up. Next: pick your headset (step 2).");
                }, true);
            };
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(0, 310), Size = new Size(660, 250), BackColor = Color.FromArgb(12, 13, 16), ForeColor = Dim, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            p.Controls.Add(log);
            return p;
        }

        // ---- page 2: headset ----
        Panel BuildHeadset()
        {
            var p = new Panel();
            H(p, "Pick your headset");
            T(p, "The tuned game sound has to be sent to your headset. Voicemeeter does that; this sets it up for you. After this, Windows plays everything else (Discord, browser, music) through \"Normal Audio\", untuned, to the same headset.", 36, 62);
            Button find = B(p, "Find my headsets", 0, 108, 200, false);
            headsetBox = C(p, 160, 480);
            Button use = B(p, "Use this headset", 0, 204, 200, true);
            headsetState = T(p, "", 256, 120);
            find.Click += delegate
            {
                Background(delegate
                {
                    List<VmDevice> devs = Voicemeeter.OutputDevices();
                    Ui(delegate
                    {
                        headsetBox.Items.Clear();
                        foreach (VmDevice d in devs) if (d.Type == Voicemeeter.Wdm && d.Name.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) < 0) headsetBox.Items.Add(d);
                        if (headsetBox.Items.Count > 0) headsetBox.SelectedIndex = 0;
                        headsetState.Text = headsetBox.Items.Count + " found. Pick the one you wear when you play, then press Use this headset.";
                    });
                }, true);
            };
            use.Click += delegate
            {
                var d = headsetBox.SelectedItem as VmDevice;
                if (d == null) { MessageBox.Show(this, "Press Find my headsets first.", "ClearSteps"); return; }
                Background(delegate
                {
                    Voicemeeter.Configure(d.Name, d.Type, Say);
                    Status s = Setup.Check(cat);
                    if (s.Normal != null) Audio.SetDefault(s.Normal.Id, true);
                    Ui(delegate { headsetState.ForeColor = Accent; headsetState.Text = "Done. Tuned game sound and normal sound both go to:\n" + d.Name + "\n\nIf you hear crackling later, open Voicemeeter and pick the same headset under A1 as KS instead of WDM."; });
                }, true);
            };
            return p;
        }

        // ---- page 3: tune ----
        Panel BuildTune()
        {
            var p = new Panel();
            H(p, "Choose your tune");
            T(p, cat.Title + ". " + cat.Note + " Changes apply instantly, even mid-game.", 36, 44);
            L(p, "Tuning style", 88); styleBox = C(p, 110, 300); styleText = T(p, "", 142, 58);
            L(p, "Your own gun volume", 204); gunBox = C(p, 226, 300); gunText = T(p, "", 258, 24);
            L(p, "Head model (how surround is folded into your headset)", 290); headBox = C(p, 312, 300); headText = T(p, "", 344, 40);
            L(p, "Extra volume boost (dB). Leave at 0 unless it is too quiet.", 390);
            boost = new NumericUpDown { Location = new Point(0, 414), Width = 80, Minimum = 0, Maximum = 20, BackColor = Panel2, ForeColor = Ink };
            p.Controls.Add(boost);
            foreach (Option o in cat.Styles) styleBox.Items.Add(o);
            foreach (Option o in cat.Guns) gunBox.Items.Add(o);
            foreach (Option o in cat.Heads) headBox.Items.Add(o);
            tuneState = T(p, "", 470, 60);
            EventHandler changed = delegate { ApplyTune(); };
            styleBox.SelectedIndexChanged += changed; gunBox.SelectedIndexChanged += changed; headBox.SelectedIndexChanged += changed; boost.ValueChanged += changed;
            Button reset = B(p, "Back to recommended", 320, 106, 200, false);
            reset.Click += delegate { loading = true; Pick(styleBox, cat.Styles, cat.DefaultStyle); Pick(gunBox, cat.Guns, cat.DefaultGun); Pick(headBox, cat.Heads, cat.DefaultHead); loading = false; ApplyTune(); };
            return p;
        }

        static void Pick(ComboBox box, List<Option> list, string id) { int i = list.FindIndex(delegate(Option o) { return o.Id == id; }); box.SelectedIndex = i < 0 ? 0 : i; }

        void ApplyTune()
        {
            var s = styleBox.SelectedItem as Option; var g = gunBox.SelectedItem as Option; var h = headBox.SelectedItem as Option;
            if (s != null) styleText.Text = s.Text;
            if (g != null) gunText.Text = g.Text;
            if (h != null) headText.Text = h.Text;
            if (loading || s == null || g == null || h == null) return;
            if (!st.Stack || st.Plus == null) { tuneState.ForeColor = Warn; tuneState.Text = "Do step 1 first; then your choice here is saved."; return; }
            try
            {
                var t = new TuneChoice { Style = s.Id, Gun = g.Id, Head = h.Id, EqInclude = st.Current.EqInclude, BoostDb = (double)boost.Value };
                ConfigWriter.Apply(cat, t, st.Plus.Guid);
                st.Current = t;
                tuneState.ForeColor = Accent; tuneState.Text = "Active now: " + s.Name + ", " + g.Name + " gun, " + h.Name + (boost.Value > 0 ? ", +" + boost.Value + " dB" : "") + ".";
            }
            catch (Exception ex) { tuneState.ForeColor = Warn; tuneState.Text = ex.Message; }
        }

        // ---- page 4: headphone EQ ----
        Panel BuildEq()
        {
            var p = new Panel();
            H(p, "Headphone EQ");
            T(p, "This matches the tune to your exact headset. It takes two minutes on a free website and makes a real difference. Skip it and the tune still works with a flat EQ.", 36, 44);
            T(p, "1.  Open the site and click your headset in the list (search by name).\n2.  Click the Equalizer tab, then Upload Target, and pick the file ClearSteps shows you (" + cat.TargetFile + ").\n3.  Click AutoEQ, then Export. It saves a text file.\n4.  Come back here and import that file.", 90, 110);
            Button a = B(p, "Open the EQ site", 0, 210, 190, false);
            a.Click += delegate { Process.Start(cat.SquigUrl); };
            Button b = B(p, "Show me the target file", 200, 210, 210, false);
            b.Click += delegate
            {
                string f = Path.Combine(Path.Combine(Paths.Library, cat.Folder), cat.TargetFile);
                if (!File.Exists(f)) { MessageBox.Show(this, "Do step 1 first so the tune library is installed.", "ClearSteps"); return; }
                try { Clipboard.SetText(f); } catch { }
                Process.Start("explorer.exe", "/select,\"" + f + "\"");
                MessageBox.Show(this, "The target file is highlighted in the folder that just opened, and its path is copied.\nIn the site's Upload Target window, paste (Ctrl+V) and press Open.", "ClearSteps");
            };
            Button c = B(p, "Import my EQ file...", 0, 262, 190, true);
            c.Click += delegate
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Pick the EQ file the site exported"; dlg.Filter = "EQ text files (*.txt)|*.txt";
                    dlg.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        string name = "MW4_V1_" + Path.GetFileNameWithoutExtension(dlg.FileName).Replace(" Filters", "").Replace(" ", "_");
                        st.Current.EqInclude = ConfigWriter.ImportEq(dlg.FileName, name);
                        SaveCurrent(); RefreshState();
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            Button d = B(p, "Use flat EQ instead", 200, 262, 210, false);
            d.Click += delegate { st.Current.EqInclude = null; SaveCurrent(); RefreshState(); };
            eqState = T(p, "", 320, 60);
            return p;
        }

        void SaveCurrent()
        {
            if (!st.Stack || st.Plus == null) { MessageBox.Show(this, "Do step 1 first.", "ClearSteps"); return; }
            TuneChoice t = st.Current;
            if (t.Style == null) { t.Style = cat.DefaultStyle; t.Gun = cat.DefaultGun; t.Head = cat.DefaultHead; }
            try { ConfigWriter.Apply(cat, t, st.Plus.Guid); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        // ---- page 5: in-game ----
        Panel BuildGame()
        {
            var p = new Panel();
            H(p, "In-game settings");
            T(p, "Set these once in MW4 (Settings > Audio). The ones marked REQUIRED break the tune if they are wrong. They apply to Warzone, DMZ and multiplayer.", 36, 44);
            var list = new Panel { Location = new Point(0, 86), Size = new Size(676, 476), AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            int y = 0;
            foreach (GameSetting g in cat.GameSettings)
            {
                bool req = g.Level == "required";
                list.Controls.Add(new Label { Text = g.Name, Location = new Point(0, y), Size = new Size(280, 20), Font = new Font("Segoe UI Semibold", 10f) });
                list.Controls.Add(new Label { Text = g.Value, Location = new Point(284, y), Size = new Size(170, 20), ForeColor = Accent, Font = new Font("Segoe UI Semibold", 10f) });
                list.Controls.Add(new Label { Text = g.Level.ToUpperInvariant(), Location = new Point(458, y + 2), Size = new Size(130, 18), ForeColor = req ? Warn : Dim, Font = new Font("Segoe UI", 8f) });
                list.Controls.Add(new Label { Text = g.Why, Location = new Point(0, y + 20), Size = new Size(640, 18), ForeColor = Dim, Font = new Font("Segoe UI", 8.5f) });
                y += 46;
            }
            p.Controls.Add(list);
            return p;
        }

        // ---- page 6: updates and credits ----
        Panel BuildAbout()
        {
            var p = new Panel();
            H(p, "Updates and credits");
            aboutState = T(p, "", 40, 66);
            Button u = B(p, "Check for updates", 0, 112, 200, true);
            u.Click += delegate { Background(delegate { CheckOnline(false); }, true); };
            Button lib = B(p, "Re-download tune library", 210, 112, 220, false);
            lib.Click += delegate
            {
                if (MessageBox.Show(this, "This runs ArtIsWar's installer to fetch the newest tune library. It backs up the current one first. Your headset EQ is kept.", "ClearSteps", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                ShowPage(0);
                Background(delegate { Setup.RunOfficialInstaller(cat, "3", Say); Say("Library refreshed."); }, true);
            };
            T(p, "The tune, the sound engine and the installer are the work of ArtIsWar, who gives them away free. ClearSteps is an unofficial helper made by a fan: it downloads everything from his own site and only does the clicking for you. It is free and must stay free.\n\nIf you want the polished, supported version with profiles, no Voicemeeter and stream audio, buy his app ArtTuneKit.", 176, 130);
            string[] text = { "Watch the MW4 guide", "ArtIsWar's tunes (GitHub)", "ArtTuneKit (his paid app)", "His Discord (support for the tune)", "ClearSteps page" };
            string[] urls = { cat.Video, cat.Repo, cat.PaidApp, cat.Discord, Setup.RepoPage };
            for (int i = 0; i < text.Length; i++)
            {
                string url = urls[i];
                var l = new LinkLabel { Text = text[i], Location = new Point(0, 316 + i * 28), AutoSize = true, LinkColor = Accent, ActiveLinkColor = Ink };
                l.LinkClicked += delegate { Process.Start(url); }; p.Controls.Add(l);
            }
            return p;
        }

        void CheckOnline(bool quiet)
        {
            Catalog fresh = Setup.LoadCatalog(true);
            Setup.UpdateInfo up = null; string err = null;
            try { up = Setup.CheckForUpdate(); } catch (Exception ex) { err = ex.Message; }
            Ui(delegate
            {
                if (up != null)
                {
                    if (MessageBox.Show(this, "ClearSteps " + up.Version + " is available.\n\n" + up.Notes + "\n\nUpdate now? It takes a few seconds and reopens by itself.", "ClearSteps update", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { Setup.ApplyUpdate(up); } catch (Exception ex) { MessageBox.Show(this, "Update failed: " + ex.Message, "ClearSteps"); }
                    }
                }
                else if (!quiet) MessageBox.Show(this, err == null ? "You have the newest version." : "Could not check for updates right now (" + err + ").", "ClearSteps");
                if (fresh.Version > cat.Version && !quiet) MessageBox.Show(this, "New tune options were downloaded. Close and reopen ClearSteps to see them.", "ClearSteps");
            });
        }

        // ---- state to screen ----
        void RefreshState()
        {
            try { st = Setup.Check(cat); } catch { }
            bool[] ok = { st.Stack, st.Library, st.Engine, st.EapoOnPlus, st.Format, st.OldDisabled };
            for (int i = 0; i < checks.Length; i++) { checks[i].Text = (ok[i] ? "✓  " : "•  ") + (string)checks[i].Tag; checks[i].ForeColor = ok[i] ? Accent : Dim; }
            bool all = st.ReadyForHeadset && st.OldDisabled;
            setupBtn.Text = all ? "Run setup again (repair)" : "Set up this PC";
            nav[0].Text = (all ? "✓" : "1") + "  Set up this PC";
            nav[1].Text = (st.Mixer ? "✓" : "2") + "  Pick your headset";
            nav[2].Text = (st.TuneSet ? "✓" : "3") + "  Choose your tune";
            nav[3].Text = (st.EqSet ? "✓" : "4") + "  Headphone EQ";
            loading = true;
            Pick(styleBox, cat.Styles, st.Current.Style ?? cat.DefaultStyle); Pick(gunBox, cat.Guns, st.Current.Gun ?? cat.DefaultGun); Pick(headBox, cat.Heads, st.Current.Head ?? cat.DefaultHead);
            boost.Value = (decimal)Math.Max(0, Math.Min(20, Math.Round(st.Current.BoostDb)));
            loading = false;
            var s = styleBox.SelectedItem as Option; var g = gunBox.SelectedItem as Option; var h = headBox.SelectedItem as Option;
            if (st.TuneSet && s != null && g != null && h != null) { tuneState.ForeColor = Accent; tuneState.Text = "Active now: " + s.Name + ", " + g.Name + " gun, " + h.Name + "."; }
            eqState.ForeColor = st.EqSet ? Accent : Dim;
            eqState.Text = st.EqSet ? "Using your headset EQ: " + Path.GetFileNameWithoutExtension(st.Current.EqInclude) : "Using the flat EQ (works, but not matched to your headset).";
            if (headsetState.Text.Length == 0 && st.Mixer) headsetState.Text = "A headset is already set up. Press Find my headsets to change it.";
            aboutState.Text = "ClearSteps " + Setup.AppVersion + "   |   tune library " + (st.LibraryVersion == "" ? "not installed" : st.LibraryVersion) + "   |   tune list " + cat.Updated + "\nUpdates are checked each time the app opens.";
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (!Environment.Is64BitOperatingSystem) { MessageBox.Show("ClearSteps needs 64-bit Windows 10 or 11.", "ClearSteps"); return; }
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message, "ClearSteps"); };
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 2 && args[1] == "--selftest")
            {
                // Self-check used while building: update check, update download, EQ import. Writes a log and exits.
                var sb = new System.Text.StringBuilder();
                try
                {
                    Catalog c = Setup.LoadCatalog(true);
                    sb.AppendLine("version " + Setup.AppVersion + ", catalog v" + c.Version);
                    Setup.UpdateInfo u = Setup.CheckForUpdate();
                    sb.AppendLine("update: " + (u == null ? "none" : u.Version + " " + u.Url));
                    if (args.Length > 3)
                    {
                        string inc = ConfigWriter.ImportEq(args[3], "MW4_V1_SelfTest");
                        TuneChoice t = ConfigWriter.ReadCurrent(c); t.EqInclude = inc;
                        Status s = Setup.Check(c);
                        ConfigWriter.Apply(c, t, s.Plus == null ? null : s.Plus.Guid);
                        sb.AppendLine("eq imported: " + ConfigWriter.ReadCurrent(c).EqInclude);
                        t.EqInclude = null; ConfigWriter.Apply(c, t, s.Plus == null ? null : s.Plus.Guid);
                        sb.AppendLine("eq back to: " + (ConfigWriter.ReadCurrent(c).EqInclude ?? "flat"));
                    }
                    File.WriteAllText(args[2], sb.ToString());
                    if (u != null) Setup.ApplyUpdate(u);
                }
                catch (Exception ex) { sb.AppendLine("ERROR " + ex); File.WriteAllText(args[2], sb.ToString()); }
                return;
            }
            if (args.Length > 2 && args[1] == "--shots")
            {
                // Self-check used while building: draws every page to a PNG and exits.
                var f = new MainForm(); f.Show(); f.RefreshState(); Application.DoEvents();
                for (int i = 0; i < f.pages.Count; i++)
                {
                    f.ShowPage(i); Application.DoEvents();
                    using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize)); bmp.Save(Path.Combine(args[2], "page" + (i + 1) + ".png")); }
                }
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
