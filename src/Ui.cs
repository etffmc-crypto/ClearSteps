// ClearSteps - small custom UI kit (rounded cards, tiles, buttons, switches) with a layout that stretches
// with the window. C# 5 compatible WinForms, everything painted by hand so it looks the same on every PC.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClearSteps
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(13, 15, 19), Side = Color.FromArgb(18, 21, 27), Surface = Color.FromArgb(24, 28, 36),
            Surface2 = Color.FromArgb(33, 38, 48), Border = Color.FromArgb(45, 51, 64), Ink = Color.FromArgb(236, 238, 242),
            Dim = Color.FromArgb(152, 160, 175), Accent = Color.FromArgb(72, 214, 138), AccentDark = Color.FromArgb(20, 60, 40),
            Warn = Color.FromArgb(242, 184, 75), Bad = Color.FromArgb(240, 100, 100);
        public static readonly Font Body = new Font("Segoe UI", 10.5f), Small = new Font("Segoe UI", 9f), Bold = new Font("Segoe UI Semibold", 10.5f),
            Title = new Font("Segoe UI Semibold", 13f), H1 = new Font("Segoe UI Semibold", 21f), Hero = new Font("Segoe UI Semibold", 26f),
            Tag = new Font("Segoe UI Semibold", 8f), Glyph = new Font("Segoe MDL2 Assets", 13f), GlyphBig = new Font("Segoe MDL2 Assets", 22f);

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2; var p = new GraphicsPath();
            if (d > r.Width) d = r.Width; if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            r.Width -= 1; r.Height -= 1;
            using (GraphicsPath p = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (border != Color.Empty) using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
        }
        public static int TextHeight(string text, Font f, int width)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(Math.Max(40, width), 100000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
        }
        public static void Text(Graphics g, string text, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            TextRenderer.DrawText(g, text, f, r, c, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | extra);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public static void DarkTitleBar(IntPtr hwnd) { int on = 1; try { DwmSetWindowAttribute(hwnd, 20, ref on, 4); } catch { } }

        /// The app icon, drawn at runtime: a green rounded square with two "steps".
        public static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(64, 64))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    FillRound(g, new Rectangle(2, 2, 60, 60), 14, Accent, Color.Empty);
                    using (var b = new SolidBrush(Color.FromArgb(13, 15, 19)))
                    {
                        g.FillEllipse(b, 14, 30, 16, 22); g.FillEllipse(b, 16, 14, 11, 11);
                        g.FillEllipse(b, 35, 22, 16, 22); g.FillEllipse(b, 38, 8, 11, 10);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    public interface IFlow { int Measure(int width); }

    public class Canvas : Control
    {
        public Canvas() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true); }
        protected Color Under { get { return Parent == null ? Theme.Bg : Parent.BackColor; } }
    }

    /// Word-wrapped text that reports the height it needs for a given width.
    public class TextBlock : Canvas, IFlow
    {
        public Font F = Theme.Body; public Color C = Theme.Dim;
        public TextBlock(string text, Font f, Color c) { Text = text; F = f; C = c; }
        public void Set(string text, Color c) { if (Text == text && C == c) return; Text = text; C = c; Invalidate(); if (Parent != null) Parent.PerformLayout(); }
        public int Measure(int width) { return Theme.TextHeight(Text, F, width) + 2; }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Under); Theme.Text(e.Graphics, Text, F, ClientRectangle, C, TextFormatFlags.Default); }
    }

    /// Vertical stack: every child gets the full width; IFlow children choose their own height.
    public class Stack : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = 12; public Padding Pad = new Padding(0);
        public Stack() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); return c; }
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
        public Color Fill = Theme.Surface, Edge = Theme.Border;
        public Card() { Pad = new Padding(22, 20, 22, 20); BackColor = Theme.Surface; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Bg : Parent.BackColor);
            Theme.FillRound(e.Graphics, ClientRectangle, 14, Fill, Edge);
        }
    }

    /// Horizontal row. Children keep their width; the one named Fill takes what is left. Wraps when too narrow.
    public class Row : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = 10; public Control Fill;
        public Row() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); return c; }
        public int Measure(int width)
        {
            int fixedW = 0, n = 0;
            foreach (Control c in Items) { if (!c.Visible) continue; n++; if (c != Fill) fixedW += c.Width; }
            int fillW = Fill != null && Fill.Visible ? Math.Max(120, width - fixedW - Gap * Math.Max(0, n - 1)) : 0;
            int x = 0, y = 0, lineH = 0;
            foreach (Control c in Items)
            {
                if (!c.Visible) continue;
                int w = c == Fill ? fillW : c.Width;
                var f = c as IFlow; int h = f != null ? f.Measure(w) : c.Height;
                if (x > 0 && x + w > width) { x = 0; y += lineH + Gap; lineH = 0; }
                c.SetBounds(x, y, w, h); x += w + Gap; if (h > lineH) lineH = h;
            }
            // centre shorter children vertically on single-line rows
            if (y == 0) foreach (Control c in Items) if (c.Visible && c.Height < lineH) c.Top = (lineH - c.Height) / 2;
            return y + lineH;
        }
    }

    /// Responsive grid of equal-width cells: as many columns as fit, up to MaxCols.
    public class Grid : Panel, IFlow
    {
        public readonly List<Control> Items = new List<Control>(); public int Gap = 12, MinCell = 240, MaxCols = 3;
        public Grid() { DoubleBuffered = true; }
        public T Add<T>(T c) where T : Control { Items.Add(c); Controls.Add(c); return c; }
        public void Clear() { foreach (Control c in Items) { Controls.Remove(c); c.Dispose(); } Items.Clear(); }
        public int Measure(int width)
        {
            if (Items.Count == 0) return 0;
            int cols = Math.Max(1, Math.Min(Math.Min(MaxCols, Items.Count), (width + Gap) / (MinCell + Gap)));
            int cw = (width - Gap * (cols - 1)) / cols, y = 0;
            for (int i = 0; i < Items.Count; i += cols)
            {
                int rowH = 0;
                for (int k = i; k < Math.Min(i + cols, Items.Count); k++) { var f = Items[k] as IFlow; int h = f != null ? f.Measure(cw) : Items[k].Height; if (h > rowH) rowH = h; }
                for (int k = i; k < Math.Min(i + cols, Items.Count); k++) Items[k].SetBounds((k - i) * (cw + Gap), y, cw, rowH);
                y += rowH + Gap;
            }
            return y - Gap;
        }
    }

    /// A selectable option card: title, small tag, wrapped description.
    public class Tile : Canvas, IFlow
    {
        public string Title = "", Tag2 = "", Body = ""; public Color TagColor = Theme.Accent; public bool Selected, Clickable = true; public object Data; bool hot;
        public Tile() { Cursor = Cursors.Hand; }
        public int Measure(int width)
        {
            int inner = width - 32;
            return 16 + Theme.TextHeight(Title, Theme.Bold, inner - TagWidth()) + (Body == "" ? 0 : 6 + Theme.TextHeight(Body, Theme.Small, inner)) + 16;
        }
        int TagWidth() { return Tag2 == "" ? 0 : TextRenderer.MeasureText(Tag2, Theme.Tag).Width + 22; }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            Color fill = Selected ? Theme.AccentDark : (hot && Clickable ? Theme.Surface2 : Theme.Surface2);
            Color edge = Selected ? Theme.Accent : (hot && Clickable ? Theme.Dim : Theme.Border);
            Theme.FillRound(g, ClientRectangle, 12, fill, edge);
            int inner = Width - 32, tw = TagWidth();
            int th = Theme.TextHeight(Title, Theme.Bold, inner - tw);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(16, 16, inner - tw, th), Theme.Ink, TextFormatFlags.Default);
            if (Tag2 != "")
            {
                var tr = new Rectangle(Width - 16 - (tw - 8), 15, tw - 8, 20);
                Theme.FillRound(g, tr, 10, Color.FromArgb(40, TagColor), Color.Empty);
                TextRenderer.DrawText(g, Tag2, Theme.Tag, tr, TagColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            if (Body != "") Theme.Text(g, Body, Theme.Small, new Rectangle(16, 16 + th + 6, inner, Height), Theme.Dim, TextFormatFlags.Default);
        }
    }

    public class Btn : Canvas
    {
        public bool Primary; bool hot, down;
        public Btn(string text, bool primary) { Text = text; Primary = primary; Cursor = Cursors.Hand; Height = 40; Width = TextRenderer.MeasureText(text, Theme.Bold).Width + 44; }
        public void SetText(string t) { if (Text == t) return; Text = t; Width = TextRenderer.MeasureText(t, Theme.Bold).Width + 44; Invalidate(); if (Parent != null) Parent.PerformLayout(); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            Color fill = Primary ? (down ? Color.FromArgb(52, 170, 108) : hot ? Color.FromArgb(96, 228, 158) : Theme.Accent) : (down ? Theme.Border : hot ? Theme.Border : Theme.Surface2);
            if (!Enabled) fill = Theme.Surface2;
            Theme.FillRound(g, ClientRectangle, 10, fill, Primary || !Enabled ? Color.Empty : Theme.Border);
            TextRenderer.DrawText(g, Text, Theme.Bold, ClientRectangle, !Enabled ? Theme.Dim : Primary ? Color.FromArgb(8, 24, 16) : Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// On/off switch with a label and a one-line explanation.
    public class Toggle : Canvas, IFlow
    {
        public bool On; public string Title, Body; public event EventHandler Changed;
        public Toggle(string title, string body) { Title = title; Body = body; Cursor = Cursors.Hand; }
        public int Measure(int width) { return Math.Max(40, Theme.TextHeight(Title, Theme.Bold, width - 70) + Theme.TextHeight(Body, Theme.Small, width - 70) + 6); }
        protected override void OnClick(EventArgs e) { On = !On; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under);
            var sw = new Rectangle(0, 4, 46, 26);
            Theme.FillRound(g, sw, 13, On ? Theme.Accent : Theme.Border, Color.Empty);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(On ? Color.FromArgb(8, 24, 16) : Theme.Dim)) g.FillEllipse(b, On ? 23 : 4, 8, 18, 18);
            int th = Theme.TextHeight(Title, Theme.Bold, Width - 70);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(62, 0, Width - 70, th), Theme.Ink, TextFormatFlags.Default);
            Theme.Text(g, Body, Theme.Small, new Rectangle(62, th + 2, Width - 70, Height), Theme.Dim, TextFormatFlags.Default);
        }
    }

    public enum Health { Ok, Warn, Bad, Off }

    /// One line of the health check: coloured mark, what it is, what was found, and an optional Fix button.
    public class HealthRow : Canvas, IFlow
    {
        public Health State = Health.Off; public string Title = "", Detail = ""; public readonly Btn Fix = new Btn("Fix", false);
        public HealthRow() { Fix.Height = 34; Fix.Visible = false; Controls.Add(Fix); }
        public void Set(Health s, string detail, bool showFix) { State = s; Detail = detail; Fix.Visible = showFix; Invalidate(); }
        public int Measure(int width)
        {
            int textW = width - 52 - (Fix.Visible ? Fix.Width + 16 : 0);
            int h = Math.Max(46, 10 + Theme.TextHeight(Title, Theme.Bold, textW) + Theme.TextHeight(Detail, Theme.Small, textW) + 12);
            Fix.Location = new Point(width - Fix.Width, (h - Fix.Height) / 2);
            return h;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Under); g.SmoothingMode = SmoothingMode.AntiAlias;
            Color c = State == Health.Ok ? Theme.Accent : State == Health.Warn ? Theme.Warn : State == Health.Bad ? Theme.Bad : Theme.Border;
            using (var b = new SolidBrush(Color.FromArgb(46, c))) g.FillEllipse(b, 0, 9, 30, 30);
            string mark = ((char)(State == Health.Ok ? 0xE73E : State == Health.Off ? 0xE738 : 0xE783)).ToString();
            TextRenderer.DrawText(g, mark, new Font("Segoe MDL2 Assets", 10f), new Rectangle(0, 9, 31, 31), c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            int textW = Width - 52 - (Fix.Visible ? Fix.Width + 16 : 0);
            int th = Theme.TextHeight(Title, Theme.Bold, textW);
            Theme.Text(g, Title, Theme.Bold, new Rectangle(44, 8, textW, th), Theme.Ink, TextFormatFlags.Default);
            Theme.Text(g, Detail, Theme.Small, new Rectangle(44, 8 + th + 1, textW, Height), State == Health.Ok || State == Health.Off ? Theme.Dim : c, TextFormatFlags.Default);
        }
    }

    public class NavItem : Canvas
    {
        public string Glyph; public bool Selected; public Color Dot = Color.Empty; bool hot;
        public NavItem(string glyph, string text) { Glyph = glyph; Text = text; Height = 46; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Theme.Side);
            if (Selected || hot) Theme.FillRound(g, new Rectangle(10, 2, Width - 20, Height - 4), 10, Selected ? Theme.Surface2 : Theme.Surface, Color.Empty);
            if (Selected) Theme.FillRound(g, new Rectangle(10, 12, 4, Height - 24), 2, Theme.Accent, Color.Empty);
            TextRenderer.DrawText(g, Glyph, Theme.Glyph, new Rectangle(24, 0, 32, Height), Selected ? Theme.Accent : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Text, Selected ? Theme.Bold : Theme.Body, new Rectangle(60, 0, Width - 90, Height), Selected ? Theme.Ink : Theme.Dim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (Dot != Color.Empty) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(Dot)) g.FillEllipse(b, Width - 32, Height / 2 - 4, 8, 8); }
        }
    }

    /// A scrolling page whose content column stretches with the window (up to a comfortable maximum) and stays centred.
    public class Page : Panel
    {
        public readonly Stack Root = new Stack(); public int MaxWidth = 1500;
        public Page() { AutoScroll = true; BackColor = Theme.Bg; DoubleBuffered = true; Root.Gap = 18; Root.Pad = new Padding(0, 30, 0, 36); Controls.Add(Root); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int avail = ClientSize.Width - 72; if (avail < 300) return;
            int w = Math.Min(MaxWidth, avail);
            int h = Root.Measure(w);
            Root.SetBounds((ClientSize.Width - w) / 2 + AutoScrollPosition.X, AutoScrollPosition.Y, w, h);
        }
        public void Relayout() { PerformLayout(); Invalidate(true); }
    }
}
