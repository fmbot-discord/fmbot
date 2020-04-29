using System;
using Discord;

namespace FMBot.Bot.Resources
{
    public static class Constants
    {
        public static Color LastFMColorRed = new Color(186, 0, 0);

        public const string LastFMUserUrl = "https://www.last.fm/user/";

        public const int InviteLinkPermissions = 52288;

        public const int BotWarmupTimeInSeconds = 180;

        public const ulong BotProductionId = 356268235697553409;

        public const string DocsUrl = "https://fmbot.xyz";

        public static TimeSpan GuildIndexCooldown = TimeSpan.FromDays(2);

        /// <summary>Amount of users to index. Should always end with 000</summary>
        public const int ArtistsToIndex = 4000;

        /// <summary>The Discord color for a warning embed.</summary>
        public static Color WarningColorOrange = new Color(255, 174, 66);
    }
}

