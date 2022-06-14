namespace FMBot.Bot.Models
{
    public class TopListSettings
    {
        public TopListSettings()
        {
        }

        public TopListSettings(bool extraLarge, bool billboard)
        {
            this.ExtraLarge = extraLarge;
            this.Billboard = billboard;
        }

        public bool ExtraLarge { get; set; }

        public bool Billboard { get; set; }
        public string NewSearchValue { get; set; }
    }
}
