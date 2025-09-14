using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum UserSetting
{
    [Option("Privacy", "Your privacy for Global WhoKnows")]
    Privacy = 2,
    [Option("Change your .fm", "Customize your .fm command")]
    FmMode = 3,
    [Option("WhoKnows mode", "Set the default WhoKnows response type")]
    WkMode = 4,
    [Option("Out of sync", "Info on what to do when Spotify and Last.fm are out of sync")]
    OutOfSync = 6,
    [Option("Spotify & Apple Music imports ⭐", "Add and manage your Spotify & Apple Music imports")]
    SpotifyImport = 10,
    [Option("Command shortcuts ⭐", "Configure your text command shortcuts")]
    CommandShortcuts = 11,
    [Option("User reactions ⭐", "Set personal automated emoji reactions")]
    UserReactions = 12,
    [Option("Localization", "Set your timezone and number formatting")]
    Localization = 15,
    [Option("Music bot scrobbling", "Toggle automatically scrobbling other music bots")]
    BotScrobbling = 20,
    [Option("Linked roles", "Enable linked roles for servers that use them")]
    LinkedRoles = 21,
    [Option("Manage alts", "Manage your other .fmbot accounts")]
    ManageAlts = 25,
    [Option("Delete account", "Delete your .fmbot data and account")]
    DeleteAccount = 30
}
