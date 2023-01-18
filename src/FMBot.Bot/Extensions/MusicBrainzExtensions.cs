using System.Linq;
using MetaBrainz.MusicBrainz.Interfaces.Entities;

namespace FMBot.Bot.Extensions;

public static class MusicBrainzExtensions
{
    #nullable enable
    public static string? GetCountryCode(
        this IArtist musicBrainzArtist)
    {
        var country = musicBrainzArtist.Country
                      ?? musicBrainzArtist.Area?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.Area?.Iso31661Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31661Codes?.FirstOrDefault();

        if (country == null) return null;
        if (country.Contains('-')) return country.Split("-").First();

        return country;
    }
}