using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace KeyRank
{
    internal static class Theme
    {
        public const string UiFamily = "Microsoft YaHei UI";

        public static readonly Color Bg = Color.FromArgb(13, 15, 20);
        public static readonly Color BgAlt = Color.FromArgb(17, 20, 27);
        public static readonly Color Card = Color.FromArgb(23, 27, 35);
        public static readonly Color CardHover = Color.FromArgb(32, 37, 48);
        public static readonly Color Stroke = Color.FromArgb(38, 44, 58);
        public static readonly Color Text = Color.FromArgb(232, 236, 243);
        public static readonly Color Muted = Color.FromArgb(134, 144, 163);
        public static readonly Color Dim = Color.FromArgb(92, 101, 118);
        public static readonly Color Accent = Color.FromArgb(112, 130, 255);
        public static readonly Color AccentFill = Color.FromArgb(48, 56, 94);
        public static readonly Color Green = Color.FromArgb(74, 222, 128);
        public static readonly Color Gold = Color.FromArgb(245, 196, 81);
        public static readonly Color Silver = Color.FromArgb(185, 196, 212);
        public static readonly Color Bronze = Color.FromArgb(206, 138, 88);
        public static readonly Color Danger = Color.FromArgb(248, 113, 113);
        public static readonly Color Track = Color.FromArgb(29, 34, 44);

        public static readonly Font TitleFont;
        public static readonly Font H1Font;
        public static readonly Font BodyFont;
        public static readonly Font SmallFont;
        public static readonly Font BoldFont;
        public static readonly Font NumFont;
        public static readonly Font CountFont;
        public static readonly Font GlyphFont;

        static Theme()
        {
            TitleFont = MakeFont(UiFamily, 12f, FontStyle.Bold);
            H1Font = MakeFont(UiFamily, 9f, FontStyle.Bold);
            BodyFont = MakeFont(UiFamily, 7.5f, FontStyle.Regular);
            SmallFont = MakeFont(UiFamily, 6.5f, FontStyle.Regular);
            BoldFont = MakeFont(UiFamily, 7.5f, FontStyle.Bold);
            NumFont = MakeFont(UiFamily, 11f, FontStyle.Bold);
            CountFont = MakeFont(UiFamily, 7.5f, FontStyle.Bold);
            GlyphFont = MakeFont("Segoe MDL2 Assets", 8f, FontStyle.Regular);
        }

        public static Font MakeFont(string family, float size, FontStyle style)
        {
            try { return new Font(family, size, style, GraphicsUnit.Point); }
            catch { }
            try { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
            catch { return Control.DefaultFont; }
        }

        public static Color Alpha(Color c, int a)
        {
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            return RoundRect(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            if (d <= 0f)
            {
                p.AddRectangle(r);
                return p;
            }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = RoundRect(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color)
        {
            StrokeRound(g, r, radius, color, 1f);
        }

        public static void StrokeRound(Graphics g, Rectangle r, int radius, Color color, float width)
        {
            RectangleF rf = new RectangleF(r.X + width / 2f, r.Y + width / 2f, r.Width - width, r.Height - width);
            using (GraphicsPath p = RoundRect(rf, radius))
            using (Pen pen = new Pen(color, width))
                g.DrawPath(pen, p);
        }

        public static void DrawText(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags);
        }

        public const TextFormatFlags LeftCenter =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

        public const TextFormatFlags RightCenter =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine;

        public const TextFormatFlags CenterAll =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
            TextFormatFlags.SingleLine;
    }
}
