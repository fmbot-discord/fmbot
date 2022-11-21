namespace FMBot.Discogs.Models;

public class DiscogsAuthInitialization
{
    public DiscogsAuthInitialization(string loginUrl, string oathToken, string oauthTokenSecret)
    {
        this.LoginUrl = loginUrl;
        this.OathToken = oathToken;
        this.OauthTokenSecret = oauthTokenSecret;
    }

    public string LoginUrl { get; set; }
    public string OathToken { get; set; }
    public string OauthTokenSecret { get; set; }
}
