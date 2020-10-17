using System.Drawing;

namespace FMBot.Domain
{
    public static class Constants
    {

        public const string LastFMUserUrl = "https://www.last.fm/user/";

        public const int InviteLinkPermissions = 52288;

        public const ulong BotProductionId = 356268235697553409;

        public const ulong BotStagingId = 493845886166630443;

        public const string DocsUrl = "https://fmbot.xyz";

        public const string CompactTimePeriodList = "weekly/monthly/quarterly/half/yearly/alltime";

        public const string ExpandedTimePeriodList = "'weekly', 'monthly', 'quarterly', 'half', 'yearly', or 'alltime'";

        /// <summary>Amount of users to index. Should always end with 000</summary>
        public const int ArtistsToIndex = 4000;

        /// <summary>Amount of days to store plays for users for</summary>
        public const int DaysToStorePlays = 32;

        public const int SupporterMessageChance = 25;

    }
}

