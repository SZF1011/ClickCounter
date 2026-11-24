using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyRank
{
    internal class PillButton : Control
    {
        public string Glyph = "";
        public bool Accent;
        bool hover;
        bool down;

        public PillButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.BodyFont;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Math.Max(6, Height / 2);
            Color fill = Accent ? Theme.AccentFill : Theme.Card;
            if (!Enabled) fill = Theme.Card;
            else if (hover) fill = Accent ? Color.FromArgb(58, 68, 112) : Theme.CardHover;
            if (Enabled && down) fill = Accent ? Color.FromArgb(52, 61, 100) : Color.FromArgb(37, 43, 56);

            Theme.FillRound(g, r, radius, fill);
            Theme.StrokeRound(g, r, radius, Theme.Stroke);

            Color fg = !Enabled ? Theme.Dim : ((hover || Accent) ? Theme.Text : Theme.Muted);
            Size glyphSize = string.IsNullOrEmpty(Glyph) ? Size.Empty : TextRenderer.MeasureText(Glyph, Theme.GlyphFont);
            bool hasText = !string.IsNullOrEmpty(Text);
            Size textSize = hasText ? TextRenderer.MeasureText(Text, Font) : Size.Empty;
            int gap = (glyphSize.Width > 0 && hasText) ? 6 : 0;
            int totalW = glyphSize.Width + gap + textSize.Width;
            int x = (Width - totalW) / 2;

            if (glyphSize.Width > 0)
            {
                Rectangle gr = new Rectangle(x, (Height - glyphSize.Height) / 2, glyphSize.Width, glyphSize.Height);
                TextRenderer.DrawText(g, Glyph, Theme.GlyphFont, gr, fg, Theme.CenterAll);
                x += glyphSize.Width + gap;
            }
            if (hasText)
            {
                Rectangle tr = new Rectangle(x, 0, textSize.Width + 2, Height);
                TextRenderer.DrawText(g, Text, Font, tr, fg, Theme.LeftCenter);
            }
        }
    }

    internal class Segmented : Control
    {
        public string[] Items = new string[] { "全部", "键盘", "鼠标" };
        int selected;
        int hover = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.BoldFont;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0) value = 0;
                if (value >= Items.Length) value = Items.Length - 1;
                if (selected != value)
                {
                    selected = value;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }
        }

        int HitTest(int x)
        {
            if (Items.Length == 0) return -1;
            int left = 4;
            int inner = Width - 8;
            if (x < left || x >= left + inner) return -1;
            int seg = inner / Items.Length;
            int idx = (x - left) / Math.Max(1, seg);
            if (idx < 0) idx = 0;
            if (idx >= Items.Length) idx = Items.Length - 1;
            return idx;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = HitTest(e.X);
            if (idx != hover) { hover = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = HitTest(e.X);
            if (idx >= 0) SelectedIndex = idx;
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, track, Height / 2, Color.FromArgb(19, 22, 30));
            Theme.StrokeRound(g, track, Height / 2, Theme.Stroke);

            int inner = Width - 8;
            int seg = inner / Items.Length;
            for (int i = 0; i < Items.Length; i++)
            {
                int x = 4 + i * seg;
                int w = (i == Items.Length - 1) ? (4 + inner - x) : seg;
                Rectangle cell = new Rectangle(x, 4, w, Height - 8);
                if (i == selected)
                {
                    Theme.FillRound(g, cell, (Height - 8) / 2, Theme.AccentFill);
                    TextRenderer.DrawText(g, Items[i], Font, cell, Theme.Text, Theme.CenterAll);
                }
                else
                {
                    if (i == hover) Theme.FillRound(g, cell, (Height - 8) / 2, Theme.CardHover);
                    TextRenderer.DrawText(g, Items[i], Font, cell, Theme.Muted, Theme.CenterAll);
                }
            }
        }
    }

    internal class StatusPill : Control
    {
        public bool Paused;

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.SmallFont;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Height / 2, Color.FromArgb(24, 29, 39));
            Theme.StrokeRound(g, r, Height / 2, Theme.Stroke);

            Color dot = Paused ? Theme.Dim : Theme.Green;
            using (SolidBrush glow = new SolidBrush(Theme.Alpha(dot, 55)))
                g.FillEllipse(glow, 12, Height / 2 - 5, 10, 10);
            using (SolidBrush core = new SolidBrush(dot))
                g.FillEllipse(core, 15, Height / 2 - 2, 4, 4);

            string text = Paused ? "已暂停" : "记录中";
            TextRenderer.DrawText(g, text, Font, new Rectangle(26, 0, Width - 30, Height),
                Paused ? Theme.Muted : Theme.Text, Theme.LeftCenter);
        }
    }

    internal class StatsCard : Control
    {
        public long Total;
        public long Today;
        public double MousePercent;

        public StatsCard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 16, Theme.Card);
            Theme.StrokeRound(g, r, 16, Theme.Stroke);

            string[] labels = new string[] { "总点击", "今日", "鼠标占比" };
            string[] values = new string[]
            {
                Total.ToString("N0"),
                Today.ToString("N0"),
                MousePercent.ToString("0.0") + "%"
            };

            int colW = Width / 3;
            for (int i = 0; i < 3; i++)
            {
                Rectangle cell = new Rectangle(i * colW, 0, colW, Height);
                int labelH = (int)(Height * 0.2);
                int valueH = (int)(Height * 0.44);
                int labelY = (int)(Height * 0.18);
                TextRenderer.DrawText(g, labels[i], Theme.SmallFont,
                    new Rectangle(cell.X, labelY, cell.Width, labelH), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, values[i], Theme.NumFont,
                    new Rectangle(cell.X - 2, labelY + labelH, cell.Width + 4, valueH), Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                if (i > 0)
                {
                    int x = i * colW;
                    using (Pen p = new Pen(Theme.Stroke, 1f))
                        g.DrawLine(p, x, Height * 0.22f, x, Height * 0.78f);
                }
            }
        }
    }

    internal class HintTextBox : TextBox
    {
        public string Hint;

        public HintTextBox()
        {
            BorderStyle = BorderStyle.None;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && Text.Length == 0 && !string.IsNullOrEmpty(Hint) && !Focused)
            {
                using (Graphics g = CreateGraphics())
                {
                    TextRenderer.DrawText(g, Hint, Font, ClientRectangle, Theme.Dim,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    internal class TextInput : Control
    {
        readonly HintTextBox box;

        public event EventHandler ValueChanged;

        public string Value
        {
            get { return box.Text; }
            set { box.Text = value; }
        }

        public string Hint
        {
            get { return box.Hint; }
            set { box.Hint = value; }
        }

        public TextBox Inner
        {
            get { return box; }
        }

        public TextInput()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;

            box = new HintTextBox();
            box.Hint = "";
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            box.Font = Theme.BodyFont;
            box.TextChanged += delegate
            {
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                Invalidate();
            };
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Controls.Add(box);
        }

        public void FocusInput()
        {
            box.Focus();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (box != null)
            {
                int h = box.PreferredHeight;
                box.SetBounds(14, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - 28), h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, 10, Theme.Card);
            Theme.StrokeRound(g, r, 10, box.Focused ? Theme.Accent : Theme.Stroke);
        }
    }

    internal class SearchBox : Control
    {
        readonly HintTextBox box;

        public event EventHandler QueryChanged;

        public string Query
        {
            get { return box.Text.Trim(); }
        }

        public void FocusInput()
        {
            box.Focus();
        }

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;

            box = new HintTextBox();
            box.Hint = "搜索按键";
            box.BackColor = Theme.Card;
            box.ForeColor = Theme.Text;
            box.Font = Theme.BodyFont;
            box.TextChanged += delegate { if (QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Controls.Add(box);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (box != null)
            {
                int h = box.PreferredHeight;
                box.SetBounds(34, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - 44), h);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Math.Max(6, Height / 2), Theme.Card);
            Theme.StrokeRound(g, r, Math.Max(6, Height / 2), box.Focused ? Theme.Accent : Theme.Stroke);

            Rectangle glyphRect = new Rectangle(11, 0, 18, Height);
            TextRenderer.DrawText(g, "\uE721", Theme.GlyphFont, glyphRect, Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(26, 31, 40));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Stroke))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (GraphicsPath p = Theme.RoundRect(r, 6))
            using (SolidBrush b = new SolidBrush(Theme.CardHover))
                e.Graphics.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Dim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle r = e.Item.ContentRectangle;
            using (Pen p = new Pen(Theme.Stroke))
                e.Graphics.DrawLine(p, r.Left + 10, r.Top + r.Height / 2, r.Right - 10, r.Top + r.Height / 2);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            TextRenderer.DrawText(e.Graphics, "\uE73E", Theme.GlyphFont, r, Theme.Green,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.CardHover; } }
        public override Color MenuItemBorder { get { return Theme.CardHover; } }
        public override Color MenuBorder { get { return Theme.Stroke; } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(26, 31, 40); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(26, 31, 40); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(26, 31, 40); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(26, 31, 40); } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.CardHover; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.CardHover; } }
    }

    internal sealed class WheelForwarder : IMessageFilter
    {
        readonly RankList list;
        readonly Form owner;

        public WheelForwarder(RankList target, Form form)
        {
            list = target;
            owner = form;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || list == null || !list.Visible || owner == null) return false;
            if (Form.ActiveForm != owner) return false;
            Point p = Cursor.Position;
            if (!owner.Bounds.Contains(p)) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            list.ScrollBy(-delta * 3 / 4);
            return true;
        }
    }
}
