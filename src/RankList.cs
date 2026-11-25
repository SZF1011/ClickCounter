using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyRank
{
    internal sealed class RankList : Control
    {
        List<KeyStat> rows = new List<KeyStat>();
        long maxCount = 1;
        long totalClicks;
        int offset;
        int hoverIndex = -1;
        bool draggingThumb;
        int dragGrab;
        float scale = 1f;
        string emptyTitle = "还没有记录";
        string emptyHint = "敲键盘、点鼠标，这里会实时排名";

        public int RowHeight = 38;

        public RankList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            TabStop = false;
        }

        public void SetScale(float s)
        {
            scale = s <= 0f ? 1f : s;
            RowHeight = S(34);
            offset = ClampOffset(offset);
            Invalidate();
        }

        int S(int v)
        {
            return (int)Math.Round(v * scale);
        }

        int ContentHeight { get { return rows.Count * RowHeight; } }
        int MaxOffset { get { return Math.Max(0, ContentHeight - Height); } }

        int ClampOffset(int v)
        {
            if (v < 0) return 0;
            if (v > MaxOffset) return MaxOffset;
            return v;
        }

        public void SetItems(List<KeyStat> items, long total)
        {
            rows = items ?? new List<KeyStat>();
            totalClicks = total;
            maxCount = 1;
            foreach (KeyStat s in rows)
                if (s.Count > maxCount) maxCount = s.Count;
            offset = ClampOffset(offset);
            Invalidate();
        }

        public void ScrollBy(int dy)
        {
            int next = ClampOffset(offset + dy);
            if (next != offset)
            {
                offset = next;
                Invalidate();
            }
        }

        public void ScrollToTop()
        {
            if (offset != 0) { offset = 0; Invalidate(); }
        }

        public void ScrollToBottom()
        {
            int bottom = MaxOffset;
            if (offset != bottom) { offset = bottom; Invalidate(); }
        }

        public void ScrollPage(bool down)
        {
            int step = Math.Max(RowHeight, Height - RowHeight);
            ScrollBy(down ? step : -step);
        }

        int ThumbHeight
        {
            get
            {
                if (ContentHeight <= 0) return S(26);
                int h = (int)((double)Height * Height / ContentHeight);
                return Math.Max(S(26), Math.Min(Height, h));
            }
        }

        Rectangle ThumbRect
        {
            get
            {
                int thumbH = ThumbHeight;
                int travel = Height - S(8) - thumbH;
                int y = S(4) + (travel <= 0 || MaxOffset <= 0 ? 0 : (int)((double)travel * offset / MaxOffset));
                return new Rectangle(Width - S(5), y, S(3), thumbH);
            }
        }

        public void SetEmptyMessage(string title, string hint)
        {
            emptyTitle = title;
            emptyHint = hint;
        }

        protected override void OnResize(EventArgs e)
        {
            offset = ClampOffset(offset);
            base.OnResize(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingThumb)
            {
                int thumbH = ThumbHeight;
                int travel = Height - S(8) - thumbH;
                if (travel > 0 && MaxOffset > 0)
                {
                    int top = e.Y - dragGrab - S(4);
                    if (top < 0) top = 0;
                    if (top > travel) top = travel;
                    offset = (int)Math.Round((double)top * MaxOffset / travel);
                    Invalidate();
                }
                return;
            }

            int idx = HitTest(e.Y);
            if (idx != hoverIndex)
            {
                hoverIndex = idx;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (draggingThumb)
            {
                draggingThumb = false;
                Capture = false;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (MaxOffset > 0)
            {
                ScrollBy(-e.Delta * 3 / 4);
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { ScrollBy(S(28)); e.Handled = true; return; }
            if (e.KeyCode == Keys.Up) { ScrollBy(-S(28)); e.Handled = true; return; }
            if (e.KeyCode == Keys.PageDown) { ScrollPage(true); e.Handled = true; return; }
            if (e.KeyCode == Keys.PageUp) { ScrollPage(false); e.Handled = true; return; }
            if (e.KeyCode == Keys.Home) { ScrollToTop(); e.Handled = true; return; }
            if (e.KeyCode == Keys.End) { ScrollToBottom(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && MaxOffset > 0 && e.X >= Width - S(14))
            {
                Rectangle thumb = ThumbRect;
                Rectangle hit = new Rectangle(thumb.X - S(3), thumb.Y, thumb.Width + S(6), thumb.Height);
                if (hit.Contains(e.Location))
                {
                    draggingThumb = true;
                    dragGrab = e.Y - thumb.Y;
                    Capture = true;
                    return;
                }
                ScrollPage(e.Y >= thumb.Bottom);
                return;
            }

            Focus();
            base.OnMouseDown(e);
        }

        int HitTest(int y)
        {
            int idx = (offset + y) / RowHeight;
            if (idx < 0 || idx >= rows.Count) return -1;
            return idx;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);

            if (rows.Count == 0)
            {
                DrawEmpty(g);
                return;
            }

            int first = offset / RowHeight;
            int y = -(offset % RowHeight);
            int last = Math.Min(rows.Count, first + Height / Math.Max(1, RowHeight) + 2);
            for (int i = first; i < last; i++)
            {
                DrawRow(g, new Rectangle(0, y, Width, RowHeight), rows[i], i);
                y += RowHeight;
            }
            DrawScroll(g);
        }

        void DrawEmpty(Graphics g)
        {
            Size ts = TextRenderer.MeasureText(emptyTitle, Theme.H1Font);
            Size hs = TextRenderer.MeasureText(emptyHint, Theme.SmallFont);
            int cy = Height / 2 - S(40);
            TextRenderer.DrawText(g, emptyTitle, Theme.H1Font, new Rectangle(0, cy, Width, ts.Height), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, emptyHint, Theme.SmallFont,
                new Rectangle(0, cy + ts.Height + S(10), Width, hs.Height), Theme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }

        void DrawRow(Graphics g, Rectangle row, KeyStat stat, int index)
        {
            if (index == hoverIndex)
            {
                Rectangle hr = new Rectangle(S(2), row.Y + S(2), row.Width - S(4), row.Height - S(4));
                Theme.FillRound(g, hr, S(10), Theme.BgAlt);
            }

            int d = S(20);
            Rectangle badge = new Rectangle(S(4), row.Y + (row.Height - d) / 2, d, d);
            Color rc = index == 0 ? Theme.Gold : index == 1 ? Theme.Silver : index == 2 ? Theme.Bronze : Theme.Muted;
            Color rf = index <= 2 ? Theme.Alpha(rc, 34) : Color.FromArgb(27, 32, 42);
            using (SolidBrush b = new SolidBrush(rf)) g.FillEllipse(b, badge);
            TextRenderer.DrawText(g, (index + 1).ToString(), index <= 2 ? Theme.BoldFont : Theme.SmallFont,
                badge, rc, Theme.CenterAll);

            Size labelSize = TextRenderer.MeasureText(stat.Label, Theme.BoldFont);
            int capColumn = S(84);
            int capW = Math.Min(S(78), Math.Max(S(30), labelSize.Width + S(14)));
            int capH = S(24);
            int capX = S(28) + (capColumn - capW) / 2;
            Rectangle cap = new Rectangle(capX, row.Y + (row.Height - capH) / 2, capW, capH);
            Theme.FillRound(g, cap, S(8), Color.FromArgb(27, 32, 42));
            Theme.StrokeRound(g, cap, S(8), Theme.Stroke);
            TextRenderer.DrawText(g, stat.Label, Theme.BoldFont, cap, Theme.Text, Theme.CenterAll);

            int countW = S(66);
            int rightX = row.Width - countW - S(6);
            Rectangle countRect = new Rectangle(rightX, row.Y + row.Height / 2 - S(14), countW, S(14));
            TextRenderer.DrawText(g, stat.Count.ToString("N0"), Theme.CountFont, countRect, Theme.Text, Theme.RightCenter);

            double pct = totalClicks > 0 ? stat.Count * 100.0 / totalClicks : 0.0;
            Rectangle pctRect = new Rectangle(rightX, row.Y + row.Height / 2 + S(1), countW, S(13));
            TextRenderer.DrawText(g, pct.ToString("0.0") + "%", Theme.SmallFont, pctRect, Theme.Dim, Theme.RightCenter);

            int barX = S(28) + capColumn + S(8);
            int barRight = row.Width - countW - S(12);
            if (barRight - barX > S(20))
            {
                int barH = S(5);
                int barY = row.Y + (row.Height - barH) / 2;
                Rectangle track = new Rectangle(barX, barY, barRight - barX, barH);
                Theme.FillRound(g, track, barH / 2, Theme.Track);

                double ratio = (double)stat.Count / maxCount;
                int fillW = (int)Math.Round(track.Width * ratio);
                if (fillW < barH) fillW = barH;
                if (fillW > track.Width) fillW = track.Width;
                Rectangle fill = new Rectangle(track.X, track.Y, fillW, track.Height);
                Color barColor = stat.Kind == HitKind.Mouse ? Color.FromArgb(45, 212, 191) : Theme.Accent;
                Theme.FillRound(g, fill, barH / 2, barColor);
            }
        }

        void DrawScroll(Graphics g)
        {
            if (MaxOffset <= 0) return;
            Rectangle thumb = ThumbRect;
            Color color = draggingThumb ? Color.FromArgb(120, 134, 162) : Color.FromArgb(66, 76, 96);
            Theme.FillRound(g, thumb, S(2), color);
        }
    }
}
