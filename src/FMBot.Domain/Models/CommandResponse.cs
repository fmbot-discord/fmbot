namespace FMBot.Domain.Models;

public enum CommandResponse
{
    Ok = 1,

    Help = 2,

    WrongInput = 3,

    UsernameNotSet = 4,

    NotFound = 5,

    NotSupportedInDm = 6,

    Error = 7,

    NoScrobbles = 8,

    NoPermission = 9,

    Cooldown = 10,

    IndexRequired = 11,

    LastFmError = 12,

    Disabled = 13,

    Censored = 14,

    UserBlocked = 15,

    RateLimited = 16
}
