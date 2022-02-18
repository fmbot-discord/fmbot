using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class TopCountry
{
    public string CountryName { get; set; }

    public string CountryCode { get; set; }

    public long? UserPlaycount { get; set; }

    public List<TopArtist> Artists { get; set; }
}
