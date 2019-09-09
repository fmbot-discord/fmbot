using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace FMBot.Bot.Extensions
{
    public static class BitmapExtensions
    {
        public static byte MostDifferent(byte original)
        {
            if (original < 0x80)
            {
                return 0xff;
            }
            else
            {
                return 0x00;
            }
        }

        public static Color MostDifferent(Color original)
        {
            byte r = MostDifferent(original.R);
            byte g = MostDifferent(original.G);
            byte b = MostDifferent(original.B);
            return Color.FromArgb(r, g, b);
        }

        public static Color AverageColor(Bitmap bmp, Rectangle r)
        {
            BitmapData bmd = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int s = bmd.Stride;
            long cr = 0;
            long cg = 0;
            long cb = 0;
            long clr = (long)bmd.Scan0;
            long tmp;
            long row = clr;
            for (int i = 0; i < r.Height; i++)
            {
                long col = row;
                for (int j = 0; j < r.Width; j++)
                {
                    tmp = col;
                    cr += (tmp >> 0x10) & 0xff;
                    cg += (tmp >> 0x08) & 0xff;
                    cb += tmp & 0xff;
                    col++;
                }
                row += s >> 0x02;
            }
            int div = r.Width * r.Height;
            int d2 = div >> 0x01;
            cr = (cr + d2) / div;
            cg = (cg + d2) / div;
            cb = (cb + d2) / div;
            bmp.UnlockBits(bmd);

            return Color.FromArgb((int)cr, (int)cg, (int)cb);
        }

        public static void DrawColorString(this Graphics g, Bitmap bmp, string text, Font font, PointF point)
        {
            SizeF sf = g.MeasureString(text, font);
            Rectangle r = new Rectangle(Point.Truncate(point), Size.Ceiling(sf));
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            Color brsh = MostDifferent(AverageColor(bmp, r));
            g.DrawString(text, font, new SolidBrush(brsh), point);
        }
    }
}
