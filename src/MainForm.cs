// ClearSteps - the window: a guardian dashboard plus tune, headset, EQ and game pages. C# 5 compatible WinForms.
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
        Catalog cat; Status st = new Status(); List<Check> checks = new List<Check>();
        readonly List<NavItem> nav = new List<NavItem>(); readonly List<Page> pages = new List<Page>();
        Panel side, host; Label sideState; int current; bool busy, reallyExit, startHidden;
        NotifyIcon tray; System.Windows.Forms.Timer heartbeat; bool lastGame; string lastSummary = "";

        // dashboard
        TextBlock heroTitle, heroSub; Btn heroMain, heroRecheck; readonly Dictionary<string, HealthRow> rows = new Dictionary<string, HealthRow>();
        Card healthCard; TextBox log; Toggle tAuto, tTray, tStart;
        // tune
        Grid styleGrid, gunGrid, headGrid; TextBlock tuneState, boostText; int boostDb;
        // headset
        Grid headsetGrid; TextBlock headsetState; Btn headsetUse;
        // eq, about
        TextBlock eqState, aboutState;

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            Text = "ClearSteps"; Icon = Theme.MakeIcon();
            ClientSize = new Size(1180, 760); MinimumSize = new Size(940, 620); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Ink; Font = Theme.Body; DoubleBuffered = true;
            Guardian.LoadSettings();
            cat = Setup.LoadCatalog(false);

            side = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Theme.Side };
            side.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawIcon(Icon, new Rectangle(24, 24, 36, 36));
                TextRenderer.DrawText(e.Graphics, "ClearSteps", new Font("Segoe UI Semibold", 16f), new Point(68, 22), Theme.Ink);
                TextRenderer.DrawText(e.Graphics, "MW4 footstep audio", Theme.Small, new Point(70, 50), Theme.Dim);
            };
            string[] glyphs = { G(0xE83D), G(0xE8D6), G(0xE7F6), G(0xE9E9), G(0xE7FC), G(0xE90F), G(0xE946) };
            string[] names = { "Guardian", "Tune", "Headset", "Headphone EQ", "Game settings", "Fix a problem", "Updates and credits" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i; var n = new NavItem(glyphs[i], names[i]);
                n.SetBounds(0, 96 + i * 50, 250, 46); n.Click += delegate { ShowPage(idx); };
                nav.Add(n); side.Controls.Add(n);
            }
            sideState = new Label { AutoSize = false, ForeColor = Theme.Dim, Font = Theme.Small, BackColor = Theme.Side, TextAlign = ContentAlignment.BottomLeft };
            side.Controls.Add(sideState);
            side.Resize += delegate { sideState.SetBounds(24, side.Height - 124, 210, 100); };

            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Controls.Add(host); Controls.Add(side);
            pages.Add(BuildGuardian()); pages.Add(BuildTune()); pages.Add(BuildHeadset()); pages.Add(BuildEq()); pages.Add(BuildGame()); pages.Add(BuildDoctor()); pages.Add(BuildAbout());
            foreach (Page p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }

            tray = new NotifyIcon { Icon = Icon, Text = "ClearSteps", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open ClearSteps", null, delegate { Reveal(); });
            menu.Items.Add("Check now", null, delegate { Beat(true); });
            menu.Items.Add("Exit", null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Reveal(); };

            heartbeat = new System.Windows.Forms.Timer { Interval = 20000 };
            heartbeat.Tick += delegate { Beat(false); };

            Shown += delegate
            {
                Theme.DarkTitleBar(Handle);
                RefreshState(); ShowPage(0); heartbeat.Start();
                Guardian.Log("Started " + Setup.AppVersion + " | " + Guardian.Summary(checks));
                Background(delegate { CheckOnline(true); }, false);
                if (Guardian.Headset == "" && st.Mixer) Background(delegate { Guardian.Headset = Voicemeeter.CurrentOutput(); Guardian.SaveSettings(); }, false);
                if (startHidden) BeginInvoke((Action)delegate { Hide(); });
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!reallyExit && Guardian.KeepInTray && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true; Hide();
                    tray.ShowBalloonTip(2500, "ClearSteps is still guarding your audio", "It lives here by the clock. Right-click it to exit.", ToolTipIcon.None);
                }
                else tray.Visible = false;
            };
        }

        public void Reveal() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); RefreshState(); }

        void ShowPage(int i)
        {
            current = i;
            for (int k = 0; k < pages.Count; k++) { pages[k].Visible = k == i; nav[k].Selected = k == i; nav[k].Invalidate(); }
            pages[i].Relayout();
        }

        void Ui(Action a) { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); }
        void Say(string s) { Guardian.Log(s); Ui(delegate { log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\r\n"); }); }

        void Background(Action work, bool lockUi)
        {
            if (lockUi) { if (busy) return; busy = true; host.Enabled = false; heroTitle.Set("Working...", Theme.Warn); }
            var t = new Thread(delegate()
            {
                string err = null;
                try { work(); } catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    if (lockUi) { busy = false; host.Enabled = true; }
                    RefreshState();
                    if (err != null && lockUi) { Say("PROBLEM: " + err); MessageBox.Show(this, err, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                });
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        /// The guardian heartbeat: re-audit, repair what is safe, note game start and stop.
        void Beat(bool manual)
        {
            if (busy) return;
            bool game = Guardian.GameRunning;
            if (game != lastGame) { lastGame = game; Say(game ? "Call of Duty started." : "Call of Duty closed."); }
            if (++beatNo % 3 == 1) { Background(delegate { Guardian.CheckRoute(); }, false); return; }
            Status s; List<Check> c = Guardian.Audit(cat, out s);
            if (Guardian.HeadsetPresent != headsetWasPresent)
            {
                headsetWasPresent = Guardian.HeadsetPresent;
                if (!headsetWasPresent) Say("Your headset (" + Guardian.Headset + ") went away. Waiting for it to come back.");
                else
                {
                    Say("Your headset is back. Reconnecting the tune to it...");
                    Background(delegate { Thread.Sleep(2500); Voicemeeter.RestartEngine(); Say("Reconnected."); }, false);
                    return;
                }
            }
            bool needs = c.Exists(delegate(Check x) { return x.AutoFix && x.State != Health.Ok; });
            if (needs && Guardian.AutoFixOn)
            {
                Background(delegate { foreach (string d in Guardian.Heal(cat, c, s, Say)) Say(d); }, false);
                return;
            }
            string sum = Guardian.Summary(c);
            if (sum != lastSummary || manual) { lastSummary = sum; Guardian.Log("Audit: " + sum); }
            if (Visible || manual) RefreshState();
            else { st = s; checks = c; UpdateTray(); }
        }

        Page NewPage(string title, string sub)
        {
            var p = new Page();
            p.Root.Add(new TextBlock(title, Theme.H1, Theme.Ink));
            if (sub != null) p.Root.Add(new TextBlock(sub, Theme.Body, Theme.Dim));
            return p;
        }
        static Card NewCard(Page p, string title)
        {
            var c = p.Root.Add(new Card());
            if (title != null) c.Add(new TextBlock(title, Theme.Title, Theme.Ink));
            return c;
        }

        // ---- page: guardian ----
        Page BuildGuardian()
        {
            var p = new Page();
            var hero = p.Root.Add(new Card()); hero.Pad = new Padding(28, 26, 28, 26); hero.Gap = 8;
            heroTitle = hero.Add(new TextBlock("Checking...", Theme.Hero, Theme.Ink));
            heroSub = hero.Add(new TextBlock("", Theme.Body, Theme.Dim));
            var hb = hero.Add(new Row());
            heroMain = hb.Add(new Btn("Set up this PC", true)); heroRecheck = hb.Add(new Btn("Check again", false));
            Btn heroDoctor = hb.Add(new Btn("Something sounds wrong", false));
            heroDoctor.Click += delegate { ShowPage(5); };
            heroMain.Click += delegate { MainAction(); };
            heroRecheck.Click += delegate { Beat(true); };

            healthCard = NewCard(p, "Health check"); healthCard.Gap = 4;
            string[] ids = { "tools", "data", "device", "eapo", "old", "mixer", "route", "headset", "tune", "default" };
            foreach (string id in ids)
            {
                string key = id; var r = healthCard.Add(new HealthRow()); r.Visible = false; rows[id] = r;
                r.Fix.Click += delegate { FixOne(key); };
            }

            var opt = NewCard(p, "Guardian"); opt.Gap = 16;
            tAuto = opt.Add(new Toggle("Fix problems automatically", "When Windows or an update knocks a setting out (format reset, device re-enabled, Voicemeeter closed), put it back without asking."));
            tTray = opt.Add(new Toggle("Keep running by the clock when I close the window", "The guardian keeps watching from the tray. Right-click its icon to exit."));
            tStart = opt.Add(new Toggle("Start with Windows", "Starts quietly in the tray every time you sign in, so the tune is always ready before you launch the game."));
            tAuto.On = Guardian.AutoFixOn; tTray.On = Guardian.KeepInTray; tStart.On = Guardian.StartWithWindows;
            tAuto.Changed += delegate { Guardian.AutoFixOn = tAuto.On; Guardian.SaveSettings(); };
            tTray.Changed += delegate { Guardian.KeepInTray = tTray.On; Guardian.SaveSettings(); };
            tStart.Changed += delegate { if (!Guardian.SetAutostart(tStart.On)) { tStart.On = !tStart.On; tStart.Invalidate(); MessageBox.Show(this, "Windows would not let me change the startup entry.", "ClearSteps"); } };

            var act = NewCard(p, "Activity");
            log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 190, BackColor = Theme.Bg, ForeColor = Theme.Dim, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f) };
            act.Add(log);
            try { if (File.Exists(Guardian.LogFile)) { string[] all = File.ReadAllLines(Guardian.LogFile); for (int i = Math.Max(0, all.Length - 12); i < all.Length; i++) log.AppendText(all[i] + "\r\n"); } } catch { }
            return p;
        }

        void MainAction()
        {
            bool needSetup = checks.Exists(delegate(Check c) { return c.NeedsSetup; });
            bool needHeadset = checks.Exists(delegate(Check c) { return c.NeedsHeadset; });
            bool fixable = checks.Exists(delegate(Check c) { return c.AutoFix && c.State != Health.Ok; });
            if (needSetup || (st.Plus != null && !st.ReadyForHeadset && !fixable)) RunSetup();
            else if (fixable) Background(delegate { Status s; List<Check> c = Guardian.Audit(cat, out s); foreach (string d in Guardian.Heal(cat, c, s, Say)) Say(d); }, true);
            else if (needHeadset) ShowPage(2);
            else Beat(true);
        }

        void RunSetup()
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
                Say("This PC is set up. Next: pick your headset.");
            }, true);
        }

        void FixOne(string id)
        {
            Check c = checks.Find(delegate(Check x) { return x.Id == id; }); if (c == null) return;
            if (c.NeedsSetup) { RunSetup(); return; }
            if (c.NeedsHeadset) { ShowPage(2); return; }
            Background(delegate { Status s; List<Check> all = Guardian.Audit(cat, out s); foreach (string d in Guardian.Heal(cat, all.FindAll(delegate(Check x) { return x.Id == id; }), s, Say)) Say(d); }, true);
        }

        // ---- page: tune ----
        Page BuildTune()
        {
            Page p = NewPage("Tune", cat.Title + ". " + cat.Note + " Click a card and it is live instantly, even mid-game.");
            tuneState = p.Root.Add(new TextBlock("", Theme.Bold, Theme.Accent));
            Card a = NewCard(p, "Tuning style");
            styleGrid = a.Add(new Grid { MinCell = 260, MaxCols = 3 });
            Card b = NewCard(p, "Your own gun volume");
            gunGrid = b.Add(new Grid { MinCell = 170, MaxCols = 5 });
            Card c = NewCard(p, "Head model (how surround is folded into your headset)");
            headGrid = c.Add(new Grid { MinCell = 260, MaxCols = 2 });
            FillTiles(styleGrid, cat.Styles); FillTiles(gunGrid, cat.Guns); FillTiles(headGrid, cat.Heads);
            Card d = NewCard(p, "Volume boost");
            d.Add(new TextBlock("Leave at 0 unless the game is too quiet with your headset at full volume. Do not turn Master or Effects volume down in game; use Voicemeeter for that.", Theme.Small, Theme.Dim));
            var r = d.Add(new Row());
            Btn minus = r.Add(new Btn("-", false)); boostText = r.Add(new TextBlock("0 dB", Theme.Title, Theme.Ink)); boostText.Width = 90; Btn plus = r.Add(new Btn("+", false));
            Btn reset = r.Add(new Btn("Back to recommended tune", false));
            minus.Click += delegate { if (boostDb > 0) { boostDb--; ApplyTune(); } };
            plus.Click += delegate { if (boostDb < 20) { boostDb++; ApplyTune(); } };
            reset.Click += delegate { SelectTile(styleGrid, cat.DefaultStyle); SelectTile(gunGrid, cat.DefaultGun); SelectTile(headGrid, cat.DefaultHead); ApplyTune(); };
            return p;
        }

        void FillTiles(Grid g, List<Option> opts)
        {
            foreach (Option o in opts)
            {
                var t = g.Add(new Tile { Title = o.Name, Tag2 = o.Tag.ToUpperInvariant(), Body = o.Text, Data = o.Id });
                Grid owner = g; Tile me = t;
                t.Click += delegate { foreach (Control x in owner.Items) { ((Tile)x).Selected = x == me; x.Invalidate(); } ApplyTune(); };
            }
        }
        static string G(int code) { return ((char)code).ToString(); }
        static void SelectTile(Grid g, string id) { foreach (Control x in g.Items) { var t = (Tile)x; bool sel = (string)t.Data == id; if (t.Selected != sel) { t.Selected = sel; t.Invalidate(); } } }
        static string Selected(Grid g) { foreach (Control x in g.Items) if (((Tile)x).Selected) return (string)((Tile)x).Data; return null; }

        void ApplyTune()
        {
            string s = Selected(styleGrid), g = Selected(gunGrid), h = Selected(headGrid);
            boostText.Set((boostDb > 0 ? "+" : "") + boostDb + " dB", Theme.Ink);
            if (s == null || g == null || h == null) return;
            if (!st.Stack || st.Plus == null) { tuneState.Set("Set up this PC first (Guardian page); then your choice here is saved.", Theme.Warn); return; }
            try
            {
                var t = new TuneChoice { Style = s, Gun = g, Head = h, EqInclude = st.EqSet ? st.Current.EqInclude : null, BoostDb = boostDb };
                ConfigWriter.Apply(cat, t, st.Plus.Guid);
                st.Current = t; st.TuneSet = true;
                tuneState.Set("Active now: " + Guardian.DescribeTune(cat, t) + (boostDb > 0 ? ", +" + boostDb + " dB" : "") + ".", Theme.Accent);
                Guardian.Log("Tune: " + Guardian.DescribeTune(cat, t) + " boost " + boostDb);
            }
            catch (Exception ex) { tuneState.Set(ex.Message, Theme.Warn); }
        }

        // ---- page: headset ----
        Page BuildHeadset()
        {
            Page p = NewPage("Headset", "The tuned game sound has to be sent to your headset. Voicemeeter does that, and this sets it up for you. Everything else (Discord, browser, music) then plays untuned through \"Normal Audio\" to the same headset.");
            Card c = NewCard(p, "Where do you listen?");
            var r = c.Add(new Row());
            Btn find = r.Add(new Btn("Find my headsets", true));
            headsetUse = r.Add(new Btn("Use the selected one", false)); headsetUse.Enabled = false;
            headsetGrid = c.Add(new Grid { MinCell = 320, MaxCols = 2 });
            headsetState = c.Add(new TextBlock("Press Find my headsets. Voicemeeter opens for a few seconds while it looks.", Theme.Body, Theme.Dim));
            find.Click += delegate
            {
                Background(delegate
                {
                    List<VmDevice> devs = Voicemeeter.OutputDevices();
                    Ui(delegate
                    {
                        headsetGrid.Clear();
                        foreach (VmDevice d in devs)
                        {
                            if (d.Type != Voicemeeter.Wdm || d.Name.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                            var t = headsetGrid.Add(new Tile { Title = d.Name, Data = d }); Tile me = t;
                            t.Click += delegate { foreach (Control x in headsetGrid.Items) { ((Tile)x).Selected = x == me; x.Invalidate(); } headsetUse.Enabled = true; headsetUse.Primary = true; headsetUse.Invalidate(); };
                        }
                        headsetState.Set(headsetGrid.Items.Count + " found. Click the one you wear when you play, then press Use the selected one.", Theme.Dim);
                        pages[2].Relayout();
                    });
                }, true);
            };
            headsetUse.Click += delegate
            {
                VmDevice d = null; foreach (Control x in headsetGrid.Items) if (((Tile)x).Selected) d = (VmDevice)((Tile)x).Data;
                if (d == null) return;
                Background(delegate
                {
                    Voicemeeter.Configure(d.Name, d.Type, Say);
                    Status s = Setup.Check(cat);
                    if (s.Normal != null) Audio.SetDefault(s.Normal.Id, true);
                    Guardian.Headset = d.Name; Guardian.SaveSettings(); headsetWasPresent = true;
                    Say("Headset set: " + d.Name);
                    Ui(delegate { headsetState.Set("Done. Tuned game sound and normal sound both go to " + d.Name + ". If you ever hear crackling, open Voicemeeter and choose the same headset under A1 as KS instead of WDM.", Theme.Accent); });
                }, true);
            };
            return p;
        }

        // ---- page: headphone EQ ----
        Page BuildEq()
        {
            Page p = NewPage("Headphone EQ", "This matches the tune to your exact headset. It takes two minutes on a free website and makes a real difference. Skip it and the tune still works with a flat EQ.");
            eqState = p.Root.Add(new TextBlock("", Theme.Bold, Theme.Dim));
            Card c = NewCard(p, "How");
            var g = c.Add(new Grid { MinCell = 190, MaxCols = 4 });
            string[] t = { "1  Find your headset", "2  Upload the target", "3  AutoEQ, then Export", "4  Import it here" };
            string[] b = { "Open the EQ site and click your headset in the list. Search by name.", "Click the Equalizer tab, then Upload Target, and pick " + cat.TargetFile + ". ClearSteps copies its location for you.", "Click AutoEQ, then Export. The site saves a small text file to Downloads.", "Press Import my EQ file and pick that file. It is live straight away." };
            for (int i = 0; i < 4; i++) g.Add(new Tile { Title = t[i], Body = b[i], Clickable = false, Cursor = Cursors.Default });
            var r = c.Add(new Row());
            Btn open = r.Add(new Btn("Open the EQ site", false)), target = r.Add(new Btn("Show me the target file", false)), imp = r.Add(new Btn("Import my EQ file", true)), flat = r.Add(new Btn("Use flat EQ", false));
            open.Click += delegate { Process.Start(cat.SquigUrl); };
            target.Click += delegate
            {
                string f = Path.Combine(Path.Combine(Paths.Library, cat.Folder), cat.TargetFile);
                if (!File.Exists(f)) { MessageBox.Show(this, "Set up this PC first so the tune library is installed.", "ClearSteps"); return; }
                try { Clipboard.SetText(f); } catch { }
                Process.Start("explorer.exe", "/select,\"" + f + "\"");
                MessageBox.Show(this, "The target file is highlighted in the folder that just opened, and its location is copied.\nIn the site's Upload Target window, paste (Ctrl+V) and press Open.", "ClearSteps");
            };
            imp.Click += delegate
            {
                using (var dlg = new OpenFileDialog())
                {
                    dlg.Title = "Pick the EQ file the site exported"; dlg.Filter = "EQ text files (*.txt)|*.txt";
                    dlg.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        string name = "MW4_V1_" + Path.GetFileNameWithoutExtension(dlg.FileName).Replace(" Filters", "").Replace(" ", "_");
                        SaveEq(ConfigWriter.ImportEq(dlg.FileName, name));
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            flat.Click += delegate { SaveEq(null); };
            return p;
        }

        void SaveEq(string include)
        {
            if (!st.Stack || st.Plus == null) { MessageBox.Show(this, "Set up this PC first.", "ClearSteps"); return; }
            TuneChoice t = st.Current; t.EqInclude = include;
            if (t.Style == null) { t.Style = cat.DefaultStyle; t.Gun = cat.DefaultGun; t.Head = cat.DefaultHead; }
            try { ConfigWriter.Apply(cat, t, st.Plus.Guid); Guardian.Log("EQ: " + (include ?? "flat")); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            RefreshState();
        }

        // ---- page: game settings ----
        Page BuildGame()
        {
            Page p = NewPage("Game settings", "Set these once in MW4 under Settings, Audio. The ones marked REQUIRED break the tune if they are wrong. They apply to Warzone, DMZ and multiplayer.");
            var g = p.Root.Add(new Grid { MinCell = 340, MaxCols = 3 });
            foreach (GameSetting s in cat.GameSettings)
                g.Add(new Tile { Title = s.Name + ":  " + s.Value, Tag2 = s.Level.ToUpperInvariant(), TagColor = s.Level == "required" ? Theme.Warn : Theme.Dim, Body = s.Why, Clickable = false, Cursor = Cursors.Default });
            return p;
        }

        // ---- page: fix a problem (the built-in IT guy) ----
        bool headsetWasPresent = true; TextBox doctorBox; int beatNo;

        Page BuildDoctor()
        {
            Page p = NewPage("Fix a problem", "Tell me what is wrong and I will work through it like an IT person would: check each link in the chain, fix what I can, and tell you plainly what is left. Nothing here costs anything or needs the internet.");
            var g = p.Root.Add(new Grid { MinCell = 320, MaxCols = 3 });
            foreach (Symptom s in Doctor.Symptoms)
            {
                string id = s.Id;
                var t = g.Add(new Tile { Title = s.Title, Body = s.Body });
                t.Click += delegate
                {
                    doctorBox.Clear();
                    Action<string> tell = delegate(string line) { Guardian.Log("Doctor: " + line); Ui(delegate { doctorBox.AppendText(line + "\r\n\r\n"); }); };
                    Background(delegate { Doctor.Run(id, cat, tell); }, true);
                };
            }
            Card c = NewCard(p, "What I found");
            doctorBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 330, BackColor = Theme.Surface, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None, Font = Theme.Body, Text = "Pick what is wrong above. If you are not sure, start with the full check-up." };
            c.Add(doctorBox);
            var r = c.Add(new Row());
            Btn copy = r.Add(new Btn("Copy a report to send for help", false));
            copy.Click += delegate
            {
                try { Clipboard.SetText(Doctor.Report(cat) + "\r\nTroubleshooter said:\r\n" + doctorBox.Text); MessageBox.Show(this, "A full report is copied. Paste it into a message to whoever set this up for you.", "ClearSteps"); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "ClearSteps"); }
            };
            return p;
        }

        // ---- page: updates and credits ----
        Page BuildAbout()
        {
            Page p = NewPage("Updates and credits", null);
            Card a = NewCard(p, "Updates");
            aboutState = a.Add(new TextBlock("", Theme.Body, Theme.Dim));
            var r = a.Add(new Row());
            Btn u = r.Add(new Btn("Check for updates", true)), lib = r.Add(new Btn("Re-download tune library", false));
            u.Click += delegate { Background(delegate { CheckOnline(false); }, true); };
            lib.Click += delegate
            {
                if (MessageBox.Show(this, "This runs ArtIsWar's installer to fetch the newest tune library. It backs up the current one first. Your headset EQ is kept.", "ClearSteps", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                ShowPage(0);
                Background(delegate { Setup.RunOfficialInstaller(cat, "3", Say); Say("Library refreshed."); }, true);
            };
            Card c = NewCard(p, "Credit where it is due");
            c.Add(new TextBlock("The tune, the sound engine and the installer are the work of ArtIsWar, who gives them away free. ClearSteps is an unofficial helper made by a fan: it downloads everything from his own site and only does the clicking and the watching for you. It is free and must stay free.\n\nIf you want his polished, supported version with profiles, no Voicemeeter and clean stream audio, buy his app ArtTuneKit.", Theme.Body, Theme.Dim));
            var links = c.Add(new Row());
            string[] text = { "Watch the MW4 guide", "His tunes on GitHub", "ArtTuneKit (his paid app)", "His Discord", "ClearSteps page" };
            string[] urls = { cat.Video, cat.Repo, cat.PaidApp, cat.Discord, Setup.RepoPage };
            for (int i = 0; i < text.Length; i++) { string url = urls[i]; links.Add(new Btn(text[i], false)).Click += delegate { Process.Start(url); }; }
            return p;
        }

        void CheckOnline(bool quiet)
        {
            Catalog fresh = Setup.LoadCatalog(true);
            Setup.UpdateInfo up = null; string err = null;
            try { up = Setup.CheckForUpdate(); } catch (Exception ex) { err = ex.Message; }
            Ui(delegate
            {
                if (up != null && (Visible || !quiet))
                {
                    if (MessageBox.Show(this, "ClearSteps " + up.Version + " is available.\n\n" + up.Notes + "\n\nUpdate now? It takes a few seconds and reopens by itself.", "ClearSteps update", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        try { tray.Visible = false; Setup.ApplyUpdate(up); } catch (Exception ex) { tray.Visible = true; MessageBox.Show(this, "Update failed: " + ex.Message, "ClearSteps"); }
                    }
                }
                else if (!quiet) MessageBox.Show(this, err == null ? "You have the newest version." : "Could not check for updates right now (" + err + ").", "ClearSteps");
                if (fresh.Version > cat.Version && !quiet) MessageBox.Show(this, "New tune options were downloaded. Close and reopen ClearSteps to see them.", "ClearSteps");
            });
        }

        // ---- state to screen ----
        void UpdateTray()
        {
            int bad = checks.FindAll(delegate(Check c) { return c.State == Health.Warn || c.State == Health.Bad; }).Count;
            string t = bad == 0 ? "ClearSteps: protected" : "ClearSteps: " + bad + " thing" + (bad == 1 ? "" : "s") + " need attention";
            if (tray != null) tray.Text = t;
        }

        void RefreshState()
        {
            try { checks = Guardian.Audit(cat, out st); } catch { }
            lastSummary = Guardian.Summary(checks);
            foreach (KeyValuePair<string, HealthRow> kv in rows)
            {
                Check c = checks.Find(delegate(Check x) { return x.Id == kv.Key; });
                kv.Value.Visible = c != null; if (c == null) continue;
                kv.Value.Title = c.Title;
                kv.Value.Set(c.State, c.Detail, c.State != Health.Ok && (c.AutoFix || c.NeedsSetup || c.NeedsHeadset));
                kv.Value.Fix.SetText(c.NeedsSetup ? "Set up" : c.NeedsHeadset ? "Pick headset" : "Fix");
            }
            bool needSetup = checks.Exists(delegate(Check c) { return c.NeedsSetup; });
            bool needHeadset = checks.Exists(delegate(Check c) { return c.NeedsHeadset; });
            int bad = checks.FindAll(delegate(Check c) { return c.State == Health.Warn || c.State == Health.Bad; }).Count;
            bool game = Guardian.GameRunning;
            if (!busy)
            {
                if (needSetup) { heroTitle.Set("Not set up yet", Theme.Ink); heroSub.Set("One button installs ArtIsWar's free audio tools from his own site and does every Windows sound setting his guide has you do by hand. About 3 minutes. Your sound cuts out briefly.", Theme.Dim); heroMain.SetText("Set up this PC"); }
                else if (bad > 0) { heroTitle.Set(bad + (bad == 1 ? " thing needs" : " things need") + " attention", Theme.Warn); heroSub.Set(needHeadset && bad == 1 ? "Almost there: pick the headset you play on." : "Something changed since the tune was set up. One press puts it back.", Theme.Dim); heroMain.SetText(needHeadset && bad == 1 ? "Pick your headset" : "Fix everything"); }
                else { heroTitle.Set("Protected", Theme.Accent); heroSub.Set("Everything the tune needs is in place" + (st.TuneSet ? ": " + Guardian.DescribeTune(cat, st.Current) + (st.EqSet ? ", with your headset EQ." : ", flat EQ.") : ".") + (game ? "  Call of Duty is running." : ""), Theme.Dim); heroMain.SetText("Check again"); }
                heroRecheck.Visible = needSetup || bad > 0;
            }
            nav[0].Dot = needSetup ? Theme.Bad : bad > 0 ? Theme.Warn : Theme.Accent;
            nav[2].Dot = needHeadset ? Theme.Warn : Color.Empty;
            nav[3].Dot = st.Stack && !st.EqSet ? Theme.Dim : Color.Empty;
            foreach (NavItem n in nav) n.Invalidate();
            SelectTile(styleGrid, st.Current.Style ?? cat.DefaultStyle); SelectTile(gunGrid, st.Current.Gun ?? cat.DefaultGun); SelectTile(headGrid, st.Current.Head ?? cat.DefaultHead);
            boostDb = (int)Math.Max(0, Math.Min(20, Math.Round(st.Current.BoostDb)));
            boostText.Set((boostDb > 0 ? "+" : "") + boostDb + " dB", Theme.Ink);
            tuneState.Set(st.TuneSet ? "Active now: " + Guardian.DescribeTune(cat, st.Current) + (boostDb > 0 ? ", +" + boostDb + " dB" : "") + "." : "Nothing loaded yet.", st.TuneSet ? Theme.Accent : Theme.Dim);
            eqState.Set(st.EqSet ? "Using your headset EQ: " + Path.GetFileNameWithoutExtension(st.Current.EqInclude) : "Using the flat EQ (works, but not matched to your headset).", st.EqSet ? Theme.Accent : Theme.Dim);
            aboutState.Set("ClearSteps " + Setup.AppVersion + "   |   tune library " + (st.LibraryVersion == "" ? "not installed" : st.LibraryVersion) + "   |   tune list " + cat.Updated + "\nUpdates are checked every time the app starts.", Theme.Dim);
            sideState.Text = G(0x25CF) + (bad == 0 && !needSetup ? "  Guardian on" : "  Needs attention") + "\n" + (game ? "Call of Duty is running" : "Game not running") + "\nv" + Setup.AppVersion;
            sideState.ForeColor = bad == 0 && !needSetup ? Theme.Accent : Theme.Warn;
            UpdateTray();
            if (pages.Count > current) pages[current].Relayout();
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (!Environment.Is64BitOperatingSystem) { MessageBox.Show("ClearSteps needs 64-bit Windows 10 or 11.", "ClearSteps"); return; }
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 2 && args[1] == "--selftest") { SelfTest(args); return; }
            if (args.Length > 2 && args[1] == "--autostart") { Guardian.LoadSettings(); Environment.ExitCode = Guardian.SetAutostart(args[2] == "on") ? 0 : 1; return; }
            if (args.Length > 2 && args[1] == "--shots")
            {
                // Self-check used while building: draws every page to a PNG at the given size and exits.
                int w = args.Length > 4 ? int.Parse(args[3]) : 1180, h = args.Length > 4 ? int.Parse(args[4]) : 760;
                var f = new MainForm(false); f.StartPosition = FormStartPosition.Manual; f.Location = new Point(0, 0); f.ClientSize = new Size(w, h);
                f.Show(); f.heartbeat.Stop(); f.RefreshState(); Application.DoEvents();
                for (int i = 0; i < f.pages.Count; i++)
                {
                    f.ShowPage(i); Application.DoEvents();
                    using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize)); bmp.Save(Path.Combine(args[2], "page" + (i + 1) + ".png")); }
                }
                f.tray.Visible = false;
                return;
            }
            bool created;
            using (var mutex = new Mutex(true, "ClearSteps.SingleInstance", out created))
            using (var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "ClearSteps.Show"))
            {
                if (!created) { showSignal.Set(); return; }
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Guardian.Log("Error: " + e.Exception.Message); MessageBox.Show(e.Exception.Message, "ClearSteps"); };
                var form = new MainForm(Array.IndexOf(args, "--tray") >= 0);
                var waiter = new Thread(delegate() { while (true) { showSignal.WaitOne(); try { form.BeginInvoke((Action)delegate { form.Reveal(); }); } catch { } } });
                waiter.IsBackground = true; waiter.Start();
                Application.Run(form);
            }
        }

        static void SelfTest(string[] args)
        {
            // Self-check used while building: update check, update download, EQ import. Writes a log and exits.
            var sb = new System.Text.StringBuilder();
            try
            {
                Catalog c = Setup.LoadCatalog(true);
                sb.AppendLine("version " + Setup.AppVersion + ", catalog v" + c.Version);
                Status s; sb.AppendLine("audit: " + Guardian.Summary(Guardian.Audit(c, out s)));
                Setup.UpdateInfo u = Setup.CheckForUpdate();
                sb.AppendLine("update: " + (u == null ? "none" : u.Version + " " + u.Url));
                if (args.Length > 3)
                {
                    string inc = ConfigWriter.ImportEq(args[3], "MW4_V1_SelfTest");
                    TuneChoice t = ConfigWriter.ReadCurrent(c); t.EqInclude = inc;
                    ConfigWriter.Apply(c, t, s.Plus == null ? null : s.Plus.Guid);
                    sb.AppendLine("eq imported: " + ConfigWriter.ReadCurrent(c).EqInclude);
                    t.EqInclude = null; ConfigWriter.Apply(c, t, s.Plus == null ? null : s.Plus.Guid);
                    sb.AppendLine("eq back to: " + (ConfigWriter.ReadCurrent(c).EqInclude ?? "flat"));
                }
                File.WriteAllText(args[2], sb.ToString());
                if (u != null) Setup.ApplyUpdate(u);
            }
            catch (Exception ex) { sb.AppendLine("ERROR " + ex); File.WriteAllText(args[2], sb.ToString()); }
        }
    }
}
