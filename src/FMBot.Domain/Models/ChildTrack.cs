using System;
using System.Collections.Generic;
using System.Text;

namespace FMBot.Domain.Models
{
    public partial class ChildTrack
    {
        public string Name { get; set; }
        public Uri Url { get; set; }
        public long Duration { get; set; }
        public ChildArtist Artist { get; set; }
    }
}
