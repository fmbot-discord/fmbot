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

    public static readonly List<PaginatorButton> PaginationEmotes =
    [
        new(Emote.Parse(PagesFirst), PaginatorAction.SkipToStart, ButtonStyle.Secondary),
        new(Emote.Parse(PagesPrevious), PaginatorAction.Backward, ButtonStyle.Secondary),
        new(Emote.Parse(PagesNext), PaginatorAction.Forward, ButtonStyle.Secondary),
        new(Emote.Parse(PagesLast), PaginatorAction.SkipToEnd, ButtonStyle.Secondary)
    ];

    public const string PagesFirst = "<:pages_first:883825508633182208>";
    public const string PagesPrevious = "<:pages_previous:883825508507336704>";
    public const string PagesNext = "<:pages_next:883825508087922739>";
    public const string PagesLast = "<:pages_last:883825508482183258>";
    public const string PagesGoTo = "<:pages_goto:1138849626234036264>";


    public const int PaginationTimeoutInSeconds = 120;

    public const string FiveOrMoreUp = "<:5_or_more_up:912380324841918504>";
    public const string OneToFiveUp = "<:1_to_5_up:912085138232442920>";
    public const string SamePosition = "<:same_position:912374491752046592>";
    public const string OneToFiveDown = "<:1_to_5_down:912085138245029888>";
    public const string FiveOrMoreDown = "<:5_or_more_down:912380324753838140>";
    public const string New = "<:new:912087988001980446>";

    public const string Info = "<:info:1183840696457777153>";
    public const string Vinyl = "<:vinyl:1043644602969763861>";

    public const string Facebook = "<:social_facebook:1183830516533825656>";
    public const string Twitter = "<:social_twitter:1183831922917511298>";
    public const string Instagram = "<:social_instagram:1183829878458548224>";
    public const string TikTok = "<:social_tiktok:1183831072413335742>";
    public const string Bandcamp = "<:social_bandcamp:1183838619270643823>";
    public const string Spotify = "<:spotify:882221219334725662>";
    public const string RateYourMusic = "<:rym:1183851241151930399>";
    public const string YouTube = "<:youtube:1230496939355934730>";
    public const string AppleMusic = "<:apple_music:1218182727149420544>";

    public const string Loading = "<a:loading:821676038102056991>";
    public const string Imports = "<:fmbot_importing:1131511469096312914>";
    public const string Discoveries = "<:fmbot_discoveries:1145740579284713512>";
    public const string Shortcut = "<:shortcut:1416430054061117610>";
}
