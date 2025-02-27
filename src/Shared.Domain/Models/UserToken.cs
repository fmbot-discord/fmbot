using Shared.Domain.Enums;

namespace Shared.Domain.Models;

public class UserToken
{
    public int Id { get; set; }

    public ulong DiscordUserId { get; set; }
    public DateTime LastUpdated { get; set; }

    public BotType BotType { get; set; }

    public string Scope { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime TokenExpiresAt { get; set; }
}
