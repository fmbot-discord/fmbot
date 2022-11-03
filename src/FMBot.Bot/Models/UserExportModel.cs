namespace FMBot.Bot.Models;

public class UserExportModel
{
    public UserExportModel(string discordUserID, string userNameLastFM)
    {
        this.DiscordUserID = discordUserID;
        this.LastFMUsername = userNameLastFM;
    }

    public string DiscordUserID { get; }
    public string LastFMUsername { get; }
}
