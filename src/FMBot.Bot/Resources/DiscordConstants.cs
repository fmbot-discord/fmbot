using System.Collections.Generic;
using Discord;
using Fergun.Interactive.Pagination;

namespace FMBot.Bot.Resources;

public static class DiscordConstants
{
    public static Color LastFmColorRed = new(186, 0, 0);

    /// <summary>The Discord color for a warning embed.</summary>
    public static Color WarningColorOrange = new(255, 174, 66);

    public static Color SuccessColorGreen = new(50, 205, 50);

    public static Color InformationColorBlue = new(68, 138, 255);

    public static Color SpotifyColorGreen = new(30, 215, 97);


    public static readonly IDictionary<IEmote, PaginatorAction> PaginationEmotes = new Dictionary<IEmote, PaginatorAction>
    {
        { Emote.Parse("<:pages_first:883825508633182208>"), PaginatorAction.SkipToStart},
        { Emote.Parse("<:pages_previous:883825508507336704>"), PaginatorAction.Backward},
        { Emote.Parse("<:pages_next:883825508087922739>"), PaginatorAction.Forward},
        { Emote.Parse("<:pages_last:883825508482183258>"), PaginatorAction.SkipToEnd}
    };

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
}
