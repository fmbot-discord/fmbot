using System.Collections.Generic;
using System.Runtime.InteropServices.ComTypes;

namespace FMBot.Domain;

public static class Constants
{

    public const string LastFMUserUrl = "https://www.last.fm/user/";

    public const string LastFmNonExistentImageName = "2a96cbd8b46e442fc41c2b86b821562f.png";

    public const int InviteLinkPermissions = 322624;

    public const ulong BotProductionId = 356268235697553409;

    public const ulong BotBetaId = 493845886166630443;

    public const string DocsUrl = "https://fmbot.xyz";

    public const string CompactTimePeriodList = "weekly/monthly/quarterly/half/yearly/alltime";

    public static readonly string ExpandedTimePeriodList = "'weekly', 'monthly', 'quarterly', 'half', 'yearly', or 'alltime'";

    public const string UserMentionExample = "`@usermention` / `lfm:fm-bot` / `356268235697553409`";

    public const string BillboardExample = "`billboard` / `bb`";
    public const string ExtraLargeExample = "`extralarge` / `xl`";

    public const string UserMentionOrLfmUserNameExample = "`fm-bot` / `@usermention` / `356268235697553409`";

    public const string AutoCompleteLoginRequired = "Connect your Last.fm account with /login first to see results..";
    public const string ServerStaffOnly = "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.";

    public const string GetSupporterButton = "Get .fmbot supporter";
    public const string GetSupporterLink = "https://opencollective.com/fmbot/contribute";

    /// <summary>Amount of days to store plays for users for</summary>
    public const int DaysToStorePlays = 46;

    public const int SupporterMessageChance = 12;
    public const int SupporterPromoChance = 10;

    public const int DefaultPlaysForCrown = 30;

    public const int DefaultPageSize = 10;
    public const int DefaultExtraLargePageSize = 16;

    public const int FeaturedMinute = 0;
    public const int DaysLastUsedForFeatured = 1;

    public const int MaxFriends = 12;
    public const int MaxFriendsSupporter = 18;

    public static readonly int[] PlayCountBreakPoints = {
        50,
        100,
        250,
        420,
        500,
        1000,
        1337,
        2500,
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
