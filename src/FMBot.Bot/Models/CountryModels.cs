using System.Collections.Generic;

namespace FMBot.Bot.Models;

public class ArtistCountryDto
{
    public string CountryCode { get; set; }

    public string ArtistName { get; set; }
}

public class WindowsTimeZone
{
    public string Id { get; set; }
    public string Name { get; set; }
}

public class CountryInfo
{
    public string IsoAlpha3 { get; set; }
    public List<string> TimeZones { get; set; }
    public List<WindowsTimeZone> WindowsTimeZones { get; set; }
    public string CountryName { get; set; }
    public string IsoAlpha2 { get; set; }
}
