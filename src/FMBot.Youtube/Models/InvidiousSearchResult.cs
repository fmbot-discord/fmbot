using System.Collections.Generic;

namespace FMBot.Youtube.Models
{
    public class InvidiousSearchResultList
    {
        public List<InvidiousSearchResult> Results { get; set; }
    }

    public class InvidiousSearchResult
    {
        public string Type { get; set; }

        public string Title { get; set; }

        public string VideoId { get; set; }

        public string Author { get; set; }

        public string AuthorId { get; set; }

        public string AuthorUrl { get; set; }

        public string Description { get; set; }

        public string DescriptionHtml { get; set; }

        public long ViewCount { get; set; }

        public long Published { get; set; }

        public string PublishedText { get; set; }

        public long LengthSeconds { get; set; }

        public bool LiveNow { get; set; }

        public bool Paid { get; set; }

        public bool Premium { get; set; }

        public bool IsUpcoming { get; set; }
    }
}
