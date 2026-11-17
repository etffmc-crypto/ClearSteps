// ClearSteps - the window: a guardian dashboard plus tune, headset, EQ, game and troubleshooting pages.
// C# 5 compatible WinForms. Plain ASCII source on purpose (icons are written as character codes).
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
        Panel side, host; Label sideState; BusyBar busyBar; Bitmap brandMark; int current; bool busy, reallyExit, startHidden;
        NotifyIcon tray; System.Windows.Forms.Timer heartbeat; bool lastGame; string lastSummary = ""; int beatNo; bool headsetWasPresent = true;

        // guardian page
        TextBlock heroTitle, heroSub; Btn heroMain, heroRecheck; ChainView chain; readonly Dictionary<string, HealthRow> rows = new Dictionary<string, HealthRow>();
        TextBox log; Toggle tAuto, tTray, tStart;
        // tune
        Grid styleGrid, gunGrid, headGrid; TextBlock tuneState, boostText; int boostDb;
        // headset
        Grid headsetGrid; TextBlock headsetState; Btn headsetUse;
        // eq, doctor, about
        TextBlock eqState, aboutState; TextBox doctorBox;

        static int S(int px) { return Theme.S(px); }

        public MainForm(bool hidden)
        {
            startHidden = hidden;
            Text = "ClearSteps"; Icon = Theme.MakeIcon(); brandMark = Theme.Mark(S(34));
            BackColor = Theme.Bg; ForeColor = Theme.Ink; Font = Theme.Body; DoubleBuffered = true; KeyPreview = true;
            Guardian.LoadSettings();
            PlaceWindow();
            cat = Setup.LoadCatalog(false);

            side = new Panel { Dock = DockStyle.Left, Width = S(244), BackColor = Theme.Side };
            side.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawImageUnscaled(brandMark, S(22), S(22));
                TextRenderer.DrawText(e.Graphics, "ClearSteps", Theme.Brand, new Point(S(64), S(19)), Theme.Ink, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, "MW4 footstep audio", Theme.Small, new Point(S(65), S(42)), Theme.Dim, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            };
            int[] glyphs = { 0xE83D, 0xE8D6, 0xE7F6, 0xE9E9, 0xE7FC, 0xE90F, 0xE946 };
            string[] names = { "Guardian", "Tune", "Headset", "Headphone EQ", "Game settings", "Fix a problem", "Updates and credits" };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i; var n = new NavItem(Theme.G(glyphs[i]), names[i]);
                n.SetBounds(0, S(88) + i * S(48), side.Width, S(44)); n.TabIndex = i; n.Click += delegate { ShowPage(idx); };
                nav.Add(n); side.Controls.Add(n);
            }
            sideState = new Label { AutoSize = false, ForeColor = Theme.Dim, Font = Theme.Small, BackColor = Theme.Side, TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
            side.Controls.Add(sideState);
            side.Resize += delegate { sideState.SetBounds(S(24), side.Height - S(100), side.Width - S(36), S(82)); };

            host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
            busyBar = new BusyBar { Dock = DockStyle.Top };
            Controls.Add(host); Controls.Add(busyBar); Controls.Add(side);
            pages.Add(BuildGuardian()); pages.Add(BuildTune()); pages.Add(BuildHeadset()); pages.Add(BuildEq()); pages.Add(BuildGame()); pages.Add(BuildDoctor()); pages.Add(BuildAbout());
            foreach (Page p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; host.Controls.Add(p); }

            tray = new NotifyIcon { Icon = Icon, Text = "ClearSteps", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open ClearSteps", null, delegate { Reveal(); });
            menu.Items.Add("Check now", null, delegate { Beat(true); });
            menu.Items.Add("Exit", null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { Reveal(); }; tray.BalloonTipClicked += delegate { Reveal(); };

            heartbeat = new System.Windows.Forms.Timer { Interval = 20000 };
            heartbeat.Tick += delegate { Beat(false); };

            Shown += delegate
            {
                Theme.DarkTitleBar(Handle);
                RefreshState(); ShowPage(0); heartbeat.Start();
                Guardian.Log("Started " + Setup.AppVersion + " | " + Guardian.Summary(checks));
                Background(delegate { CheckOnline(true); }, false);
                if (Guardian.Headset == "" && st.Mixer) Background(delegate { Guardian.CheckRoute(); }, false);
                if (startHidden) BeginInvoke((Action)delegate { Hide(); });
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                RememberWindow();
                if (!reallyExit && Guardian.KeepInTray && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true; Hide();
                    tray.ShowBalloonTip(2500, "ClearSteps is still guarding your audio", "It lives here by the clock. Right-click it to exit.", ToolTipIcon.None);
                }
                else tray.Visible = false;
            };
        }

        // ---- window placement: remembered between runs, and never larger than the screen ----
        void PlaceWindow()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            MinimumSize = new Size(Math.Min(S(940), work.Width), Math.Min(S(640), work.Height));
            Size want = new Size(Math.Min(S(1200), work.Width - S(40)), Math.Min(S(800), work.Height - S(40)));
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(work.X + (work.Width - want.Width) / 2, work.Y + (work.Height - want.Height) / 2, want.Width, want.Height);
            try
            {
                string[] p = Guardian.Window.Split(',');
                if (p.Length == 5)
                {
                    var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                    bool onScreen = false; foreach (Screen sc in Screen.AllScreens) if (sc.WorkingArea.IntersectsWith(r)) onScreen = true;
                    if (onScreen && r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height) Bounds = r;
                    if (p[4] == "1") WindowState = FormWindowState.Maximized;
                }
            }
            catch { }
        }
        void RememberWindow()
        {
            if (WindowState == FormWindowState.Minimized || !Visible) return;
            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Guardian.Window = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? 1 : 0);
            Guardian.SaveSettings();
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
            if (lockUi) { if (busy) return; busy = true; host.Enabled = false; busyBar.Active = true; heroTitle.Set("Working on it", Theme.Warn); }
            var t = new Thread(delegate()
            {
                string err = null;
                try { work(); } catch (Exception ex) { err = ex.Message; }
                Ui(delegate
                {
                    if (lockUi) { busy = false; host.Enabled = true; busyBar.Active = false; }
                    RefreshState();
                    if (err != null && lockUi) { Say("Problem: " + err); MessageBox.Show(this, err, "ClearSteps", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
            if (++beatNo % 3 == 1 && !manual) { Background(delegate { Guardian.CheckRoute(); }, false); return; }
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
                Background(delegate
                {
                    List<string> done = Guardian.Heal(cat, c, s, Say);
                    foreach (string d in done) Say(d);
                    // When the window is hidden, tell the user what was repaired with a notification.
                    if (done.Count > 0) Ui(delegate { if (!Visible) tray.ShowBalloonTip(5000, "ClearSteps fixed your audio", string.Join("\n", done.ToArray()), ToolTipIcon.None); });
                }, false);
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
            if (sub != null) p.Root.Add(new TextBlock(sub, Theme.Body, Theme.Dim) { MaxTextWidth = S(860) });
            return p;
        }
        static Card NewCard(Page p, string title)
        {
            var c = p.Root.Add(new Card());
            if (title != null) c.Add(new TextBlock(title, Theme.Title, Theme.Ink));
            return c;
        }
        TextBox NewLogBox(int height, Color back, Font font)
        {
            var t = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = S(height), BackColor = back, ForeColor = Theme.Dim, BorderStyle = BorderStyle.None, Font = font, TabStop = false };
            Theme.DarkScroll(t); return t;
        }

        // ---- page: guardian ----
        Page BuildGuardian()
        {
            var p = new Page();
            var hero = p.Root.Add(new Card()); hero.Pad = new Padding(S(28), S(24), S(28), S(26)); hero.Gap = S(8);
            heroTitle = hero.Add(new TextBlock("Checking", Theme.Hero, Theme.Ink));
            heroSub = hero.Add(new TextBlock("", Theme.Body, Theme.Dim) { MaxTextWidth = S(820) });
            // The signal path is the picture of what this app protects: it leads the page.
            var pathWrap = hero.Add(new Stack()); pathWrap.Pad = new Padding(0, S(14), 0, S(10));
            chain = pathWrap.Add(new ChainView());
            string[] nodeNames = { "Call of Duty", "Art Tune +", "The tune", "Voicemeeter", "Your headset" };
            int[] nodeGlyphs = { 0xE7FC, 0xE8D6, 0xE9E9, 0xE7F5, 0xE7F6 };
            for (int i = 0; i < nodeNames.Length; i++) chain.Nodes.Add(new ChainNode { Name = nodeNames[i], Glyph = Theme.G(nodeGlyphs[i]) });
            var hb = hero.Add(new Row());
            heroMain = hb.Add(new Btn("Set up this PC", true)); heroRecheck = hb.Add(new Btn("Check again", false));
            Btn heroDoctor = hb.Add(new Btn("Something sounds wrong", false));
            heroMain.Click += delegate { MainAction(); };
            heroRecheck.Click += delegate { Beat(true); };
            heroDoctor.Click += delegate { ShowPage(5); };

            Card health = NewCard(p, "Health check");
            var hg = health.Add(new Grid { MinCell = S(380), MaxCols = 2, Gap = S(6) });
            string[] ids = { "tools", "data", "device", "eapo", "old", "mixer", "route", "headset", "tune", "default" };
            foreach (string id in ids)
            {
                string key = id; var r = hg.Add(new HealthRow()); r.Visible = false; rows[id] = r;
                r.Fix.Click += delegate { FixOne(key); };
            }

            Card opt = NewCard(p, "How the guardian behaves"); opt.Gap = S(16);
            tAuto = opt.Add(new Toggle("Fix problems automatically", "When Windows or an update knocks a setting out (format reset, device re-enabled, Voicemeeter closed), put it back without asking."));
            tTray = opt.Add(new Toggle("Keep running by the clock when I close the window", "The guardian keeps watching from the tray. Right-click its icon to exit."));
            tStart = opt.Add(new Toggle("Start with Windows", "Starts quietly in the tray every time you sign in, so the tune is ready before you launch the game."));
            tAuto.On = Guardian.AutoFixOn; tTray.On = Guardian.KeepInTray; tStart.On = Guardian.StartWithWindows;
            tAuto.Changed += delegate { Guardian.AutoFixOn = tAuto.On; Guardian.SaveSettings(); };
            tTray.Changed += delegate { Guardian.KeepInTray = tTray.On; Guardian.SaveSettings(); };
            tStart.Changed += delegate { if (!Guardian.SetAutostart(tStart.On)) { tStart.On = !tStart.On; tStart.Invalidate(); MessageBox.Show(this, "Windows would not let ClearSteps change the startup entry.", "ClearSteps"); } };

            Card act = NewCard(p, "Recent activity");
            log = NewLogBox(170, Theme.Surface, Theme.Mono); act.Add(log);
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
            Page p = NewPage("Tune", cat.Title + ". " + cat.Note + " Click a card and it is live straight away, even mid-game.");
            tuneState = p.Root.Add(new TextBlock("", Theme.Bold, Theme.Accent));
            Card a = NewCard(p, "Tuning style");
            styleGrid = a.Add(new Grid { MinCell = S(260), MaxCols = 3 });
            Card b = NewCard(p, "Your own gun volume");
            gunGrid = b.Add(new Grid { MinCell = S(170), MaxCols = 5 });
            Card c = NewCard(p, "Head model");
            c.Add(new TextBlock("How the game's surround sound is folded into your two ears. Heads differ, so try both.", Theme.Small, Theme.Dim));
            headGrid = c.Add(new Grid { MinCell = S(260), MaxCols = 2 });
            FillTiles(styleGrid, cat.Styles); FillTiles(gunGrid, cat.Guns); FillTiles(headGrid, cat.Heads);
            Card d = NewCard(p, "Volume boost");
            d.Add(new TextBlock("Leave at 0 unless the game is too quiet with your headset turned right up. Never turn Master or Effects volume down in game; use Voicemeeter for that.", Theme.Small, Theme.Dim) { MaxTextWidth = S(820) });
            var r = d.Add(new Row());
            Btn minus = r.Add(new Btn("-", false)); minus.AccessibleName = "Less boost";
            boostText = r.Add(new TextBlock("0 dB", Theme.Title, Theme.Ink)); boostText.Width = S(84);
            Btn plus = r.Add(new Btn("+", false)); plus.AccessibleName = "More boost";
            Btn reset = r.Add(new Btn("Back to the recommended tune", false));
            minus.Click += delegate { if (boostDb > 0) { boostDb--; ApplyTune(); } };
            plus.Click += delegate { if (boostDb < 20) { boostDb++; ApplyTune(); } };
            reset.Click += delegate { SelectTile(styleGrid, cat.DefaultStyle); SelectTile(gunGrid, cat.DefaultGun); SelectTile(headGrid, cat.DefaultHead); ApplyTune(); };
            return p;
        }

        void FillTiles(Grid g, List<Option> opts)
        {
            foreach (Option o in opts)
            {
                var t = g.Add(new Tile { Title = o.Name, Tag2 = o.Tag, Body = o.Text, Data = o.Id });
                Grid owner = g; Tile me = t;
                t.Click += delegate { foreach (Control x in owner.Items) { ((Tile)x).Selected = x == me; x.Invalidate(); } ApplyTune(); };
            }
        }
        static void SelectTile(Grid g, string id) { foreach (Control x in g.Items) { var t = (Tile)x; bool sel = (string)t.Data == id; if (t.Selected != sel) { t.Selected = sel; t.Invalidate(); } } }
        static string Selected(Grid g) { foreach (Control x in g.Items) if (((Tile)x).Selected) return (string)((Tile)x).Data; return null; }
        static string Boost(int db) { return (db > 0 ? "+" : "") + db + " dB"; }

        void ApplyTune()
        {
            string s = Selected(styleGrid), g = Selected(gunGrid), h = Selected(headGrid);
            boostText.Set(Boost(boostDb), Theme.Ink);
            if (s == null || g == null || h == null) return;
            if (!st.Stack || st.Plus == null) { tuneState.Set("Set up this PC first, on the Guardian page. Then your choice here is saved.", Theme.Warn); return; }
            try
            {
                var t = new TuneChoice { Style = s, Gun = g, Head = h, EqInclude = st.EqSet ? st.Current.EqInclude : null, BoostDb = boostDb };
                ConfigWriter.Apply(cat, t, st.Plus.Guid);
                st.Current = t; st.TuneSet = true;
                tuneState.Set("Playing now: " + Guardian.DescribeTune(cat, t) + (boostDb > 0 ? ", " + Boost(boostDb) : "") + ".", Theme.Accent);
                Guardian.Log("Tune: " + Guardian.DescribeTune(cat, t) + " boost " + boostDb);
            }
            catch (Exception ex) { tuneState.Set(ex.Message, Theme.Warn); }
        }

        // ---- page: headset ----
        Page BuildHeadset()
        {
            Page p = NewPage("Headset", "The tuned game sound has to be sent to your headset. Voicemeeter does that, and this page sets it up for you. Everything else (Discord, browser, music) then plays untuned through Normal Audio to the same headset.");
            Card c = NewCard(p, "Where do you listen?");
            var r = c.Add(new Row());
            Btn find = r.Add(new Btn("Find my headsets", true));
            headsetUse = r.Add(new Btn("Use the selected headset", false)); headsetUse.Enabled = false;
            headsetGrid = c.Add(new Grid { MinCell = S(320), MaxCols = 2 });
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
                            bool isCurrent = string.Equals(d.Name, Guardian.Headset, StringComparison.OrdinalIgnoreCase);
                            var t = headsetGrid.Add(new Tile { Title = d.Name, Tag2 = isCurrent ? "In use" : "", Data = d }); Tile me = t;
                            t.Click += delegate { foreach (Control x in headsetGrid.Items) { ((Tile)x).Selected = x == me; x.Invalidate(); } headsetUse.Enabled = true; headsetUse.Primary = true; headsetUse.Invalidate(); };
                        }
                        headsetState.Set(headsetGrid.Items.Count == 0 ? "No headsets found. Open Fix a problem and choose \"I changed headset, or it does not show up\"." : headsetGrid.Items.Count + " found. Click the one you wear when you play, then press Use the selected headset.", Theme.Dim);
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
                    Guardian.Headset = d.Name; Guardian.RouteOk = true; Guardian.SaveSettings(); headsetWasPresent = true;
                    Say("Headset set: " + d.Name);
                    Ui(delegate { headsetState.Set("Done. Tuned game sound and normal sound both go to " + d.Name + ". If you ever hear crackling, open Fix a problem.", Theme.Accent); });
                }, true);
            };
            return p;
        }

        // ---- page: headphone EQ ----
        Page BuildEq()
        {
            Page p = NewPage("Headphone EQ", "This matches the tune to your exact headset. It takes two minutes on a free website and makes a real difference. Skip it and the tune still works with a flat EQ.");
            eqState = p.Root.Add(new TextBlock("", Theme.Bold, Theme.Dim));
            Card c = NewCard(p, "Four steps");
            var g = c.Add(new Grid { MinCell = S(200), MaxCols = 4 });
            string[] t = { "1. Find your headset", "2. Upload the target", "3. AutoEQ, then Export", "4. Import it here" };
            string[] b = { "Open the EQ site and click your headset in the list. Search by name.", "Click the Equalizer tab, then Upload Target, and pick " + cat.TargetFile + ". ClearSteps copies its location for you.", "Click AutoEQ, then Export. The site saves a small text file to Downloads.", "Press Import my EQ file and pick that file. It is live straight away." };
            for (int i = 0; i < 4; i++) g.Add(new Tile { Title = t[i], Body = b[i], Clickable = false });
            var r = c.Add(new Row());
            Btn open = r.Add(new Btn("Open the EQ site", false)), target = r.Add(new Btn("Show me the target file", false)), imp = r.Add(new Btn("Import my EQ file", true)), flat = r.Add(new Btn("Use the flat EQ", false));
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
            Page p = NewPage("Game settings", "Set these once in MW4 under Settings, Audio. The required ones switch the tune off if they are wrong. They apply to Warzone, DMZ and multiplayer.");
            var g = p.Root.Add(new Grid { MinCell = S(340), MaxCols = 3 });
            foreach (GameSetting s in cat.GameSettings)
            {
                bool req = s.Level == "required";
                string level = s.Level.Length > 0 ? char.ToUpperInvariant(s.Level[0]) + s.Level.Substring(1) : "";
                g.Add(new Tile { Title = s.Name, Tag2 = s.Value, TagColor = req ? Theme.Accent : Theme.Dim, Body = level + ". " + s.Why, Clickable = false });
            }
            return p;
        }

        // ---- page: fix a problem ----
        Page BuildDoctor()
        {
            Page p = NewPage("Fix a problem", "Pick what is wrong. ClearSteps checks each link between the game and your ears, repairs what it can, and tells you plainly what is left. It costs nothing and does not need the internet.");
            var g = p.Root.Add(new Grid { MinCell = S(320), MaxCols = 3 });
            foreach (Symptom s in Doctor.Symptoms)
            {
                string id = s.Id;
                var t = g.Add(new Tile { Title = s.Title, Body = s.Body }); t.AccessibleRole = AccessibleRole.PushButton;
                t.Click += delegate
                {
                    doctorBox.Clear();
                    Action<string> tell = delegate(string line) { Guardian.Log("Doctor: " + line); Ui(delegate { doctorBox.AppendText(line + "\r\n\r\n"); }); };
                    Background(delegate { Doctor.Run(id, cat, tell); }, true);
                };
            }
            Card c = NewCard(p, "What was found");
            doctorBox = NewLogBox(320, Theme.Surface, Theme.Body); doctorBox.ForeColor = Theme.Ink;
            doctorBox.Text = "Nothing checked yet. If you are not sure what is wrong, start with the full check-up.";
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
            Btn u = r.Add(new Btn("Check for updates", true)), lib = r.Add(new Btn("Re-download the tune library", false));
            u.Click += delegate { Background(delegate { CheckOnline(false); }, true); };
            lib.Click += delegate
            {
                if (MessageBox.Show(this, "This runs ArtIsWar's installer to fetch the newest tune library. It backs up the current one first. Your headset EQ is kept.", "ClearSteps", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                ShowPage(0);
                Background(delegate { Setup.RunOfficialInstaller(cat, "3", Say); Say("Library refreshed."); }, true);
            };
            Card c = NewCard(p, "Credit where it is due");
            c.Add(new TextBlock("The tune, the sound engine and the installer are the work of ArtIsWar, who gives them away free. ClearSteps is an unofficial helper made by a fan: it downloads everything from his own site and only does the clicking and the watching for you. It is free and must stay free.\n\nIf you want his polished, supported version with profiles, no Voicemeeter and clean stream audio, buy his app ArtTuneKit.", Theme.Body, Theme.Dim) { MaxTextWidth = S(820) });
            var links = c.Add(new Row());
            string[] text = { "Watch the MW4 guide", "His tunes on GitHub", "ArtTuneKit, his paid app", "His Discord", "ClearSteps page" };
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
            if (tray != null) tray.Text = bad == 0 ? "ClearSteps: protected" : "ClearSteps: " + bad + " thing" + (bad == 1 ? "" : "s") + " need attention";
        }

        Check Find(string id) { return checks.Find(delegate(Check x) { return x.Id == id; }); }
        Health Worst(params string[] ids)
        {
            Health w = Health.Off;
            foreach (string id in ids)
            {
                Check c = Find(id); if (c == null || c.State == Health.Off) continue;
                if (c.State == Health.Bad) return Health.Bad;
                if (c.State == Health.Warn) w = Health.Warn; else if (w == Health.Off) w = Health.Ok;
            }
            return w;
        }
        static bool Broken(Check c) { return c != null && (c.State == Health.Warn || c.State == Health.Bad); }

        void UpdateChain(bool game)
        {
            List<ChainNode> n = chain.Nodes;
            n[0].State = game ? Health.Ok : Health.Off; n[0].Caption = game ? "Running" : "Not running";
            n[1].State = Worst("tools", "device", "old");
            n[1].Caption = Broken(Find("tools")) ? "Not installed" : Broken(Find("device")) ? "Wrong format" : Broken(Find("old")) ? "Old device is on" : n[1].State == Health.Ok ? "16 channels" : "After setup";
            n[2].State = Worst("data", "eapo", "tune");
            Option style = cat.Styles.Find(delegate(Option o) { return o.Id == st.Current.Style; });
            n[2].Caption = Broken(Find("data")) ? "Not downloaded" : Broken(Find("eapo")) ? "Being skipped" : Broken(Find("tune")) ? "Nothing loaded" : n[2].State == Health.Ok && style != null ? style.Name : "After setup";
            Check mixer = Find("mixer");
            n[3].State = Worst("mixer", "route");
            n[3].Caption = Broken(Find("route")) ? "Lost its devices" : mixer != null && mixer.NeedsHeadset ? "No headset picked" : Broken(mixer) ? "Closed" : n[3].State == Health.Ok ? "Running" : (st.Stack ? "Not used" : "After setup");
            Check hs = Find("headset");
            n[4].State = hs != null ? hs.State : (mixer != null && mixer.NeedsHeadset ? Health.Warn : Health.Off);
            n[4].Caption = hs != null ? (hs.State == Health.Ok ? Guardian.Headset : "Unplugged or off") : (mixer != null && mixer.NeedsHeadset ? "Not picked yet" : "Not set");
            chain.Changed();
        }

        bool previewFrozen;
        void RefreshState()
        {
            if (previewFrozen) return;
            try { checks = Guardian.Audit(cat, out st); } catch { }
            lastSummary = Guardian.Summary(checks);
            foreach (KeyValuePair<string, HealthRow> kv in rows)
            {
                Check c = Find(kv.Key);
                kv.Value.Visible = c != null; if (c == null) continue;
                kv.Value.Title = c.Title;
                kv.Value.Fix.SetText(c.NeedsSetup ? "Set up" : c.NeedsHeadset ? "Pick headset" : "Fix");
                kv.Value.Set(c.State, c.Detail, c.State != Health.Ok && (c.AutoFix || c.NeedsSetup || c.NeedsHeadset));
            }
            bool needSetup = checks.Exists(delegate(Check c) { return c.NeedsSetup; });
            bool needHeadset = checks.Exists(delegate(Check c) { return c.NeedsHeadset; });
            int bad = checks.FindAll(delegate(Check c) { return c.State == Health.Warn || c.State == Health.Bad; }).Count;
            bool game = Guardian.GameRunning;
            UpdateChain(game);
            if (!busy)
            {
                if (needSetup) { heroTitle.Set("Not set up yet", Theme.Ink); heroSub.Set("One button installs ArtIsWar's free audio tools from his own site and makes every Windows sound setting his guide has you make by hand. About 3 minutes. Your sound cuts out briefly.", Theme.Dim); heroMain.SetText("Set up this PC"); }
                else if (bad > 0) { heroTitle.Set(bad + (bad == 1 ? " thing needs" : " things need") + " attention", Theme.Warn); heroSub.Set(needHeadset && bad == 1 ? "Almost there. Pick the headset you play on and the path below lights up." : "Something changed since the tune was set up. The path below shows where the sound stops. One press puts it back.", Theme.Dim); heroMain.SetText(needHeadset && bad == 1 ? "Pick your headset" : "Fix everything"); }
                else { heroTitle.Set("Your audio is protected", Theme.Accent); heroSub.Set("Every link between the game and your ears is in place" + (st.TuneSet ? ", playing " + Guardian.DescribeTune(cat, st.Current) + (st.EqSet ? " with your headset EQ." : " with the flat EQ.") : "."), Theme.Dim); heroMain.SetText("Check again"); }
                heroRecheck.Visible = needSetup || bad > 0;
            }
            nav[0].Dot = needSetup ? Theme.Bad : bad > 0 ? Theme.Warn : Theme.Accent;
            nav[2].Dot = needHeadset ? Theme.Warn : Color.Empty;
            nav[3].Dot = st.Stack && !st.EqSet ? Theme.Dim : Color.Empty;
            foreach (NavItem n in nav) n.Invalidate();
            SelectTile(styleGrid, st.Current.Style ?? cat.DefaultStyle); SelectTile(gunGrid, st.Current.Gun ?? cat.DefaultGun); SelectTile(headGrid, st.Current.Head ?? cat.DefaultHead);
            boostDb = (int)Math.Max(0, Math.Min(20, Math.Round(st.Current.BoostDb)));
            boostText.Set(Boost(boostDb), Theme.Ink);
            tuneState.Set(st.TuneSet ? "Playing now: " + Guardian.DescribeTune(cat, st.Current) + (boostDb > 0 ? ", " + Boost(boostDb) : "") + "." : "Nothing loaded yet.", st.TuneSet ? Theme.Accent : Theme.Dim);
            eqState.Set(st.EqSet ? "Using your headset EQ: " + Path.GetFileNameWithoutExtension(st.Current.EqInclude) : "Using the flat EQ. It works, but it is not matched to your headset.", st.EqSet ? Theme.Accent : Theme.Dim);
            aboutState.Set("ClearSteps " + Setup.AppVersion + ", tune library " + (st.LibraryVersion == "" ? "not installed" : st.LibraryVersion) + ", tune list from " + cat.Updated + ".\nUpdates are checked every time the app starts.", Theme.Dim);
            sideState.Text = (bad == 0 && !needSetup ? "Guardian is on" : "Needs attention") + "\n" + (game ? "Call of Duty is running" : "Game not running") + "\nVersion " + Setup.AppVersion;
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
            if (args.Length > 2 && args[1] == "--icon") { Theme.SaveIco(args[2]); return; }
            if (args.Length > 2 && args[1] == "--autostart") { Guardian.LoadSettings(); Environment.ExitCode = Guardian.SetAutostart(args[2] == "on") ? 0 : 1; return; }
            if (args.Length > 2 && args[1] == "--shots")
            {
                // Self-check used while building: draws every page to a PNG at the given size and exits.
                // An optional last argument "broken" fakes a few failed checks so the unhappy state can be reviewed too.
                int w = args.Length > 4 ? int.Parse(args[3]) : S(1180), h = args.Length > 4 ? int.Parse(args[4]) : S(760);
                var f = new MainForm(false); f.WindowState = FormWindowState.Normal; f.Location = new Point(0, 0); f.ClientSize = new Size(w, h);
                f.Show(); f.heartbeat.Stop(); f.RefreshState(); Application.DoEvents();
                if (args.Length > 5 && args[5] == "broken") f.FakeBroken();
                for (int i = 0; i < f.pages.Count; i++)
                {
                    f.ShowPage(i); Application.DoEvents();
                    using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.ClientSize)); bmp.Save(Path.Combine(args[2], "page" + (i + 1) + ".png")); }
                }
                f.tray.Visible = false; f.reallyExit = true;
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

        /// Preview only: paints the dashboard as it looks when things are wrong, without touching the PC.
        void FakeBroken()
        {
            foreach (Check c in checks)
            {
                if (c.Id == "device") { c.State = Health.Warn; c.AutoFix = true; c.Detail = "Wrong format (2 channel, 24 bit, 48000 Hz), so the game cannot send all 16 channels."; }
                if (c.Id == "mixer") { c.State = Health.Warn; c.AutoFix = true; c.Detail = "Closed, so you hear nothing from the game."; }
            }
            foreach (KeyValuePair<string, HealthRow> kv in rows)
            {
                Check c = Find(kv.Key); if (c == null) continue;
                kv.Value.Set(c.State, c.Detail, c.State != Health.Ok && c.AutoFix);
            }
            UpdateChain(false);
            heroTitle.Set("2 things need attention", Theme.Warn);
            heroSub.Set("Something changed since the tune was set up. The path below shows where the sound stops. One press puts it back.", Theme.Dim);
            heroMain.SetText("Fix everything"); heroRecheck.Visible = true; nav[0].Dot = Theme.Warn; previewFrozen = true;
            pages[0].Relayout();
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
