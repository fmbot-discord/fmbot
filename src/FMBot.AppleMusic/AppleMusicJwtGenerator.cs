using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Identity.Abstractions;
using System.Security.Claims;

namespace FMBot.AppleMusic;

public class AppleMusicJwtAuthProvider : IAuthorizationHeaderProvider
{
    private readonly string _secret;
    private readonly string _keyId;
    private readonly string _teamId;

    public AppleMusicJwtAuthProvider(string secret, string keyId, string teamId)
    {
        this._secret = secret;
        this._keyId = keyId;
        this._teamId = teamId;
    }

    public Task<string> CreateAuthorizationHeaderForUserAsync(
        IEnumerable<string> scopes,
        AuthorizationHeaderProviderOptions options = null,
        ClaimsPrincipal user = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAuthorizationHeaderAsync();
    }

    public Task<string> CreateAuthorizationHeaderForAppAsync(
        string scopes,
        AuthorizationHeaderProviderOptions downstreamApiOptions = null,
        CancellationToken cancellationToken = default)
    {
        return CreateAuthorizationHeaderAsync();
    }

    private Task<string> CreateAuthorizationHeaderAsync()
    {
        var timeNow = DateTime.UtcNow;
        var timeExpired = DateTime.UtcNow.AddDays(180);

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(this._secret));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(credentials)
        {
            {"kid", this._keyId}
        };

        var payload = new JwtPayload
        {
            { "iss", this._teamId },
            { "exp", new DateTimeOffset(timeExpired).ToUnixTimeSeconds() },
            { "iat", new DateTimeOffset(timeNow).ToUnixTimeSeconds() }
        };

        var token = new JwtSecurityToken(header, payload);

        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.WriteToken(token);

        return Task.FromResult($"Bearer {jwtToken}");
    }
}
