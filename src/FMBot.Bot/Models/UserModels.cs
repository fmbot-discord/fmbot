namespace FMBot.Bot.Models
{
    public class UserExportModel
    {
        public UserExportModel(ulong discordUserID, string discordUsername, string userNameLastFM)
        {
            this.DiscordUserID = discordUserID;
            this.LastFMUsername = userNameLastFM;
            this.DiscordUsername = discordUsername;
        }

        public ulong DiscordUserID { get; }
        public string DiscordUsername { get; }
        public string LastFMUsername { get; }
    }
}
