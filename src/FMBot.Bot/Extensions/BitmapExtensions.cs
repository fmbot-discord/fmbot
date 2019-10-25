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
            //shadow
			Color brshShadow = Color.Black;
			PointF shadowAngle = new PointF(point.X+5, point.Y+5);
            g.DrawString(text, font, new SolidBrush(brshShadow), point);
			//text
			Color brsh = Color.White;
            g.DrawString(text, font, new SolidBrush(brsh), point);
        }
    }
}
