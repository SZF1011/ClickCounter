using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

internal static class IconGen
{
    static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();
        foreach (int size in sizes) pngs.Add(Render(size));

        using (FileStream fs = new FileStream(output, FileMode.Create, FileAccess.Write))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int dim = sizes[i] >= 256 ? 0 : sizes[i];
                w.Write((byte)dim);
                w.Write((byte)dim);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)pngs[i].Length);
                w.Write((uint)offset);
                offset += pngs[i].Length;
            }
            foreach (byte[] png in pngs) w.Write(png);
        }
        Console.WriteLine("icon written: " + output);
    }

    static byte[] Render(int size)
    {
        using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float u = size / 256f;
                RectangleF bg = new RectangleF(6 * u, 6 * u, size - 12 * u, size - 12 * u);
                using (GraphicsPath path = Round(bg, 58 * u))
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(bg,
                        Color.FromArgb(46, 56, 78), Color.FromArgb(16, 20, 28), 90f))
                        g.FillPath(brush, path);
                    using (Pen pen = new Pen(Color.FromArgb(72, 86, 112), Math.Max(1f, 2f * u)))
                        g.DrawPath(pen, path);
                }

                RectangleF cap = new RectangleF(66 * u, 52 * u, 124 * u, 98 * u);
                using (GraphicsPath cp = Round(cap, 20 * u))
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(38, 46, 62)))
                        g.FillPath(brush, cp);
                    using (Pen pen = new Pen(Color.FromArgb(96, 112, 144), Math.Max(1f, 2.4f * u)))
                        g.DrawPath(pen, cp);
                }

                if (size >= 24)
                {
                    using (Font font = new Font("Segoe UI", 56f * u, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        using (SolidBrush brush = new SolidBrush(Color.FromArgb(236, 241, 250)))
                            g.DrawString("A", font, brush, new RectangleF(cap.X, cap.Y - 2 * u, cap.Width, cap.Height), sf);
                    }
                }

                Color[] colors = new Color[] { Color.FromArgb(112, 130, 255), Color.FromArgb(45, 212, 191), Color.FromArgb(245, 196, 81) };
                float[] heights = new float[] { 30f, 48f, 68f };
                float barW = 32f;
                float gap = 16f;
                float startX = 128f - (barW * 3f + gap * 2f) / 2f;
                float baseY = 212f;
                for (int i = 0; i < 3; i++)
                {
                    RectangleF bar = new RectangleF((startX + i * (barW + gap)) * u, (baseY - heights[i]) * u, barW * u, heights[i] * u);
                    using (GraphicsPath bp = Round(bar, 8 * u))
                    using (SolidBrush brush = new SolidBrush(colors[i]))
                        g.FillPath(brush, bp);
                }
            }

            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static GraphicsPath Round(RectangleF r, float radius)
    {
        GraphicsPath path = new GraphicsPath();
        float d = radius * 2f;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
