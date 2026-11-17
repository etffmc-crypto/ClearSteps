// ClearSteps - small custom UI kit (cards, tiles, buttons, switches, the signal-path view) with a layout that
// stretches with the window and scales with the display (100% to 300%). Keyboard focus and screen-reader names
// are built in. C# 5 compatible WinForms; all source is plain ASCII on purpose.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClearSteps
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(14, 16, 20), Side = Color.FromArgb(19, 22, 28), Surface = Color.FromArgb(25, 29, 37),
            Surface2 = Color.FromArgb(34, 39, 50), Border = Color.FromArgb(48, 54, 68), Ink = Color.FromArgb(238, 240, 244),
            Dim = Color.FromArgb(160, 168, 183), Accent = Color.FromArgb(72, 214, 138), AccentDark = Color.FromArgb(22, 62, 43),
            Warn = Color.FromArgb(244, 188, 80), Bad = Color.FromArgb(242, 106, 106);

        /// Display scale: 1.0 at 100%, 2.0 at 200%. Every pixel measurement goes through S().
        public static readonly float K = ReadScale();
        public static int S(int px) { return (int)Math.Round(px * K); }
        static float ReadScale() { try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); } catch { return 1f; } }

        static bool Has(string family) { try { using (var f = new FontFamily(family)) return true; } catch { return false; } }
        // Windows 11 ships Segoe UI Variable and Segoe Fluent Icons; Windows 10 falls back to Segoe UI and MDL2 (same glyph codes).
        static readonly string Sans = Has("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string Display = Has("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
        static readonly string Icons = Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        static readonly string SemiSuffix = Has("Segoe UI Variable Text") ? "" : " Semibold";
        static Font Semi(string family, float pt) { return SemiSuffix == "" ? new Font(family, pt, FontStyle.Bold) : new Font("Segoe UI Semibold", pt); }

        public static readonly Font Body = new Font(Sans, 10.5f), Small = new Font(Sans, 9.25f), Bold = Semi(Sans, 10.5f),
            Title = Semi(Display, 13f), H1 = Semi(Display, 20f), Hero = Semi(Display, 22f), Tag = Semi(Sans, 8.5f),
            Brand = Semi(Display, 15f), Glyph = new Font(Icons, 12.5f), GlyphSmall = new Font(Icons, 9.5f), GlyphNode = new Font(Icons, 13f),
            Mono = new Font("Consolas", 9.5f);

        public static string G(int code) { return ((char)code).ToString(); }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2; var p = new GraphicsPath();
            if (d > r.Width) d = r.Width; if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border) { FillRound(g, r, radius, fill, border, 1f); }
        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border, float borderWidth)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int inset = (int)Math.Ceiling(borderWidth / 2f);
            r = new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2 - 1, r.Height - inset * 2 - 1);
            using (GraphicsPath p = Round(r, radius))
            {
                if (fill != Color.Empty) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border != Color.Empty) using (var pen = new Pen(border, borderWidth)) g.DrawPath(pen, p);
            }
        }
        public static int TextHeight(string text, Font f, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(Math.Max(40, width), 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }
        public static int TextWidth(string text, Font f) { return TextRenderer.MeasureText(text, f, new Size(100000, 100000), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width; }
        public static void Text(Graphics g, string text, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, text, f, r, c, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | extra);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        public static void DarkTitleBar(IntPtr hwnd) { int on = 1; try { DwmSetWindowAttribute(hwnd, 20, ref on, 4); } catch { } }
        /// Gives a scrolling control Windows' own dark scrollbars instead of bright white ones.
        public static void DarkScroll(Control c)
        {
            EventHandler apply = delegate { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) apply(c, EventArgs.Empty); c.HandleCreated += apply;
        }

        /// The app mark: a green rounded square with two footprints. Drawn at any size.
        public static Bitmap Mark(int size)
        {
            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent); float u = size / 64f;
                FillRound(g, new Rectangle(0, 0, size, size), (int)(15 * u), Accent, Color.Empty, 0f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Color.FromArgb(14, 16, 20)))
                {
                    g.FillEllipse(b, 13 * u, 29 * u, 16 * u, 23 * u); g.FillEllipse(b, 15 * u, 13 * u, 12 * u, 12 * u);
                    g.FillEllipse(b, 35 * u, 21 * u, 16 * u, 23 * u); g.FillEllipse(b, 38 * u, 6 * u, 12 * u, 11 * u);
                }
            }
            return bmp;
        }
        public static Icon MakeIcon() { using (Bitmap b = Mark(64)) return Icon.FromHandle(b.GetHicon()); }

        /// Writes the mark as a multi-size .ico (PNG frames) so the exe file itself carries the icon. Used by the build.
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 256 };
            var frames = new List<byte[]>();
            foreach (int s in sizes) using (Bitmap b = Mark(s)) using (var ms = new System.IO.MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); frames.Add(ms.ToArray()); }
            using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(frames[i].Length); w.Write(offset); offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
            }
        }
    }

    public interface IFlow { int Measure(int width); }

    /// Base for hand-painted controls: flicker-free, optionally focusable with a visible keyboard focus ring.
    public class Canvas : Control
    {
        protected bool Hot, Down;
        public Canvas() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); SetStyle(ControlStyles.Selectable, false); TabStop = false; }
        protected Color Under { get { return Parent == null ? Theme.Bg : Parent.BackColor; } }
        protected void MakeFocusable(AccessibleRole role) { SetStyle(ControlStyles.Selectable, true); TabStop = true; AccessibleRole = role; Cursor = Cursors.Hand; }
        protected bool KeyboardFocus { get { return Focused && ShowFocusCues; } }
        protected void FocusRing(Graphics g, int radius) { if (KeyboardFocus) Theme.FillRound(g, ClientRectangle, radius, Color.Empty, Theme.Ink, Math.Max(2f, Theme.K * 1.5f)); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (TabStop && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)) { e.Handled = true; OnClick(EventArgs.Empty); }
            base.OnKeyDown(e);
        }
    }

    /// Word-wrapped text that reports the height it needs for a given width.
    public class TextBlock : Canvas, IFlow
    {
        public Font F; public Color C; public int MaxTextWidth = 0;
        public TextBlock(string text, Font f, Color c) { Text = text; F = f; C = c; AccessibleRole = AccessibleRole.StaticText; AccessibleName = text; }
        public void Set(string text, Color c) { if (Text == text && C == c) return; Text = text; C = c; AccessibleName = text; Invalidate(); if (Parent != null) Parent.PerformLayout(); }
        int Wrap(int width) { return MaxTextWidth > 0 ? Math.Min(width, MaxTextWidth) : width; }
        public int Measure(int width) { return Theme.TextHeight(Text, F, Wrap(width)) + Theme.S(2); }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Under); Theme.Text(e.Graphics, Text, F, new Rectangle(0, 0, Wrap(Width), Height), C, TextFormatFlags.Default); }
    }

    /// Vertical stack: every child gets the full width; IFlow children choose their own height.
    public class Stack : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = Theme.S(12); public Padding Pad = new Padding(0);
        public Stack() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); c.TabIndex = Items.Count; return c; }
        public virtual int Measure(int width)
        {
            int y = Pad.Top, w = Math.Max(60, width - Pad.Horizontal); bool any = false;
            foreach (Control c in Items)
            {
                if (!c.Visible) continue;
                var f = c as IFlow; int h = f != null ? f.Measure(w) : c.Height;
                c.SetBounds(Pad.Left, y, w, h); y += h + Gap; any = true;
            }
            return (any ? y - Gap : y) + Pad.Bottom;
        }
    }

    public class Card : Stack
    {
        public Color Fill = Theme.Surface, Edge = Theme.Border; public int Radius = Theme.S(14);
        public Card() { Pad = new Padding(Theme.S(22), Theme.S(20), Theme.S(22), Theme.S(20)); BackColor = Theme.Surface; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.FillRound(e.Graphics, ClientRectangle, Radius, Fill, Edge);
        }
    }

    /// Horizontal row. Children keep their width; the one named Fill takes what is left. Wraps when too narrow.
    public class Row : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = Theme.S(10); public Control Fill;
        public Row() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); c.TabIndex = Items.Count; return c; }
        public int Measure(int width)
        {
            int fixedW = 0, n = 0;
            foreach (Control c in Items) { if (!c.Visible) continue; n++; if (c != Fill) fixedW += c.Width; }
            int fillW = Fill != null && Fill.Visible ? Math.Max(Theme.S(120), width - fixedW - Gap * Math.Max(0, n - 1)) : 0;
            int x = 0, y = 0, lineH = 0;
            foreach (Control c in Items)
            {
                if (!c.Visible) continue;
                int w = c == Fill ? fillW : c.Width;
                var f = c as IFlow; int h = f != null ? f.Measure(w) : c.Height;
                if (x > 0 && x + w > width) { x = 0; y += lineH + Gap; lineH = 0; }
                c.SetBounds(x, y, w, h); x += w + Gap; if (h > lineH) lineH = h;
            }
            if (y == 0) foreach (Control c in Items) if (c.Visible && c.Height < lineH) c.Top = (lineH - c.Height) / 2;
            return y + lineH;
        }
    }

    /// Responsive grid of equal-width cells: as many columns as fit, up to MaxCols.
    public class Grid : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = Theme.S(12), MinCell = Theme.S(240), MaxCols = 3;
        public Grid() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); c.TabIndex = Items.Count; return c; }
        public void Clear() { foreach (Control c in Items) { Controls.Remove(c); c.Dispose(); } Items.Clear(); }
        public int Measure(int width)
        {
            var vis = Items.FindAll(delegate(Control c) { return c.Visible; });
            if (vis.Count == 0) return 0;
            int cols = Math.Max(1, Math.Min(Math.Min(MaxCols, vis.Count), (width + Gap) / (MinCell + Gap)));
            int cw = (width - Gap * (cols - 1)) / cols, y = 0;
            for (int i = 0; i < vis.Count; i += cols)
            {
                int rowH = 0;
                for (int k = i; k < Math.Min(i + cols, vis.Count); k++) { var f = vis[k] as IFlow; int h = f != null ? f.Measure(cw) : vis[k].Height; if (h > rowH) rowH = h; }
                for (int k = i; k < Math.Min(i + cols, vis.Count); k++) vis[k].SetBounds((k - i) * (cw + Gap), y, cw, rowH);
                y += rowH + Gap;
            }
            return y - Gap;
        }
    }

    /// An option card: title, small tag, wrapped description. Selectable ones behave like radio buttons.
    public class Tile : Canvas, IFlow
    {
        public string Title = "", Tag2 = "", Body = ""; public Color TagColor = Theme.Accent; public bool Selected; public object Data;
        bool clickable = true;
        public bool Clickable { get { return clickable; } set { clickable = value; if (!value) { SetStyle(ControlStyles.Selectable, false); TabStop = false; Cursor = Cursors.Default; AccessibleRole = AccessibleRole.StaticText; } } }
        public Tile() { MakeFocusable(AccessibleRole.RadioButton); }
        int P { get { return Theme.S(16); } }
        int TagWidth() { return Tag2 == "" ? 0 : Theme.TextWidth(Tag2, Theme.Tag) + Theme.S(26); }
        public int Measure(int width)
        {
            AccessibleName = Title + (Tag2 == "" ? "" : ", " + Tag2) + (Body == "" ? "" : ". " + Body);
            int inner = width - P * 2;
            return P + Theme.TextHeight(Title, Theme.Bold, inner - TagWidth()) + (Body == "" ? 0 : Theme.S(6) + Theme.TextHeight(Body, Theme.Small, inner)) + P;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            bool live = clickable && Hot;
            Color fill = Selected ? Theme.AccentDark : live ? Theme.Border : Theme.Surface2;
            Color edge = Selected ? Theme.Accent : live ? Theme.Dim : (clickable ? Theme.Border : Color.Empty);
            Theme.FillRound(g, ClientRectangle, Theme.S(10), fill, edge, Selected ? Math.Max(1.5f, Theme.K) : 1f);
            int inner = Width - P * 2, tw = TagWidth();
            int th = Theme.TextHeight(Title, Theme.Bold, inner - tw);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(P, P, inner - tw, th), Theme.Ink, TextFormatFlags.Default);
            if (Tag2 != "")
            {
                int pillW = tw - Theme.S(8), pillH = Theme.S(22);
                var tr = new Rectangle(Width - P - pillW, P - Theme.S(1), pillW, pillH);
                Theme.FillRound(g, tr, pillH / 2, Color.FromArgb(44, TagColor), Color.Empty, 0f);
                TextRenderer.DrawText(g, Tag2, Theme.Tag, tr, TagColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            if (Body != "") Theme.Text(g, Body, Theme.Small, new Rectangle(P, P + th + Theme.S(6), inner, Height), Theme.Dim, TextFormatFlags.Default);
            FocusRing(g, Theme.S(10));
        }
    }

    public class Btn : Canvas
    {
        public bool Primary;
        public Btn(string text, bool primary) { Primary = primary; MakeFocusable(AccessibleRole.PushButton); Height = Theme.S(40); SetText(text); }
        public void SetText(string t) { if (Text == t && Width > 0) return; Text = t; AccessibleName = t; Width = Theme.TextWidth(t, Theme.Bold) + Theme.S(40); Invalidate(); if (Parent != null) Parent.PerformLayout(); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            Color fill = Primary ? (Down ? Color.FromArgb(52, 172, 110) : Hot ? Color.FromArgb(104, 230, 162) : Theme.Accent) : (Down ? Theme.Surface : Hot ? Theme.Border : Theme.Surface2);
            if (!Enabled) fill = Theme.Surface2;
            Theme.FillRound(g, ClientRectangle, Theme.S(9), fill, Primary || !Enabled ? Color.Empty : Theme.Border);
            TextRenderer.DrawText(g, Text, Theme.Bold, ClientRectangle, !Enabled ? Theme.Dim : Primary ? Color.FromArgb(6, 26, 16) : Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            FocusRing(g, Theme.S(9));
        }
    }

    /// On/off switch with a label and a one-line explanation.
    public class Toggle : Canvas, IFlow
    {
        public bool On; public string Title, Body; public event EventHandler Changed;
        public Toggle(string title, string body) { Title = title; Body = body; MakeFocusable(AccessibleRole.CheckButton); AccessibleName = title + ". " + body; }
        int TextLeft { get { return Theme.S(62); } }
        public int Measure(int width) { int w = width - TextLeft - Theme.S(8); return Math.Max(Theme.S(40), Theme.TextHeight(Title, Theme.Bold, w) + Theme.TextHeight(Body, Theme.Small, w) + Theme.S(6)); }
        protected override void OnClick(EventArgs e) { On = !On; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            int sw = Theme.S(46), sh = Theme.S(26), knob = Theme.S(18), pad = Theme.S(4);
            var track = new Rectangle(Theme.S(2), Theme.S(3), sw, sh);
            Theme.FillRound(g, track, sh / 2, On ? Theme.Accent : Theme.Border, Color.Empty, 0f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(On ? Color.FromArgb(6, 26, 16) : Theme.Dim)) g.FillEllipse(b, On ? track.Right - knob - pad : track.Left + pad, track.Top + (sh - knob) / 2, knob, knob);
            int w = Width - TextLeft - Theme.S(8), th = Theme.TextHeight(Title, Theme.Bold, w);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(TextLeft, 0, w, th), Theme.Ink, TextFormatFlags.Default);
            Theme.Text(g, Body, Theme.Small, new Rectangle(TextLeft, th + Theme.S(2), w, Height), Theme.Dim, TextFormatFlags.Default);
            if (KeyboardFocus) Theme.FillRound(g, new Rectangle(0, 0, sw + Theme.S(4), sh + Theme.S(6)), sh / 2 + Theme.S(2), Color.Empty, Theme.Ink, Math.Max(2f, Theme.K * 1.5f));
        }
    }

    public enum Health { Ok, Warn, Bad, Off }

    /// One line of the health check: coloured mark, what it is, what was found, and an optional Fix button.
    public class HealthRow : Canvas, IFlow
    {
        public Health State = Health.Off; public string Title = "", Detail = ""; public readonly Btn Fix = new Btn("Fix", false);
        public HealthRow() { Fix.Height = Theme.S(34); Fix.Visible = false; Controls.Add(Fix); AccessibleRole = AccessibleRole.StaticText; }
        public void Set(Health s, string detail, bool showFix)
        {
            State = s; Detail = detail; Fix.Visible = showFix;
            AccessibleName = Title + ": " + (s == Health.Ok ? "OK. " : s == Health.Off ? "" : "Needs attention. ") + detail; Invalidate();
        }
        int Dot { get { return Theme.S(28); } }
        int TextLeft { get { return Dot + Theme.S(14); } }
        public int Measure(int width)
        {
            int textW = width - TextLeft - (Fix.Visible ? Fix.Width + Theme.S(16) : 0);
            int h = Math.Max(Theme.S(48), Theme.S(8) + Theme.TextHeight(Title, Theme.Bold, textW) + Theme.TextHeight(Detail, Theme.Small, textW) + Theme.S(12));
            Fix.Location = new Point(width - Fix.Width, (h - Fix.Height) / 2);
            return h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = State == Health.Ok ? Theme.Accent : State == Health.Warn ? Theme.Warn : State == Health.Bad ? Theme.Bad : Theme.Border;
            var dot = new Rectangle(0, Theme.S(8), Dot, Dot);
            using (var b = new SolidBrush(Color.FromArgb(48, c))) g.FillEllipse(b, dot);
            string mark = Theme.G(State == Health.Ok ? 0xE73E : State == Health.Off ? 0xE738 : 0xE783);
            TextRenderer.DrawText(g, mark, Theme.GlyphSmall, dot, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int textW = Width - TextLeft - (Fix.Visible ? Fix.Width + Theme.S(16) : 0);
            int th = Theme.TextHeight(Title, Theme.Bold, textW);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(TextLeft, Theme.S(6), textW, th), Theme.Ink, TextFormatFlags.Default);
            Theme.Text(g, Detail, Theme.Small, new Rectangle(TextLeft, Theme.S(6) + th + Theme.S(1), textW, Height), State == Health.Ok || State == Health.Off ? Theme.Dim : c, TextFormatFlags.Default);
        }
    }

    public class ChainNode { public string Name = "", Caption = "", Glyph = ""; public Health State = Health.Off; }

    /// The signal path: where the game's sound travels on its way to your ears, and where it is broken if it is.
    /// This is the one picture that explains the whole product, so the dashboard leads with it.
    public class ChainView : Canvas, IFlow
    {
        public readonly List<ChainNode> Nodes = new List<ChainNode>();
        public ChainView() { AccessibleRole = AccessibleRole.Diagram; }
        public void Changed()
        {
            var sb = new System.Text.StringBuilder("Sound path: ");
            foreach (ChainNode n in Nodes) sb.Append(n.Name + " " + (n.State == Health.Ok ? "OK" : n.State == Health.Off ? "idle" : "needs attention") + ", " + n.Caption + ". ");
            AccessibleName = sb.ToString(); Invalidate();
        }
        int Disc { get { return Theme.S(46); } }
        public int Measure(int width)
        {
            if (Nodes.Count == 0) return 0;
            int col = width / Nodes.Count, cap = 0;
            foreach (ChainNode n in Nodes) cap = Math.Max(cap, Theme.TextHeight(n.Caption, Theme.Small, col - Theme.S(12)));
            return Theme.S(6) + Disc + Theme.S(10) + Theme.TextHeight("Ag", Theme.Bold, 500) + Theme.S(2) + cap + Theme.S(4);
        }
        static Color Tone(Health h) { return h == Health.Ok ? Theme.Accent : h == Health.Warn ? Theme.Warn : h == Health.Bad ? Theme.Bad : Theme.Border; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Nodes.Count == 0) return;
            int col = Width / Nodes.Count, top = Theme.S(6), cy = top + Disc / 2;
            // Links first, so the discs sit on top. A link is lit only when sound can actually pass through both ends.
            for (int i = 0; i < Nodes.Count - 1; i++)
            {
                bool flows = Nodes[i].State == Health.Ok && Nodes[i + 1].State == Health.Ok;
                bool broken = Nodes[i + 1].State == Health.Warn || Nodes[i + 1].State == Health.Bad || Nodes[i].State == Health.Warn || Nodes[i].State == Health.Bad;
                int x1 = i * col + col / 2 + Disc / 2 + Theme.S(8), x2 = (i + 1) * col + col / 2 - Disc / 2 - Theme.S(8);
                using (var pen = new Pen(flows ? Theme.Accent : broken ? Color.FromArgb(150, Theme.Warn) : Theme.Border, Math.Max(2f, Theme.K * 2f)))
                {
                    if (!flows) pen.DashPattern = new float[] { 2f, 2.5f };
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, x1, cy, x2, cy);
                }
            }
            for (int i = 0; i < Nodes.Count; i++)
            {
                ChainNode n = Nodes[i]; Color c = Tone(n.State);
                int cx = i * col + col / 2; var disc = new Rectangle(cx - Disc / 2, top, Disc, Disc);
                using (var b = new SolidBrush(n.State == Health.Off ? Theme.Surface2 : Color.FromArgb(46, c))) g.FillEllipse(b, disc);
                using (var pen = new Pen(c, Math.Max(1.5f, Theme.K * 1.5f))) g.DrawEllipse(pen, disc);
                TextRenderer.DrawText(g, n.Glyph, Theme.GlyphNode, disc, n.State == Health.Off ? Theme.Dim : c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                int ty = top + Disc + Theme.S(10), nh = Theme.TextHeight("Ag", Theme.Bold, 500);
                var nameRect = new Rectangle(i * col + Theme.S(4), ty, col - Theme.S(8), nh);
                TextRenderer.DrawText(g, n.Name, Theme.Bold, nameRect, Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var capRect = new Rectangle(i * col + Theme.S(6), ty + nh + Theme.S(2), col - Theme.S(12), Height);
                TextRenderer.DrawText(g, n.Caption, Theme.Small, capRect, n.State == Health.Warn || n.State == Health.Bad ? c : Theme.Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }

    public class NavItem : Canvas
    {
        public string Glyph; public bool Selected; public Color Dot = Color.Empty;
        public NavItem(string glyph, string text) { Glyph = glyph; Text = text; AccessibleName = text; MakeFocusable(AccessibleRole.PageTab); Height = Theme.S(44); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Theme.Side);
            int m = Theme.S(10);
            var pill = new Rectangle(m, Theme.S(2), Width - m * 2, Height - Theme.S(4));
            if (Selected || Hot) Theme.FillRound(g, pill, Theme.S(9), Selected ? Theme.Surface2 : Theme.Surface, Color.Empty, 0f);
            if (Selected) Theme.FillRound(g, new Rectangle(m, Height / 2 - Theme.S(9), Theme.S(4), Theme.S(18)), Theme.S(2), Theme.Accent, Color.Empty, 0f);
            TextRenderer.DrawText(g, Glyph, Theme.Glyph, new Rectangle(Theme.S(22), 0, Theme.S(30), Height), Selected ? Theme.Accent : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int textLeft = Theme.S(60), dotRoom = Dot != Color.Empty ? Theme.S(30) : Theme.S(16);
            TextRenderer.DrawText(g, Text, Selected ? Theme.Bold : Theme.Body, new Rectangle(textLeft, 0, Width - textLeft - dotRoom, Height), Selected ? Theme.Ink : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Dot != Color.Empty) { g.SmoothingMode = SmoothingMode.AntiAlias; int d = Theme.S(8); using (var b = new SolidBrush(Dot)) g.FillEllipse(b, Width - Theme.S(28), Height / 2 - d / 2, d, d); }
            if (KeyboardFocus) Theme.FillRound(g, pill, Theme.S(9), Color.Empty, Theme.Ink, Math.Max(2f, Theme.K * 1.5f));
        }
    }

    /// A thin strip that slides while work is in progress, so a long step never looks frozen.
    public class BusyBar : Canvas
    {
        readonly Timer timer = new Timer { Interval = 16 }; float pos; bool active;
        public BusyBar() { Height = Theme.S(3); timer.Tick += delegate { pos += 0.012f; if (pos > 1.3f) pos = -0.3f; Invalidate(); }; }
        public bool Active { get { return active; } set { if (active == value) return; active = value; pos = -0.3f; if (value) timer.Start(); else timer.Stop(); Invalidate(); } }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            if (!active) return;
            int w = (int)(Width * 0.3f), x = (int)(Width * pos);
            using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, x, 0, w, Height);
        }
    }

    /// A scrolling page whose content column stretches with the window (up to a comfortable maximum) and stays centred.
    public class Page : Panel
    {
        public readonly Stack Root = new Stack(); public int MaxWidth = Theme.S(1500);
        public Page()
        {
            AutoScroll = true; BackColor = Theme.Bg; DoubleBuffered = true;
            Root.Gap = Theme.S(18); Root.Pad = new Padding(0, Theme.S(28), 0, Theme.S(36)); Controls.Add(Root); Theme.DarkScroll(this);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int avail = ClientSize.Width - Theme.S(72); if (avail < Theme.S(300)) return;
            int w = Math.Min(MaxWidth, avail);
            int h = Root.Measure(w);
            Root.SetBounds((ClientSize.Width - w) / 2 + AutoScrollPosition.X, AutoScrollPosition.Y, w, h);
        }
        public void Relayout() { PerformLayout(); Invalidate(true); }
    }
}
