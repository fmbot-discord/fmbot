using System.Collections.Generic;
using System.Drawing;
using Discord;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;

namespace FMBot.Bot.Models
{
    public class ChartSettings
    {
        public ChartSettings(int height, int width, IUser discordUser)
        {
            this.Height = height;
            this.Width = width;
            this.DiscordUser = discordUser;

            this.ChartImages = new List<ChartImage>();

            this.TitlesEnabled = true;
            this.SkipArtistsWithoutImage = false;
        }

        public PageResponse<LastAlbum> Albums { get; set; }

        public int Height { get; set; }

        public int Width { get; set; }

        public int ImagesNeeded { get; set; }

        public List<ChartImage> ChartImages { get; set; }

        public IUser DiscordUser { get; set; }

        public bool TitlesEnabled { get; set; }

        public bool SkipArtistsWithoutImage { get; set; }
    }

    public class ChartImage
    {
        public ChartImage(Bitmap image, int indexOf, bool validImage)
        {
            this.Image = image;
            this.Index = indexOf;
            this.ValidImage = validImage;
        }

        public Bitmap Image { get; }

        public int Index { get; }

        public bool ValidImage { get; }
    }
}
