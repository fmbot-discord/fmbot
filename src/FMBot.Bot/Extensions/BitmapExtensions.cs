using System;
using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace FMBot.Bot.Extensions
{
    public static class BitmapExtensions
    {
        private static byte MostDifferent(byte original)
        {
            return original < 0x80 ? (byte) 0xff : (byte) 0x00;
        }

        private static Color MostDifferent(Color original)
        {
            var r = MostDifferent(original.R);
            var g = MostDifferent(original.G);
            var b = MostDifferent(original.B);
            return Color.FromArgb(r, g, b);
        }

        public static unsafe Color GetAverageRgbColor(this SKBitmap skbmp)
        {
            var bmp = SkiaSharp.Views.Desktop.Extensions.ToBitmap(skbmp);

            var totalRed = 0;
            var totalGreen = 0;
            var totalBlue = 0;

            var bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite,
                bmp.PixelFormat);
            var bitsPerPixel = GetBitsPerPixel(bData.PixelFormat);
            var scan0 = (byte*) bData.Scan0.ToPointer();
            for (var i = 0; i < bData.Height; ++i)
            {
                for (var j = 0; j < bData.Width; ++j)
                {
                    var data = scan0 + i * bData.Stride + j * bitsPerPixel / 8;
                    var clr = Color.FromArgb(data[3], data[2], data[1], data[0]);
                    totalRed += clr.R;
                    totalGreen += clr.G;
                    totalBlue += clr.B;
                }
            }

            bmp.UnlockBits(bData);

            var totalPixels = bData.Width * bData.Height;
            var avgRed = (byte) (totalRed / totalPixels);
            var avgGreen = (byte) (totalGreen / totalPixels);
            var avgBlue = (byte) (totalBlue / totalPixels);
            return Color.FromArgb(avgRed, avgGreen, avgBlue);
        }

        internal static byte GetBitsPerPixel(PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                    return 24;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    return 32;
                default:
                    throw new ArgumentException("Only 24 and 32 bit images are supported");
            }
        }

        private static int PerceivedBrightness(Color c)
        {
            return (int) Math.Sqrt(
                c.R * c.R * .299 +
                c.G * c.G * .587 +
                c.B * c.B * .114);
        }

        public static SKColor GetTextColor(this SKBitmap bmp)
        {
            return PerceivedBrightness(MostDifferent(GetAverageRgbColor(bmp))) > 130 ? SKColors.White : SKColors.Black;
        }

        public static Color GetPrimaryColor(this SKBitmap bmp)
        {
            return GetAverageRgbColor(bmp);
        }
    }
}
