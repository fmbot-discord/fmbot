using System;
using System.Collections.Generic;

namespace FMBot.Bot.Models
{
    public class DailyOverview
    {
        public int Uniques { get; set; }

        public int Playcount { get; set; }

        public double AvgPerDay { get; set; }

        public List<DayOverview> Days { get; set; }
    }

    public class DayOverview
    {
        public DateTime Date { get; set; }

        public int Playcount { get; set; }

        public string TopArtist { get; set; }

        public string TopTrack { get; set; }

        public string TopAlbum { get; set; }

        public List<string> TopGenres { get; set; }
    }
}
