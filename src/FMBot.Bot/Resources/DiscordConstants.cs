using System.Collections.Generic;
using Fergun.Interactive.Pagination;
using NetCord;

namespace FMBot.Bot.Resources;

public static class DiscordConstants
{
    public static Color LastFmColorRed = new(186, 0, 0);

    /// <summary>The Discord color for a warning embed.</summary>
    public static Color WarningColorOrange = new(255, 174, 66);

    public static Color SuccessColorGreen = new(50, 205, 50);

    public static Color InformationColorBlue = new(68, 138, 255);

    public static Color SpotifyColorGreen = new(30, 215, 97);

    public static Color AppleMusicRed = new(249, 87, 107);

    public static Color Gold = new(241, 196, 15);

    public static readonly List<PaginatorButton> PaginationEmotes =
    [
        new(EmojiProperties.Custom(PagesFirst), PaginatorAction.SkipToStart, ButtonStyle.Secondary),
        new(EmojiProperties.Custom(PagesPrevious), PaginatorAction.Backward, ButtonStyle.Secondary),
        new(EmojiProperties.Custom(PagesNext), PaginatorAction.Forward, ButtonStyle.Secondary),
        new(EmojiProperties.Custom(PagesLast), PaginatorAction.SkipToEnd, ButtonStyle.Secondary)
    ];

    public const ulong PagesFirst = 883825508633182208;
    public const ulong PagesPrevious = 883825508507336704;
    public const ulong PagesNext = 883825508087922739;
    public const ulong PagesLast = 883825508482183258;
    public const ulong PagesGoTo = 1138849626234036264;

    public const int PaginationTimeoutInSeconds = 120;

    public const ulong FiveOrMoreUp = 912380324841918504;
    public const ulong OneToFiveUp = 912085138232442920;
    public const ulong SamePosition = 912374491752046592;
    public const ulong OneToFiveDown = 912085138245029888;
    public const ulong FiveOrMoreDown = 912380324753838140;
    public const ulong New = 912087988001980446;

    public const ulong Info = 1183840696457777153;
    public const ulong Vinyl = 1043644602969763861;

    public const ulong Server = 961685224041902140;
    public const ulong User = 961687127249260634;

    public const ulong Facebook = 1183830516533825656;
    public const ulong Twitter = 1183831922917511298;
    public const ulong Instagram = 1183829878458548224;
    public const ulong TikTok = 1183831072413335742;
    public const ulong Bandcamp = 1183838619270643823;
    public const ulong Spotify = 882221219334725662;
    public const ulong RateYourMusic = 1183851241151930399;
    public const ulong YouTube = 1230496939355934730;
    public const ulong AppleMusic = 1218182727149420544;
    public const ulong LastFm = 882227627287515166;

    public const ulong Loading = 821676038102056991;
    public const ulong Imports = 1131511469096312914;
    public const ulong Discoveries = 1145740579284713512;
    public const ulong Shortcut = 1416430054061117610;
    public const ulong PlayPreview = 1305607890941378672;
}
