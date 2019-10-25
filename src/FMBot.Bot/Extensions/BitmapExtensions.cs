using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace FMBot.Bot.Extensions
{
    public static class BitmapExtensions
    {
        public static void DrawColorString(this Graphics g, Bitmap bmp, string text, Font font, PointF point)
        {
            SizeF sf = g.MeasureString(text, font);
            Rectangle r = new Rectangle(Point.Truncate(point), Size.Ceiling(sf));
            r.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            Color brsh = Color.FromArgb(255, 255, 255);
            g.DrawString(text, font, new SolidBrush(brsh), point);
			Color brshShadow = Color.FromArgb(0, 0, 0);
			PointF shadowAngle = new PointF(point.x-2f, point.y-2f);
            g.DrawString(text, font, new SolidBrush(brshShadow), point);
        }
    }
}
