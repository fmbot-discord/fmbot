using System.Collections.Generic;
using Discord;
using FMBot.Domain.Models;
using SkiaSharp;
using Color = System.Drawing.Color;

namespace FMBot.Bot.Models;

public class ChartSettings
{
    public ChartSettings(IUser discordUser)
    {
        this.DiscordUser = discordUser;

        this.ChartImages = new List<ChartImage>();

        this.TitleSetting = TitleSetting.Titles;
        this.SkipWithoutImage = false;
        this.ContainsNsfw = false;
    }

    public List<TopAlbum> Albums { get; set; }

    public List<TopArtist> Artists { get; set; }

    public bool ArtistChart { get; set; }

    private int _height;
    public int Height
    {
        get
        {
            return this._height;
        }
        set
        {
            this._height = value;
            this.ImagesNeeded = this._width * this._height;
        }
    }

    private int _width;
    public int Width
    {
        get
        {
            return this._width;
        }
        set
        {
            this._width = value;
            this.ImagesNeeded = this._width * this._height;
        }
    }

    public int ImagesNeeded { get; set; }

    public List<ChartImage> ChartImages { get; set; }

    public TimeSettingsModel TimeSettings { get; set; }

    public IUser DiscordUser { get; set; }

    public string TimespanString { get; set; }

    public string TimespanUrlString { get; set; }

    public TitleSetting TitleSetting { get; set; }

    public bool SkipWithoutImage { get; set; }

    public bool SkipNsfw { get; set; }

    public bool CustomOptionsEnabled { get; set; }

    public bool RainbowSortingEnabled { get; set; }

    public bool ContainsNsfw { get; set; }
    public int? CensoredItems { get; set; }
}

public class ChartImage
{
    public ChartImage(SKBitmap image, int indexOf, bool validImage, Color? primaryColor, bool nsfw, bool censored)
    {
        this.Image = image;
        this.Index = indexOf;
        this.ValidImage = validImage;
        this.PrimaryColor = primaryColor;
        this.Nsfw = nsfw;
        this.Censored = censored;
    }

    public SKBitmap Image { get; }

    public int Index { get; }

    public bool ValidImage { get; }

    public Color? PrimaryColor { get; }

    public bool Nsfw { get; set; }

    public bool Censored { get; set; }

    public int? ColorSortValue { get; set;  }
}

public enum TitleSetting
{
    Titles = 1,
    TitlesDisabled = 2,
    ClassicTitles = 3
}
