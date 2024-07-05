using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace FMBot.AppleMusic;

public class AppleMusicJwtAuthProvider
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



    public Task<string> CreateAuthorizationHeaderAsync()
    {
        var timeNow = DateTime.UtcNow;
        var timeExpired = timeNow.AddDays(180);

        var privateKey = ParsePrivateKey(_secret);
        var securityKey = new ECDsaSecurityKey(privateKey) { KeyId = _keyId };
        var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);

        var header = new JwtHeader(signingCredentials);

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

    private static ECDsa ParsePrivateKey(string privateKey)
    {
        var keyString = privateKey
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();

        var keyBytes = Convert.FromBase64String(keyString);

        var ecDsa = ECDsa.Create();
        ecDsa.ImportPkcs8PrivateKey(keyBytes, out _);
        return ecDsa;
    }
}
