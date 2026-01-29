namespace FMBot.Domain;

public static class Constants
{

    public const string DiscogsUserUrl = "https://www.discogs.com/user/";

    public const string DiscogsReleaseUrl = "https://www.discogs.com/release/";

    public const string LastFMUserUrl = "https://www.last.fm/user/";

    public const string LastFmNonExistentImageName = "2a96cbd8b46e442fc41c2b86b821562f.png";

    public const long InviteLinkPermissions = 275415092288;

    public const ulong BotProductionId = 356268235697553409;
    public const ulong BotBetaId = 493845886166630443;

    public const string DocsUrl = "https://fm.bot/";

    public const string CompactTimePeriodList = "weekly/monthly/quarterly/half/yearly/alltime";

    public const string UserMentionExample = "`@usermention` / `lfm:fm-bot` / `356268235697553409`";

    public const string BillboardExample = "`billboard` / `bb`";
    public const string EmbedSizeExample = "`extralarge` / `xl` / `extrasmall` / `xs`";

    public const string UserMentionOrLfmUserNameExample = "`fm-bot` / `@usermention` / `356268235697553409`";

    public const string AutoCompleteLoginRequired = "Connect your Last.fm account with /login first to see results..";
    public const string ServerStaffOnly = "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.";
    public const string FmbotStaffOnly = "Unauthorized, only .fmbot staff can use this command.";

    public const string GetSupporterButton = "Get .fmbot supporter";

    public const string GetSupporterOverviewLink = "https://fm.bot/supporter/";
    public const string GetSupporterDiscordLink = "https://discord.com/application-directory/356268235697553409/store/1120720816154345532";

    public const int SupporterMessageChance = 12;
    public const int SupporterPromoChance = 10;

    public const int DefaultPlaysForCrown = 30;

    public const int DefaultPageSize = 10;
    public const int DefaultExtraSmallPageSize = 5;
    public const int DefaultExtraLargePageSize = 16;

    public const int FeaturedMinute = 0;
    public const int DaysLastUsedForFeatured = 1;

    public const int MaxFriends = 12;
    public const int MaxFriendsSupporter = 18;

    public const int MaxFooterOptions = 4;
    public const int MaxFooterOptionsSupporter = 10;

    public const int MaxButtons = 1;
    public const int MaxButtonsSupporter = 5;

    public const int StreakSaveThreshold = 25;

    public const int NonSupporterMaxSavedPlays = 15000;
    public const int NonSupporterMaxSavedPlayPages = 15;

    public const string GetPremiumServer = "This feature is not quite ready yet. Stay tuned!";

    public const int MaxAlts = 8;

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
