namespace FMBot.Bot.Resources;

public static class InteractionConstants
{
    public static class FmCommand
    {
        public const string FmSettingType = "fm-type-menu";
        public const string FmSettingFooter = "fm-footer-menu";
        public const string FmSettingFooterSupporter = "fm-footer-menu-supporter";

        public const string FmModeChange = "fm-mode-pick";
    }

    public const string GuildSetting = "guild-setting-picker";
    public const string UserSetting = "user-setting-picker";

    public const string FmPrivacySetting = "fm-gwk-privacy";

    public static class Discogs
    {
        public const string AuthDm = "discogs-authdm";
        public const string StartAuth = "discogs-startauth";

        public const string Collection = "discogs-collection";
        public const string ToggleCollectionValue = "discogs-togglecollectionvalue";

        public const string RemoveAccount = "discogs-remove";
    }

    public const string ResponseModeSetting = "response-mode-set";
    public const string ResponseModeChange = "response-mode-pick";

    public const string RemoveFmbotAccount = "remove-account-confirm";
    public const string RemoveFmbotAccountModal = "remove-account-confirm-modal";

    public const string FmGuildSettingType = "fm-guild-type-menu";

    public const string SetAllowedRoleMenu = "guild-allowed-roles-menu";
    public const string SetBlockedRoleMenu = "guild-blocked-roles-menu";
    public const string SetBotManagementRoleMenu = "guild-bot-management-roles-menu";

    public const string SetGuildActivityThreshold = "set-guild-activity-threshold";
    public const string SetGuildActivityThresholdModal = "set-guild-activity-threshold-modal";
    public const string RemoveGuildActivityThreshold = "remove-guild-activity-threshold";

    public const string ImportManage = "user-import-manage";
    public const string ImportSetting = "user-import-setting";
    public const string ImportClear = "user-import-clear";

    public const string DeleteStreak = "user-streak-delete";
    public const string DeleteStreakModal = "user-streak-delete-modal";

    public static class Artist
    {
        public const string Info = "artist-info";
        public const string Overview = "artist-overview";

        public const string Albums = "artist-albums";
        public const string Tracks = "artist-tracks";

        public const string Crown = "artist-crown";
        public const string WhoKnows = "artist-whoknows";
    }

    public static class User
    {
        public const string Profile = "user-profile";
        public const string History = "user-history";
    }

    public static class Album
    {
        public const string Info = "album-info";
        public const string Tracks = "album-tracks";
        public const string Cover = "album-cover";
    }

    public static class ModerationCommands
    {
        public const string CensorTypes = "admin-censor-*";

        public const string ArtistAlias = "artist-alias-*";

        public const string GlobalWhoKnowsReport = "gwk-report";
        public const string GlobalWhoKnowsReportModal = "gwk-report-modal";

        public const string ReportArtist = "report-artist";
        public const string ReportArtistModal = "report-artist-modal";

        public const string ReportAlbum = "report-album";
        public const string ReportAlbumModal = "report-album-modal";
    }

    public const string BotScrobblingEnable = "user-setting-botscrobbling-enable";
    public const string BotScrobblingDisable = "user-setting-botscrobbling-disable";

    public const string SetPrefix = "set-prefix-threshold";
    public const string SetPrefixModal = "set-prefix-modal";
    public const string RemovePrefix = "remove-prefix";

    public const string SetFmbotActivityThreshold = "set-fmbot-activity-threshold";
    public const string SetFmbotActivityThresholdModal = "set-fmbot-activity-threshold-modal";
    public const string RemoveFmbotActivityThreshold = "remove-fmbot-activity-threshold";

    public const string SetCrownActivityThreshold = "set-crown-activity-threshold";
    public const string SetCrownActivityThresholdModal = "set-crown-activity-threshold-modal";
    public const string RemoveCrownActivityThreshold = "remove-crown-activity-threshold";

    public const string SetCrownMinPlaycount = "set-crown-min-playcount";
    public const string SetCrownMinPlaycountModal = "set-crown-min-playcount-modal";
    public const string RemoveCrownMinPlaycount = "remove-crown-min-playcount";

    public const string RunCrownseeder = "run-crownseeder";

    public static class ToggleCommand
    {
        public const string ToggleCommandMove = "toggle-command-move";
        public const string ToggleCommandAdd = "toggle-command-add";
        public const string ToggleCommandRemove = "toggle-command-remove";
        public const string ToggleCommandClear = "toggle-command-clear";

        public const string ToggleCommandAddModal = "toggle-command-modal-add";
        public const string ToggleCommandRemoveModal = "toggle-command-modal-remove";

        public const string ToggleCommandEnableAll = "toggle-command-enable-all";
        public const string ToggleCommandDisableAll = "toggle-command-disable-all";

        public const string ToggleCommandChannelFmType = "toggle-command-fm-mode";

        public const string ToggleGuildCommandAdd = "toggle-guild-command-add";
        public const string ToggleGuildCommandRemove = "toggle-guild-command-remove";
        public const string ToggleGuildCommandClear = "toggle-guild-command-clear";

        public const string ToggleGuildCommandAddModal = "toggle-guild-command-modal-add";
        public const string ToggleGuildCommandRemoveModal = "toggle-guild-command-modal-remove";
    }

    public const string WhoKnowsRolePicker = "whoknows-role-picker";
    public const string WhoKnowsAlbumRolePicker = "whoknows-album-role-picker";
    public const string WhoKnowsTrackRolePicker = "whoknows-track-role-picker";

    public const string GenreGuild = "genre-guild";
    public const string GenreUser = "genre-user";

    public const string GuildMembers = "guild-members";
}
