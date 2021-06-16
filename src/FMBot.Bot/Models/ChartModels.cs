using System.Collections.Generic;
using Discord;
using FMBot.Domain.Models;
using SkiaSharp;
using Color = System.Drawing.Color;

namespace FMBot.Bot.Models
{
    public class ChartSettings
    {
        public ChartSettings(IUser discordUser)
        {
            this.DiscordUser = discordUser;

            this.ChartImages = new List<ChartImage>();

            this.TitleSetting = TitleSetting.Titles;
            this.SkipWithoutImage = false;
            this.UsePlays = false;
        }

        public List<TopAlbum> Albums { get; set; }

        public List<TopArtist> Artists { get; set; }

        public bool ArtistChart { get; set; }

        public int Height { get; set; }

        public int Width { get; set; }

        public int ImagesNeeded { get; set; }

        public List<ChartImage> ChartImages { get; set; }

        public TimePeriod TimePeriod { get; set; }

        public IUser DiscordUser { get; set; }

        public string TimespanString { get; set; }

        public string TimespanUrlString { get; set; }

        public TitleSetting TitleSetting { get; set; }

        public bool SkipWithoutImage { get; set; }

        public bool CustomOptionsEnabled { get; set; }

        public bool RainbowSortingEnabled { get; set; }

        public bool UsePlays { get; set; }

        public int? CensoredAlbums { get; set; }
    }

    public class ChartImage
    {
        public ChartImage(SKBitmap image, int indexOf, bool validImage, Color? primaryColor)
        {
            this.Image = image;
            this.Index = indexOf;
            this.ValidImage = validImage;
            this.PrimaryColor = primaryColor;
        }

        public SKBitmap Image { get; }

        public int Index { get; }

        public bool ValidImage { get; }

        public Color? PrimaryColor { get; }

        public int? ColorSortValue { get; set;  }
    }

    public enum TitleSetting
    {
        Titles = 1,
        TitlesDisabled = 2,
        ClassicTitles = 3
    }
}
