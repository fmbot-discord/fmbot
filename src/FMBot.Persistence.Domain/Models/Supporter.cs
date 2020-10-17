using System;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models
{
    public class Supporter
    {
        public int SupporterId { get; set; }

        public string Name { get; set; }

        public SupporterType SupporterType { get; set; }

        public string Notes { get; set; }

        public bool SupporterMessagesEnabled { get; set; }

        public bool VisibleInOverview { get; set; }

        public DateTime Created { get; set; }

    }
}
