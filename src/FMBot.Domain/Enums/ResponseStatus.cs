namespace FMBot.Domain.Enums;

public enum ResponseStatus
{
    Unknown = 0,

    /// <summary>
    /// The service requested does not exist (2)
    /// </summary>
    BadService = 2,

    /// <summary>
    /// The method requested does not exist in this service (3)
    /// </summary>
    BadMethod = 3,

    /// <summary>
    /// This credential does not have permission to access the service requested (4)
    /// </summary>
    BadAuth = 4,

    /// <summary>
    /// This service doesn't exist in the requested format
    /// </summary>
    BadFormat = 5,

    /// <summary>
    /// Required parameters were missing from the request (6)
    /// </summary>
    MissingParameters = 6,

    /// <summary>
    /// The requested resource is invalid (7)
    /// </summary>
    BadResource = 7,

    /// <summary>
    /// An unknown failure occured when creating the response (8)
    /// </summary>
    Failure = 8,

    /// <summary>
    /// The session has expired, reauthenticate before retrying (9)
    /// </summary>
    SessionExpired = 9,

    /// <summary>
    /// The provided API key was invalid (10)
    /// </summary>
    BadApiKey = 10,

    /// <summary>
    /// This service is temporarily offline, retry later (11)
    /// </summary>
    ServiceDown = 11,

    /// <summary>
    /// The request signature was invalid. Check that your API key and secret are valid. (13)
    /// You can generate new keys at http://www.last.fm/api/accounts
    /// </summary>
    BadMethodSignature = 13,

    /// <summary>
    /// There was a temporary error while processing the request, retry later (16)
    /// </summary>
    TemporaryFailure = 16,

    /// <summary>
    /// User required to be logged in. (17)
    /// Requested profile might not have privacy set to public.
    /// </summary>
    LoginRequired = 17,

    /// <summary>
    /// The request was successful!
    /// </summary>
    Successful = 20,

    /// <summary>
    /// The request has been cached, it will be sent later
    /// </summary>
    Cached = 21,

    /// <summary>
    /// The request could not be sent, and could not be cached.
    /// Check the Exception property of the response for details.
    /// </summary>
    CacheFailed = 22,

    /// <summary>
    /// The request failed, check for network connectivity
    /// </summary>
    RequestFailed = 23,

    /// <summary>
    /// This API key has been suspended, please generate a new key at http://www.last.fm/api/accounts (26)
    /// </summary>
    KeySuspended = 26,

    /// <summary>
    /// This API key has been rate-limited because too many requests have been made in a short period. Retry later (29)
    /// For more information on rate limits, please contact Last.FM at the partners@last.fm email address.
    /// </summary>
    RateLimited = 29
}
