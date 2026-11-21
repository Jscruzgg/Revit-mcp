// Window design system, taken from the RR / Kovach tools (Ui.cs of Jscruzgg/Revit-Rhino--Bridge): Fluent 2 base + Carbon-style status pills.
// Only what the Bridge window uses: colours, fonts, header, 12 -> 14 px scaling, rounded buttons, owner-drawn tables with pills.
// C# 5 syntax (compiled with the .NET Framework csc for Revit 2024).
using System;
using System.Collections.Generic;
using System.Linq;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace RevitMultiBridge
{
    // dictionary that does not keep its keys alive (ConditionalWeakTable): per-control settings of the theme. A plain static
    // Dictionary kept every closed window (and the package it had read) in memory until Revit closed (16 Oct 2026)
    class WeakMap<K, V> where K : class
    {
        class Box { public V Value; }
        readonly System.Runtime.CompilerServices.ConditionalWeakTable<K, Box> t = new System.Runtime.CompilerServices.ConditionalWeakTable<K, Box>();
        public V this[K key]
        {
            get { Box b; if (!t.TryGetValue(key, out b)) throw new KeyNotFoundException(); return b.Value; }
            set { t.GetValue(key, k => new Box()).Value = value; }
        }
        public bool TryGetValue(K key, out V value) { Box b; if (t.TryGetValue(key, out b)) { value = b.Value; return true; } value = default(V); return false; }
        public bool Remove(K key) { return t.Remove(key); }
    }

    class Pill
    {
        public SD.Color Back, Fore, Border;
        public Pill(SD.Color b, SD.Color f, SD.Color border) { Back = b; Fore = f; Border = border; }
    }

    static class Ui
    {
        // colours of the Cladding BIM design system (6 Nov 2026, user): primary #0F4C53, cold neutrals, no beige, one typeface (Segoe UI)
        public static readonly SD.Color Page = SD.Color.FromArgb(0xFF, 0xFF, 0xFF);   // Homie surface-page
        public static readonly SD.Color Surface = SD.Color.White;
        public static readonly SD.Color Text = SD.Color.FromArgb(0x14, 0x14, 0x1A);   // Homie text-primary
        public static readonly SD.Color Text2 = SD.Color.FromArgb(0x53, 0x53, 0x5F);   // Homie text-secondary
        public static readonly SD.Color Border = SD.Color.FromArgb(0xD9, 0xD9, 0xDE);   // Homie border-default
        public static readonly SD.Color BorderStrong = SD.Color.FromArgb(0xC4, 0xC4, 0xCA);   // Homie border-input
        public static readonly SD.Color Hover = SD.Color.FromArgb(0xEC, 0xEC, 0xEF);   // Homie surface-pill
        public static readonly SD.Color Selected = SD.Color.FromArgb(0xE7, 0xE7, 0xF4);   // Homie primary-100
        public static readonly SD.Color Brand = SD.Color.FromArgb(0x56, 0x56, 0x99);   // Homie action-primary
        public static readonly SD.Color BrandHover = SD.Color.FromArgb(0x48, 0x48, 0x7F);   // primary-700
        public static readonly SD.Color BrandPress = SD.Color.FromArgb(0x39, 0x39, 0x65);   // primary-800
        public static readonly SD.Color BrandSoft = SD.Color.FromArgb(0xE7, 0xE7, 0xF4);   // primary-100
        public static readonly SD.Color Card = SD.Color.FromArgb(0xF6, 0xF6, 0xF8);   // Homie surface-card (fill, no border, no shadow)
        public static readonly SD.Color Disabled = SD.Color.FromArgb(0xEC, 0xEC, 0xEF);
        public static readonly SD.Color DisabledText = SD.Color.FromArgb(0x81, 0x81, 0x8D);
        public static readonly SD.Color DangerColor = SD.Color.FromArgb(0xDA, 0x00, 0x00);   // Homie text-danger
        public static readonly SD.Color DangerHover = SD.Color.FromArgb(0x97, 0x00, 0x00);   // text-danger-strong
        // font (user, 22 Oct 2026): Segoe UI Variable (Windows 11, the native font closest to the design system's Inter), Segoe UI where
        // it is not installed (Windows 10). Windows code keeps writing "Segoe UI" / "Segoe UI Semibold": Apply maps them (MapFamily).
        public static readonly string TextFamily = Installed("Rethink Sans") ? "Rethink Sans" : "Segoe UI";   // Homie: Rethink Sans, Segoe UI fallback
        public static readonly string SemiboldFamily = Installed("Rethink Sans Medium") ? "Rethink Sans Medium" : Installed("Rethink Sans") ? "Rethink Sans" : "Segoe UI Semibold";
        static bool Installed(string family)
        {
            try { using (var f = new SD.Font(family, 10f)) return f.Name == family; }   // a missing family comes back as Microsoft Sans Serif
            catch { return false; }
        }
        public static string MapFamily(string family)
        {
            return family == "Segoe UI" ? TextFamily : family == "Segoe UI Semibold" ? SemiboldFamily : family;
        }
        // whole-pixel sizes at 96 DPI (10.5 pt = 14 px = Fluent body, 9.75 pt = 13 px); fractional sizes render jagged with GDI
        public static readonly SD.Font Body = new SD.Font(TextFamily, 10.5f);
        public static readonly SD.Font Strong = new SD.Font(SemiboldFamily, 10.5f);
        public static readonly SD.Font Small = new SD.Font(TextFamily, 9.75f);

        // status tags = solid chips with white text (StatusChip); "Orange" shares the warning colour
        // Homie chips: soft tint + dark text, fully rounded (never solid)
        public static readonly Pill Red = new Pill(SD.Color.FromArgb(0xEA, 0xDC, 0xDC), SD.Color.FromArgb(0x97, 0x00, 0x00), SD.Color.FromArgb(0xEA, 0xDC, 0xDC));
        public static readonly Pill Green = new Pill(SD.Color.FromArgb(0xE3, 0xEE, 0xDF), SD.Color.FromArgb(0x44, 0x70, 0x3B), SD.Color.FromArgb(0xE3, 0xEE, 0xDF));
        public static readonly Pill Amber = new Pill(SD.Color.FromArgb(0xFA, 0xF2, 0xDC), SD.Color.FromArgb(0x85, 0x4D, 0x0E), SD.Color.FromArgb(0xFA, 0xF2, 0xDC));
        public static readonly Pill Blue = new Pill(SD.Color.FromArgb(0xE7, 0xE7, 0xF4), SD.Color.FromArgb(0x48, 0x48, 0x7F), SD.Color.FromArgb(0xE7, 0xE7, 0xF4));
        public static readonly Pill Orange = Amber;
        public static readonly Pill Gray = new Pill(SD.Color.FromArgb(0xEC, 0xEC, 0xEF), SD.Color.FromArgb(0x53, 0x53, 0x5F), SD.Color.FromArgb(0xEC, 0xEC, 0xEF));

        public static Pill PillFor(string status)
        {
            string s = (status ?? "").ToUpperInvariant();
            if (s.StartsWith("FAILED") || s.StartsWith("ERROR") || s.StartsWith("STOPPED")) return Red;
            if (s.StartsWith("OK") || s.StartsWith("READY") || s.StartsWith("EXECUTED")) return Green;
            if (s.StartsWith("BUSY") || s.StartsWith("CANCEL") || s.StartsWith("TIMEOUT") || s.StartsWith("DRY")) return Amber;
            if (s.StartsWith("THIS")) return Blue;
            return Gray;
        }

        // buttons (design px, before scaling): the text always fits (+ padding); bottom-bar buttons are all 32 px high (bottom edge kept)
        static void NormalizeButtons(WF.Control root)
        {
            foreach (WF.Control c in root.Controls)
            {
                var b = c as WF.Button;
                if (b != null && b.Visible && !string.IsNullOrEmpty(b.Text))
                {
                    int need = WF.TextRenderer.MeasureText(b.Text.Replace("&&", "&"), b.Font).Width + 28;
                    if (b.Width < need)
                    {
                        int d = need - b.Width;
                        if ((b.Anchor & WF.AnchorStyles.Right) != 0 && (b.Anchor & WF.AnchorStyles.Left) == 0) b.Left -= d;
                        b.Width = need;
                    }
                    if ((b.Anchor & WF.AnchorStyles.Bottom) != 0 && (b.Anchor & WF.AnchorStyles.Top) == 0 && b.Height != 38) { b.Top -= 38 - b.Height; b.Height = 38; }   // Homie: 44 px buttons after scaling
                }
                if (c.HasChildren) NormalizeButtons(c);
            }
        }

        // rounded box (8 px, user design 22 Oct 2026): the corners are cut with a Region (parent colour shows) and the border is painted
        public static void RoundBox(WF.Control c, SD.Color border)
        {
            Func<SD.Rectangle, int, System.Drawing.Drawing2D.GraphicsPath> path = (r, rad) =>
            {
                var gp = new System.Drawing.Drawing2D.GraphicsPath(); int d = rad * 2;
                gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90); gp.CloseFigure(); return gp;
            };
            Action shape = () => { if (c.Width > 16 && c.Height > 16) c.Region = new SD.Region(path(new SD.Rectangle(0, 0, c.Width, c.Height), 10)); };
            c.SizeChanged += delegate { shape(); c.Invalidate(); };
            c.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var p = new SD.Pen(border)) using (var gp = path(new SD.Rectangle(0, 0, c.Width - 1, c.Height - 1), 10)) e.Graphics.DrawPath(p, gp);
            };
            shape();
        }

        // informative note under the title: for neutral facts (plain muted text); the yellow Banner is kept for prototypes / cautions
        public static WF.Label InfoBanner(string text, int width)
        {
            var l = new WF.Label
            {
                Text = text, Location = new SD.Point(12, 8), Size = new SD.Size(width, 28), TextAlign = SD.ContentAlignment.MiddleLeft,
                Font = new SD.Font("Segoe UI", 9f),   // design size, scaled by Apply (same as Banner)
                Anchor = WF.AnchorStyles.Top | WF.AnchorStyles.Left | WF.AnchorStyles.Right,
                ForeColor = Text2, AutoEllipsis = true
            };
            return l;
        }

        // section without a box (replaces GroupBox): small grey title + its controls; children keep their GroupBox coordinates (y >= 20)
        public static WF.Panel Section(string title, SD.Point location, SD.Size size, WF.AnchorStyles anchor)
        {
            var p = new WF.Panel { Location = location, Size = size, Anchor = anchor, BackColor = Surface };
            RoundBox(p, Border);
            p.Controls.Add(new WF.Label { Text = title.ToUpperInvariant(), Location = new SD.Point(10, 5), AutoSize = true, ForeColor = Text2, Font = new SD.Font("Segoe UI Semibold", 8.25f) });
            return p;
        }

        public static void Apply(WF.Form f, WF.Button primary)
        {
            // the form font is NOT changed: WinForms legacy auto-scale would move/resize every control and push the bottom ones out
            f.BackColor = Page; f.ForeColor = Text;
            WhiteTitleBar(f);
            Header(f);
            NormalizeButtons(f);
            ScaleUp(f);                                  // 12 px design -> 14 px Fluent body, layout scaled to match
            StyleTree(f.Controls);
            if (primary != null) Primary(primary);
            FitClient(f);
        }

        // "Stop" is shown as a Danger button, "Start" as the Primary one
        public static void SetPrimaryKind(WF.Button b, bool danger) { if (danger) Danger(b); else Primary(b); }

        // White title bar whatever the Windows accent colour ("Show accent colour on title bars" painted it pink).
        // DWMWA_CAPTION_COLOR (35) exists on Windows 11 only; on Windows 10 the call just fails and the system colour stays.
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        static void WhiteTitleBar(WF.Form f)
        {
            EventHandler set = (s, e) => { try { int white = 0x00FFFFFF; DwmSetWindowAttribute(f.Handle, 35, ref white, 4); } catch { } };
            if (f.IsHandleCreated) set(f, EventArgs.Empty);
            f.HandleCreated += set;   // also when WinForms recreates the handle
        }
        // Window header of the design system: the brand goes in the title bar, the tool name and its context on top of the window.
        // Everything that is not docked moves down by the header height, before the layout is scaled.
        public const int HeaderH = 46;
        static void Header(WF.Form f)
        {
            string raw = f.Text ?? "", name = raw, ctx = "";
            int cut = raw.IndexOf(" \u2013 "); if (cut > 0 && raw.Length > cut + 3) { name = raw.Substring(0, cut); ctx = raw.Substring(cut + 3); ctx = char.ToUpperInvariant(ctx[0]) + ctx.Substring(1); }
            var client = f.ClientSize; var min = f.MinimumSize;
            var anchors = new List<KeyValuePair<WF.Control, WF.AnchorStyles>>();
            foreach (WF.Control c in f.Controls)
                if (c.Dock == WF.DockStyle.None) { anchors.Add(new KeyValuePair<WF.Control, WF.AnchorStyles>(c, c.Anchor)); c.Anchor = WF.AnchorStyles.Top | WF.AnchorStyles.Left; c.Top += HeaderH; }
            f.ClientSize = new SD.Size(client.Width, client.Height + HeaderH);
            foreach (var kv in anchors) kv.Key.Anchor = kv.Value;
            if (!min.IsEmpty) f.MinimumSize = new SD.Size(min.Width, min.Height + HeaderH);
            var head = new WF.Panel { Dock = WF.DockStyle.Top, Height = HeaderH, BackColor = Page };
            var title = new WF.Label { Text = name, AutoSize = true, Location = new SD.Point(12, ctx == "" ? 12 : 5), ForeColor = Text, Font = new SD.Font("Segoe UI", 12.75f, SD.FontStyle.Bold) };
            head.Controls.Add(title);
            if (ctx != "") head.Controls.Add(new WF.Label { Text = ctx, AutoSize = true, Location = new SD.Point(13, 29), ForeColor = Text2, Font = new SD.Font("Segoe UI", 8.25f) });
            f.Controls.Add(head); head.SendToBack();
            f.Text = "Claude \u00B7 " + name + (ctx == "" ? "" : " (" + ctx + ")");
        }

        // ---------- readability: forms are laid out for Segoe UI 9 pt (12 px); shown at 10.5 pt (14 px, Fluent body) ----------
        public const float DesignPx = 12f, BodyPx = 14f;
        static readonly System.Reflection.MethodInfo shouldSerializeFont =
            typeof(WF.Control).GetMethod("ShouldSerializeFont", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // font of a design size scaled to a whole number of pixels (fractional sizes render jagged)
        static SD.Font ScaledFont(SD.Font f)
        {
            float px = (float)Math.Round(f.SizeInPoints / 0.75f * BodyPx / DesignPx);
            return new SD.Font(MapFamily(f.FontFamily.Name), px * 0.75f, f.Style);
        }

        static void ScaleUp(WF.Form f)
        {
            float dpi = 96f;
            try { using (var g = SD.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX; } catch { }
            float k = BodyPx / DesignPx * dpi / 96f;     // layout factor (fonts in points follow the DPI by themselves)
#pragma warning disable 618
            f.AutoScale = false;                          // no legacy auto-scale on Load (it moved controls out of view)
#pragma warning restore 618
            f.AutoScaleMode = WF.AutoScaleMode.None;
            // explicit fonts first (inherited ones follow the form font)
            Action<WF.Control> fonts = null;
            fonts = c =>
            {
                foreach (WF.Control k2 in c.Controls)
                {
                    if ((bool)shouldSerializeFont.Invoke(k2, null)) k2.Font = ScaledFont(k2.Font);
                    if (k2.HasChildren) fonts(k2);
                }
            };
            fonts(f);
            f.Font = ScaledFont(f.Font);
            var client = f.ClientSize; var min = f.MinimumSize;
            // freeze the form-level anchors so resizing the window does not move the already scaled controls twice
            var anchors = new List<KeyValuePair<WF.Control, WF.AnchorStyles>>();
            foreach (WF.Control c in f.Controls)
                if (c.Dock == WF.DockStyle.None) { anchors.Add(new KeyValuePair<WF.Control, WF.AnchorStyles>(c, c.Anchor)); c.Anchor = WF.AnchorStyles.Top | WF.AnchorStyles.Left; }
            f.Scale(new SD.SizeF(k, k));
            f.ClientSize = new SD.Size((int)Math.Round(client.Width * k), (int)Math.Round(client.Height * k));
            foreach (var kv in anchors) kv.Key.Anchor = kv.Value;
            if (!min.IsEmpty) f.MinimumSize = new SD.Size((int)Math.Round(min.Width * k), (int)Math.Round(min.Height * k));
            Action<WF.Control> cols = null;
            cols = c =>
            {
                foreach (WF.Control k2 in c.Controls)
                {
                    var lv = k2 as WF.ListView;
                    if (lv != null) foreach (WF.ColumnHeader h in lv.Columns) h.Width = (int)Math.Round(h.Width * k);
                    if (k2.HasChildren) cols(k2);
                }
            };
            cols(f);
            RowHeight = (int)Math.Round(36 * k);   // Homie rows: ~44 px
        }
        public static int RowHeight = 28;

        // grow the window if a control lies outside the client area (layouts were written for Size, not ClientSize)
        static void FitClient(WF.Form f)
        {
            int needW = f.ClientSize.Width, needH = f.ClientSize.Height;
            foreach (WF.Control c in f.Controls)
            {
                if (c.Dock != WF.DockStyle.None || !c.Visible && c.Width == 0) continue;
                needW = Math.Max(needW, c.Right + 12); needH = Math.Max(needH, c.Bottom + 12);
            }
            if (needW == f.ClientSize.Width && needH == f.ClientSize.Height) return;
            var anchors = new Dictionary<WF.Control, WF.AnchorStyles>();
            foreach (WF.Control c in f.Controls) if (c.Dock == WF.DockStyle.None) { anchors[c] = c.Anchor; c.Anchor = WF.AnchorStyles.Top | WF.AnchorStyles.Left; }
            f.ClientSize = new SD.Size(needW, needH);
            foreach (var kv in anchors) kv.Key.Anchor = kv.Value;   // anchors re-measured from the new size
            var wa = WF.Screen.FromPoint(WF.Cursor.Position).WorkingArea;
            if (f.Height > wa.Height) f.Height = wa.Height;
        }

        static void StyleTree(WF.Control.ControlCollection cs)
        {
            foreach (WF.Control c in cs)
            {
                // Revit turns on GDI+ "compatible" text rendering: force normal GDI/ClearType text (sharper)
                if (c is WF.ButtonBase) ((WF.ButtonBase)c).UseCompatibleTextRendering = false;
                if (c is WF.Label) ((WF.Label)c).UseCompatibleTextRendering = false;
                if (c is WF.Label && ((WF.Label)c).BorderStyle == WF.BorderStyle.FixedSingle) SoftBox((WF.Label)c);
                if (c is WF.Button) Secondary((WF.Button)c);
                else if (c is WF.TextBox) { ((WF.TextBox)c).BorderStyle = WF.BorderStyle.FixedSingle; c.BackColor = Surface; }
                else if (c is WF.ComboBox) ((WF.ComboBox)c).FlatStyle = WF.FlatStyle.System;   // native Windows 11 box: visible border + chevron
                else if (c is WF.CheckBox) ((WF.CheckBox)c).FlatStyle = WF.FlatStyle.System;
                else if (c is WF.RadioButton) ((WF.RadioButton)c).FlatStyle = WF.FlatStyle.System;
                else if (c is WF.ListView) Table((WF.ListView)c);
                else if (c is WF.NumericUpDown) c.BackColor = Hover;
                else if (c is WF.TabControl) { foreach (WF.TabPage p in ((WF.TabControl)c).TabPages) { p.BackColor = Page; p.UseVisualStyleBackColor = false; } }
                else if (c is WF.CheckedListBox || c is WF.ListBox) { c.BackColor = Surface; ((WF.ListControl)c).Font = Body; }
                if (c.HasChildren) StyleTree(c.Controls);
            }
        }
        // buttons of the design system (22 Oct 2026): rounded (8 px), Secondary = Outline (primary border + text), Primary = Filled,
        // Danger = Filled red, disabled = Grey-200 fill + Grey-400 text. WinForms flat buttons cannot round their corners, so the whole
        // button is painted again on top of the flat one (Paint), with hover / pressed states.
        enum BtnKind { Secondary, Primary, Danger, Dropdown }
        class BtnState { public BtnKind Kind; public bool Hover, Down; }
        static readonly WeakMap<WF.Button, BtnState> buttons = new WeakMap<WF.Button, BtnState>();

        static void RoundButton(WF.Button b, BtnKind kind)
        {
            b.FlatStyle = WF.FlatStyle.Flat; b.UseVisualStyleBackColor = false; b.FlatAppearance.BorderSize = 0; b.Cursor = WF.Cursors.Hand;
            b.Font = kind == BtnKind.Dropdown ? Body : Strong;
            BtnState st;
            if (!buttons.TryGetValue(b, out st))
            {
                st = new BtnState(); buttons[b] = st;
                b.MouseEnter += delegate { st.Hover = true; b.Invalidate(); };
                b.MouseLeave += delegate { st.Hover = false; st.Down = false; b.Invalidate(); };
                b.MouseDown += (s, e) => { if (e.Button == WF.MouseButtons.Left) { st.Down = true; b.Invalidate(); } };
                b.MouseUp += delegate { st.Down = false; b.Invalidate(); };
                b.EnabledChanged += delegate { b.Invalidate(); };
                b.Paint += (s, e) => PaintButton(b, st, e.Graphics);
            }
            st.Kind = kind;
            // flat colours underneath (seen only for an instant before the custom paint)
            b.BackColor = kind == BtnKind.Secondary || kind == BtnKind.Dropdown ? Surface : kind == BtnKind.Primary ? Brand : DangerColor;
            b.ForeColor = kind == BtnKind.Secondary || kind == BtnKind.Dropdown ? Text : SD.Color.White;
            b.FlatAppearance.MouseOverBackColor = b.BackColor; b.FlatAppearance.MouseDownBackColor = b.BackColor;
            b.Invalidate();
        }

        static SD.Color ParentBack(WF.Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent) if (p.BackColor.A == 255) return p.BackColor;
            return Page;
        }

        static void PaintButton(WF.Button b, BtnState st, SD.Graphics g)
        {
            bool en = b.Enabled;
            SD.Color fill, border, fore;
            if (!en) { fill = Disabled; border = Disabled; fore = DisabledText; }
            else if (st.Kind == BtnKind.Primary) { fill = st.Down ? BrandPress : st.Hover ? BrandHover : Brand; border = fill; fore = SD.Color.White; }
            else if (st.Kind == BtnKind.Danger) { fill = st.Down || st.Hover ? DangerHover : DangerColor; border = fill; fore = SD.Color.White; }
            else if (st.Kind == BtnKind.Dropdown) { fill = Page; border = st.Hover || st.Down ? Brand : BorderStrong; fore = Text; }
            else { fill = st.Down ? Border : st.Hover ? Hover : Card; border = fill; fore = Text; }   // Homie secondary: filled, no border
            g.Clear(ParentBack(b));
            var old = g.SmoothingMode; g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundRect(new SD.RectangleF(0.5f, 0.5f, b.Width - 1f, b.Height - 1f), 12))
            {
                using (var br = new SD.SolidBrush(fill)) g.FillPath(br, path);
                using (var p = new SD.Pen(border, 1f)) g.DrawPath(p, path);
            }
            if (en && b.Focused)            // keyboard focus: soft primary ring inside the border
                using (var path = RoundRect(new SD.RectangleF(2f, 2f, b.Width - 4f, b.Height - 4f), 10))
                using (var p = new SD.Pen(st.Kind == BtnKind.Primary || st.Kind == BtnKind.Danger ? SD.Color.FromArgb(170, 255, 255, 255) : Brand, 1.5f)) g.DrawPath(p, path);
            if (st.Kind == BtnKind.Dropdown)
            {
                // text on the left (without the "▾" of the caption) + chevron on the right
                int cx = b.Width - 16, cy = b.Height / 2;
                using (var p = new SD.Pen(en ? Text2 : DisabledText, 1.5f) { StartCap = SD.Drawing2D.LineCap.Round, EndCap = SD.Drawing2D.LineCap.Round })
                    g.DrawLines(p, new[] { new SD.PointF(cx - 4, cy - 2), new SD.PointF(cx, cy + 2), new SD.PointF(cx + 4, cy - 2) });
                g.SmoothingMode = old;
                WF.TextRenderer.DrawText(g, b.Text.TrimEnd(' ', '▾'), b.Font, new SD.Rectangle(10, 0, b.Width - 34, b.Height), fore,
                    WF.TextFormatFlags.Left | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis | WF.TextFormatFlags.NoPrefix);
                return;
            }
            g.SmoothingMode = old;
            WF.TextRenderer.DrawText(g, b.Text, b.Font, new SD.Rectangle(4, 0, b.Width - 8, b.Height), fore,
                WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.EndEllipsis | WF.TextFormatFlags.HidePrefix);
        }

        // rounded rectangle path (anti-aliased drawing)
        public static SD.Drawing2D.GraphicsPath RoundRect(SD.RectangleF r, float rad)
        {
            var gp = new SD.Drawing2D.GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            gp.AddArc(r.X, r.Y, d, d, 180, 90); gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90); gp.CloseFigure();
            return gp;
        }

        public static void Secondary(WF.Button b) { RoundButton(b, BtnKind.Secondary); }
        public static void Primary(WF.Button b) { RoundButton(b, BtnKind.Primary); }
        public static void Danger(WF.Button b) { RoundButton(b, BtnKind.Danger); }
        // ---------- tables: owner-drawn ListView (taller rows, no grid, soft header, pills in the status column) ----------
        static readonly WeakMap<WF.ListView, int> pillColumn = new WeakMap<WF.ListView, int>();

        // tables have no native black border: the parent paints a soft rounded frame around them
        static readonly WeakMap<WF.ListView, object> framed = new WeakMap<WF.ListView, object>();
        static void TableFrame(WF.ListView lv)
        {
            lv.BorderStyle = WF.BorderStyle.None;
            var host = lv.Parent; object seen;
            if (host == null || lv.Dock != WF.DockStyle.None || framed.TryGetValue(lv, out seen)) return;
            framed[lv] = lv;
            EventHandler redraw = delegate { host.Invalidate(); };
            lv.LocationChanged += redraw; lv.SizeChanged += redraw; lv.VisibleChanged += redraw;
            host.Paint += (s, e) =>
            {
                if (!lv.Visible) return;
                var g = e.Graphics; var old = g.SmoothingMode; g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                using (var gp = RoundRect(new SD.RectangleF(lv.Left - 8, lv.Top - 6, lv.Width + 16, lv.Height + 12), 12)) using (var b = new SD.SolidBrush(Card)) g.FillPath(b, gp);   // Homie: grey card container, no border
                g.SmoothingMode = old;
            };
        }
        public static void Table(WF.ListView lv)
        {
            TableFrame(lv); lv.GridLines = false; lv.BackColor = Card; lv.ForeColor = Text; lv.Font = Body; lv.FullRowSelect = true;
            if (lv.SmallImageList == null) lv.SmallImageList = new WF.ImageList { ImageSize = new SD.Size(1, RowHeight) };   // row height
            Proportional(lv);
            CellTips(lv);
            if (lv.CheckBoxes || lv.OwnerDraw) return;                                                              // keep native drawing for checkbox lists
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += delegate(object s, WF.DrawListViewColumnHeaderEventArgs e)
            {
                try { DrawHeader(e); } catch (Exception ex) { PaintFailed(lv, ex); e.DrawDefault = true; }
            };
            lv.DrawItem += delegate(object s, WF.DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            lv.DrawSubItem += delegate(object s, WF.DrawListViewSubItemEventArgs e)
            {
                // an error while drawing one cell must not leave the whole table blank (15 Oct 2026: Import simple showed an empty list)
                try { DrawCell(lv, e); } catch (Exception ex) { PaintFailed(lv, ex); e.DrawDefault = true; }
            };
        }

        static readonly HashSet<string> paintErrors = new HashSet<string>();
        static void PaintFailed(WF.ListView lv, Exception ex)
        {
            string key = (lv.Name ?? "") + lv.Columns.Count + ex.GetType().Name + ex.Message;
            lock (paintErrors) { if (!paintErrors.Add(key)) return; }
            Log.Write("Table paint error (" + lv.Columns.Count + " columns, " + lv.Items.Count + " rows): " + ex);
        }

        static void DrawHeader(WF.DrawListViewColumnHeaderEventArgs e)
        {
            {
                using (var b = new SD.SolidBrush(Card)) e.Graphics.FillRectangle(b, e.Bounds);      // Homie: header on the container, no fill, no line
                var r = new SD.Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
                WF.TextRenderer.DrawText(e.Graphics, e.Header.Text, Small, r, Text2, WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.Left | WF.TextFormatFlags.EndEllipsis);
            }
        }

        static void DrawCell(WF.ListView lv, WF.DrawListViewSubItemEventArgs e)
        {
            {
                // rows changed since the last fit -> fit the columns again (status pills, headers), once
                int last;
                if (!rowCounts.TryGetValue(lv, out last) || last != lv.Items.Count) { rowCounts[lv] = lv.Items.Count; lv.BeginInvoke(new Action(() => FitColumns(lv))); }
                var g = e.Graphics;
                var bg = e.Item.Selected ? Selected : Card;
                using (var b = new SD.SolidBrush(bg)) g.FillRectangle(b, e.Bounds);   // Homie: rows never separated by lines
                if (e.Item.Selected && e.ColumnIndex == 0) using (var b = new SD.SolidBrush(Brand)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 4, 4, e.Bounds.Height - 8);   // selected: 4 px primary bar
                int pc;
                string txt = e.SubItem.Text;
                Func<WF.DrawListViewSubItemEventArgs, bool> custom;
                if (CellPainter.TryGetValue(lv, out custom) && custom(e)) return;
                if (pillColumn.TryGetValue(lv, out pc) && e.ColumnIndex == pc && !string.IsNullOrEmpty(txt))
                    DrawPill(g, txt, PillFor(txt), new SD.Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height));
                else
                {
                    var r = new SD.Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
                    var fc = e.Item.ForeColor == SD.Color.Empty || e.Item.ForeColor == lv.ForeColor ? Text : e.Item.ForeColor;
                    WF.TextRenderer.DrawText(g, txt, Body, r, fc, WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.Left | WF.TextFormatFlags.EndEllipsis | WF.TextFormatFlags.NoPrefix);
                }
            }
        }

        // the last column takes the remaining width (long reasons/details are not cut while there is room)
        // columns follow the window: the designed widths are weights; the table width is shared in that proportion.
        // A column the user drags becomes the new weight (until the window is closed).
        static readonly WeakMap<WF.ListView, double[]> colWeights = new WeakMap<WF.ListView, double[]>();
        static readonly WeakMap<WF.ListView, int> rowCounts = new WeakMap<WF.ListView, int>();
        static bool sizing;
        static void Proportional(WF.ListView lv)
        {
            if (lv.Columns.Count == 0 || lv.View != WF.View.Details) return;
            colWeights[lv] = lv.Columns.Cast<WF.ColumnHeader>().Select(c => (double)Math.Max(20, c.Width)).ToArray();
            lv.ClientSizeChanged += delegate { FitColumns(lv); };
            lv.Resize += delegate { FitColumns(lv); };
            lv.HandleCreated += delegate { FitColumns(lv); };
            var form = lv.FindForm();
            if (form != null) { form.Shown += delegate { FitColumns(lv); }; form.ResizeEnd += delegate { FitColumns(lv); }; }
            lv.ColumnWidthChanged += (s, e) => { if (!sizing) colWeights[lv] = lv.Columns.Cast<WF.ColumnHeader>().Select(c => (double)Math.Max(20, c.Width)).ToArray(); };
            lv.Disposed += delegate { colWeights.Remove(lv); };
            FitColumns(lv);
        }

        // runs from resize / paint events: an exception here was an unhandled-exception dialog over the tool (Rebuild on P4, 9 Nov 2026,
        // NullReferenceException: null rows during WmCreate, see FitColumnsCore). Any other failure: the columns keep their widths and the
        // full trace (with line) goes to the log, once.
        static bool fitLogged;
        static void FitColumns(WF.ListView lv)
        {
            try { FitColumnsCore(lv); }
            catch (Exception ex)
            {
                if (fitLogged) return;
                fitLogged = true;
                string where = "?"; try { var f = lv.FindForm(); where = (f != null ? f.Text : "") + " / " + lv.Name + " (" + lv.Columns.Count + " columns, " + lv.Items.Count + " rows)"; } catch { }
                Log.Write("Column fit failed in " + where + ": " + ex);
            }
        }

        static void FitColumnsCore(WF.ListView lv)
        {
            double[] w;
            if (sizing || !colWeights.TryGetValue(lv, out w) || w.Length != lv.Columns.Count) return;
            bool scroll = IsVScrollVisible(lv) || (lv.Items.Count + 1) * RowHeight > lv.Height;   // room for the scrollbar only when the rows do not fit (else a white gap)
            int avail = lv.Width - 2 - (scroll ? WF.SystemInformation.VerticalScrollBarWidth : 0);
            if (avail < 120) return;
            double tot = w.Sum();
            // a header is never cut: minimum width = header text + padding; the rest is shared in proportion
            var min = lv.Columns.Cast<WF.ColumnHeader>().Select(c => c.Text == "" ? 24 : WF.TextRenderer.MeasureText(c.Text, Small).Width + 22).ToArray();
            // the status column fits its longest pill
            int pc;
            // (each distinct status measured once: a few values over thousands of rows; this runs on every resize)
            if (pillColumn.TryGetValue(lv, out pc) && pc < min.Length)
            {
                var texts = new HashSet<string>();
                // while Windows (re)creates the list (WmCreate) .NET Framework can hand back null rows of a filled list (Rebuild on P4,
                // 9 Nov 2026: NullReferenceException here): those rows are skipped, the next fit measures them
                foreach (WF.ListViewItem it in lv.Items)
                    if (it != null && pc < it.SubItems.Count && texts.Add(it.SubItems[pc].Text))
                        min[pc] = Math.Max(min[pc], WF.TextRenderer.MeasureText(PillLabel(it.SubItems[pc].Text), Small).Width + 34);
            }
            int extra = 0; double totFree = 0;
            for (int i = 0; i < w.Length; i++) { int pw = (int)Math.Floor(w[i] * avail / tot); if (pw < min[i]) extra += min[i] - pw; else totFree += w[i]; }
            sizing = true;
            try
            {
                for (int i = 0; i < w.Length; i++)
                {
                    int pw = (int)Math.Floor(w[i] * avail / tot);
                    int nw = pw < min[i] ? min[i] : Math.Max(min[i], pw - (totFree > 0 ? (int)Math.Ceiling(extra * w[i] / totFree) : 0));
                    if (lv.Columns[i].Width != nw) lv.Columns[i].Width = nw;
                }
            }
            finally { sizing = false; }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        static bool IsVScrollVisible(WF.Control c) { return c.IsHandleCreated && (GetWindowLong(c.Handle, -16) & 0x00200000) != 0; }
        // mark the column of a table that shows status pills
        public static void Pills(WF.ListView lv, int column) { pillColumn[lv] = column; lv.Invalidate(); }
        // fit the columns again right after the rows change (the paint-time refit comes late; the scrollbar room depends on the row count)
        public static void Refit(WF.ListView lv) { FitColumns(lv); }

        // per-table custom cell painter (after the row background is drawn); return true when the cell was handled
        public static readonly WeakMap<WF.ListView, Func<WF.DrawListViewSubItemEventArgs, bool>> CellPainter =
            new WeakMap<WF.ListView, Func<WF.DrawListViewSubItemEventArgs, bool>>();
        // pill label in sentence case (Fluent): "MISSING LENGTH" -> "Missing length"; OK / acronyms stay; the data (and its comparisons) keep the original text
        public static string PillLabel(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= 3) return s;                 // OK, NEW…
            var words = s.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                bool acronym = w.Length <= 3 && w.ToUpperInvariant() == w && w.Any(char.IsLetter) && i > 0;   // LF, KV…
                if (acronym) continue;
                w = w.ToLowerInvariant();
                words[i] = i == 0 && w.Length > 0 ? char.ToUpperInvariant(w[0]) + w.Substring(1) : w;
            }
            return string.Join(" ", words);
        }

        // text inside an empty table (e.g. "Press Scan to …"); drawn after the native paint while the table has no rows
        class EmptyPainter : WF.NativeWindow
        {
            readonly WF.ListView lv; public string Text;
            public EmptyPainter(WF.ListView l, string t) { lv = l; Text = t; if (l.IsHandleCreated) AssignHandle(l.Handle); l.HandleCreated += delegate { AssignHandle(l.Handle); }; l.HandleDestroyed += delegate { ReleaseHandle(); }; }
            protected override void WndProc(ref WF.Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F || lv.Items.Count > 0 || string.IsNullOrEmpty(Text)) return;   // WM_PAINT
                using (var g = lv.CreateGraphics())
                {
                    var r = new SD.Rectangle(0, 40, lv.ClientSize.Width, Math.Max(40, lv.ClientSize.Height - 60));
                    WF.TextRenderer.DrawText(g, Text, Body, r, Text2, WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.Top | WF.TextFormatFlags.WordBreak | WF.TextFormatFlags.NoPrefix);
                }
            }
        }
        static readonly WeakMap<WF.ListView, EmptyPainter> empties = new WeakMap<WF.ListView, EmptyPainter>();
        public static void EmptyText(WF.ListView lv, string text)
        {
            EmptyPainter ep;
            if (empties.TryGetValue(lv, out ep)) ep.Text = text; else { empties[lv] = new EmptyPainter(lv, text); lv.Disposed += delegate { empties.Remove(lv); }; }
            lv.Invalidate();
        }

        // boxed label (summaries) -> white card with a light border and inner padding instead of the dark 3D frame
        public static void SoftBox(WF.Label l)
        {
            l.BorderStyle = WF.BorderStyle.None; l.BackColor = Surface; l.ForeColor = Text; l.Padding = new WF.Padding(8, 6, 8, 6);
            l.Paint += delegate(object s, WF.PaintEventArgs e) { using (var p = new SD.Pen(Border)) e.Graphics.DrawRectangle(p, 0, 0, l.Width - 1, l.Height - 1); };
        }

        // hover a cut-off cell -> tooltip with the full text
        static void CellTips(WF.ListView lv)
        {
            var tip = new WF.ToolTip { InitialDelay = 400, ReshowDelay = 100, AutoPopDelay = 15000 };
            string shown = null;
            lv.MouseMove += (s, e) =>
            {
                var h = lv.HitTest(e.Location); string t = null;
                if (h.Item != null && h.SubItem != null)
                {
                    var txt = h.SubItem.Text;
                    if (!string.IsNullOrEmpty(txt) && WF.TextRenderer.MeasureText(txt, Body).Width > h.SubItem.Bounds.Width - 14) t = txt;
                }
                if (t == shown) return;
                shown = t;
                if (t == null) tip.Hide(lv); else tip.Show(t, lv, e.X + 12, e.Y + 18, 15000);
            };
            lv.MouseLeave += delegate { shown = null; tip.Hide(lv); };
        }

        public static void DrawPill(SD.Graphics g, string text, Pill p, SD.Rectangle cell)
        {
            text = PillLabel(text);
            var sz = WF.TextRenderer.MeasureText(g, text, Small, new SD.Size(int.MaxValue, int.MaxValue), WF.TextFormatFlags.NoPadding);
            int h = 22, w = Math.Min(cell.Width - 2, sz.Width + 20);
            var r = new SD.Rectangle(cell.X, cell.Y + (cell.Height - h) / 2, w, h);
            var old = g.SmoothingMode; g.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = RoundRect(new SD.RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), h / 2f))   // Homie chip: soft tint, fully rounded
            {
                using (var b = new SD.SolidBrush(p.Back)) g.FillPath(b, path);
                using (var pen = new SD.Pen(p.Border)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = old;
            WF.TextRenderer.DrawText(g, text, Small, r, p.Fore, WF.TextFormatFlags.VerticalCenter | WF.TextFormatFlags.HorizontalCenter | WF.TextFormatFlags.EndEllipsis | WF.TextFormatFlags.NoPrefix);
        }
    }
}
