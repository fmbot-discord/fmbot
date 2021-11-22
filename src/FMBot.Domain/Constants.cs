using System.Drawing;

namespace FMBot.Domain
{
    public static class Constants
    {

        public const string LastFMUserUrl = "https://www.last.fm/user/";

        public const string LastFmNonExistentImageName = "2a96cbd8b46e442fc41c2b86b821562f.png";

        public const int InviteLinkPermissions = 322624;

        public const ulong BotProductionId = 356268235697553409;

        public const ulong BotDevelopId = 493845886166630443;

        public const string DocsUrl = "https://fmbot.xyz";

        public const string CompactTimePeriodList = "weekly/monthly/quarterly/half/yearly/alltime";

        public static readonly string ExpandedTimePeriodList = "'weekly', 'monthly', 'quarterly', 'half', 'yearly', or 'alltime'";

        public const string UserMentionExample = "`@usermention` / `lfm:fm-bot` / `356268235697553409`";

        public const string BillboardExample = "`billboard` / `bb`";
        public const string ExtraLargeExample = "`extralarge` / `xl`";

        public const string UserMentionOrLfmUserNameExample = "`fm-bot` / `@usermention` / `356268235697553409`";

        /// <summary>Amount of days to store plays for users for</summary>
        public const int DaysToStorePlays = 32;

        public const int SupporterMessageChance = 15;

        public const int DefaultPlaysForCrown = 30;

        public const int DefaultPageSize = 10;
        public const int DefaultExtraLargePageSize = 16;

        public static readonly int[] PlayCountBreakPoints = {
            100,
            420,
            1000,
            1337,
            5000,
            10000,
            25000,
            50000,
            100000,
            150000,
            200000,
            250000,
            300000,
            350000,
            400000,
            450000,
            500000,
            600000,
            700000,
            800000,
            900000,
            1000000,
            2000000,
            3000000,
            4000000,
            5000000
        };
    }
}

