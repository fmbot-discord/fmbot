using System;
using System.Collections.Generic;

namespace FMBot.Bot.Models
{
    public class TimeOverview
    {
        public class WeekOverview
        {
            public List<DayOverview> Days { get; set; }
        }

        public class DayOverview
        {
            public DateTime Date { get; set; }

            public int Playcount { get; set; }

            public string TopArtist { get; set; }

            public string TopTrack { get; set; }

            public string TopAlbum { get; set; }
        }
    }
}
