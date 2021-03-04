using System.Collections.Generic;
using Discord;
using Interactivity.Pagination;

namespace FMBot.Bot.Resources
{
    public static class DiscordConstants
    {
        public static Color LastFmColorRed = new Color(186, 0, 0);

        /// <summary>The Discord color for a warning embed.</summary>
        public static Color WarningColorOrange = new Color(255, 174, 66);

        public static Color SuccessColorGreen = new Color(50, 205, 50);

        public static Color SpotifyColorGreen = new Color(30, 215, 97);

        public static readonly Dictionary<IEmote, PaginatorAction> PaginationEmotes = new Dictionary<IEmote, PaginatorAction>
        {
            { new Emoji("⏮️"), PaginatorAction.SkipToStart},
            { new Emoji("⬅️"), PaginatorAction.Backward},
            { new Emoji("➡️"), PaginatorAction.Forward},
            { new Emoji("⏭️"), PaginatorAction.SkipToEnd}
        };

        public const int PaginationTimeoutInSeconds = 120;
    }
}
