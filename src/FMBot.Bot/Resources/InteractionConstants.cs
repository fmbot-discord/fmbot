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
    public const string ImportClearSpotify = "user-import-clear-spotify";
    public const string ImportClearAppleMusic = "user-import-clear-applemusic";

    public static class ImportModify
    {
        public const string Modify = "user-import-modify";

        public const string PickArtistModal = "im-pa";
        public const string PickAlbumModal = "im-pab";
        public const string PickTrackModal = "im-ptr";

        public const string ArtistRename = "im-arn";
        public const string ArtistRenameModal = "im-arn-mdl";
        public const string ArtistRenameConfirmed = "im-arn-y";
        public const string ArtistDelete = "im-adl";
        public const string ArtistDeleteConfirmed = "im-adl-y";

        public const string AlbumRename = "im-abrn";
        public const string AlbumRenameModal = "im-abrn-mdl";
        public const string AlbumRenameConfirmed = "im-abrn-y";
        public const string AlbumDelete = "im-abdl";
        public const string AlbumDeleteConfirmed = "im-abdl-y";

        public const string TrackRename = "im-trrn";
        public const string TrackRenameModal = "im-trrn-mdl";
        public const string TrackRenameConfirmed = "im-trrn-y";
        public const string TrackDelete = "im-trdl";
        public const string TrackDeleteConfirmed = "im-trdl-y";
    }

    public const string ImportInstructionsSpotify = "import-spotify-instructions";
    public const string ImportInstructionsAppleMusic = "import-applemusic-instructions";

    public const string DeleteStreak = "user-streak-delete";
    public const string DeleteStreakModal = "user-streak-delete-modal";

    public const string TrackPreview = "track-preview";
    public const string TrackLyrics = "track-lyrics";

    public static class ManageAlts
    {
        public const string ManageAltsButton = "managealts";
        public const string ManageAltsPicker = "managealts-picker";
        public const string ManageAltsDeleteAlt = "managealts-delete";
        public const string ManageAltsDeleteAltConfirm = "managealts-deleteconfirmed";
    }

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
        public const string Login = "user-login";
        public const string Settings = "user-settings";
        public const string Profile = "user-profile";
        public const string History = "user-history";
        public const string CrownSelectMenu = "user-crownpicker";
    }

    public static class Album
    {
        public const string Info = "album-info";
        public const string Tracks = "album-tracks";
        public const string Cover = "album-cover";
        public const string RandomCover = "randomalbum-cover";
    }

    public const string Judge = "judge-picked";

    public const string RandomMilestone = "random-milestone";

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

        public const string MoveUserData = "move-user-data-*-*";
        public const string MoveSupporter = "move-supporter-*-*";
    }

    public static class SupporterLinks
    {
        public static string GeneratePurchaseButtons(bool newResponse = true, bool expandWithPerks = false,
            bool showExpandButton = true, string source = "unknown")
        {
            return
                $"supporter-purchase-buttons-{newResponse.ToString()}-{expandWithPerks.ToString()}-{showExpandButton.ToString()}-{source}";
        }

        public const string GetPurchaseButtons = "supporter-purchase-buttons";
        public const string GetPurchaseLink = "supporter-purchase-link";
        public const string GetManageLink = "supporter-manage-link";
        public const string ManageOverview = "supporter-manage-overview";
    }

    public const string BotScrobblingEnable = "user-setting-botscrobbling-enable";
    public const string BotScrobblingDisable = "user-setting-botscrobbling-disable";
    public const string BotScrobblingManage = "user-setting-botscrobbling-manage";

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

    public static class ToggleCrowns
    {
        public const string Enable = "enable-crowns";
        public const string Disable = "disable-crowns";
    }

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

    public static class Genre
    {
        public const string GenreGuild = "genre-guild";
        public const string GenreUser = "genre-user";

        public const string GenreSelectMenu = "genre-picker";
    }

    public const string GuildMembers = "guild-members";
    public const string FeaturedLog = "featured-log";
    public const string GapView = "listeninggaps-view";
    public const string RecapAlltime = "user-recapalltime";
    public const string RecapPicker = "user-recap";

    public static class Game
    {
        public const string AddJumbleHint = "jumble-addhint";
        public const string JumbleUnblur = "jumble-unblur";
        public const string JumbleGiveUp = "jumble-giveup";
        public const string JumbleReshuffle = "jumble-reshuffle";
        public const string JumblePlayAgain = "jumble-playagain";
    }

    public static class Template
    {
        public const string Create = "fm-template-create";
        public const string ImportCode = "fm-template-importcode";
        public const string ImportScript = "fm-template-importscript";
        public const string ManagePicker = "fm-template-manage";
        public const string SetGlobalDefaultPicker = "fm-template-setglobal";
        public const string SetGuildDefaultPicker = "fm-template-setguild";

        public const string SetOptionPicker = "template-option-set";
        public const string ViewScript = "template-view-script";
        public const string ViewScriptModal = "template-view-scriptmodal";
        public const string ViewVariables = "template-variables";
        public const string AddButton = "template-add-button";

        public const string Rename = "template-rename";
        public const string RenameModal = "template-renamemodal";
        public const string Copy = "template-copy";
        public const string Delete = "template-delete";
        public const string DeleteConfirmed = "template-deleteconfirmed";
    }
}
