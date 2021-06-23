using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models
{
    public class WhoKnowsObjectWithUser
    {
        public string Name { get; set; }

        public int Playcount { get; set; }

        public string LastFMUsername { get; set; }

        public string DiscordName { get; set; }

        public int UserId { get; set; }

        public DateTime? RegisteredLastFm { get; set; }

        public PrivacyLevel? PrivacyLevel { get; set; }

        public bool? WhoKnowsWhitelisted { get; set; }
    }

    public class WhoKnowsSettings
    {
        public bool HidePrivateUsers { get; set; }

        public string NewSearchValue { get; set; }
    }
}
