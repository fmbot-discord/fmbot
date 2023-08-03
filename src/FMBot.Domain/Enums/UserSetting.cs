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
    [Option("Spotify import ⭐", "Manage your Spotify imports")]
    SpotifyImport = 10,
    [Option("User reactions ⭐", "Set personal automated emoji reactions")]
    UserReactions = 11,
    [Option("Music bot scrobbling", "Toggle automatic scrobbling other music bots")]
    BotScrobbling = 20,
    [Option("Delete account", "Delete your .fmbot data and account")]
    DeleteAccount = 30
}
