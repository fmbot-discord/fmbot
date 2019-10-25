using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace FMBot.Bot.Extensions
{
    public static class BitmapExtensions
    {
        private static byte MostDifferent(byte original)
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

        private static Color MostDifferent(Color original)
        {
            byte r = MostDifferent(original.R);
            byte g = MostDifferent(original.G);
            byte b = MostDifferent(original.B);
            return Color.FromArgb(r, g, b);
        }

        private static Color AverageColor(Bitmap bmp)
        {
            int r = 0;
            int g = 0;
            int b = 0;

            int total = 0;

            for (int x = 0; x < bmp.Width; x++)
            {
                for (int y = 0; y < bmp.Height; y++)
                {
                    Color clr = bmp.GetPixel(x, y);

                    r += clr.R;
                    g += clr.G;
                    b += clr.B;

                    total++;
                }
            }

            r /= total;
            g /= total;
            b /= total;

            return Color.FromArgb(r, g, b);
        }
		
		private int PerceivedBrightness(Color c)
		{
			return (int)Math.Sqrt(
				c.R * c.R * .299 +
				c.G * c.G * .587 +
				c.B * c.B * .114);
		}

        public static void DrawColorString(this Graphics g, Bitmap bmp, string text, Font font, PointF point)
        {
            SizeF sf = g.MeasureString(text, font);
            Rectangle r = new Rectangle(Point.Truncate(point), Size.Ceiling(sf));
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
			Color foreColor = (PerceivedBrightness(MostDifferent(AverageColor(bmp))) > 130 ? Color.Black : Color.White);
            g.DrawString(text, font, new SolidBrush(foreColor), point);
        }
    }
}
