namespace FMBot.Discogs.Models;

public class DiscogsAuth
{
    public DiscogsAuth(string accessToken, string accessTokenSecret)
    {
        this.AccessToken = accessToken;
        this.AccessTokenSecret = accessTokenSecret;
    }

    public string AccessToken { get; set; }
    public string AccessTokenSecret { get; set; }
}
