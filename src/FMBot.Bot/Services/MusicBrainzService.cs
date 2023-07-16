using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using Serilog;

namespace FMBot.Bot.Services;

public class MusicBrainzService
{
    private readonly HttpClient _httpClient;

    public MusicBrainzService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    public async Task<ArtistUpdated> AddMusicBrainzDataToArtistAsync(Artist artist)
    {
        try
        {
            var updated = false;

            if (artist.MusicBrainzDate.HasValue && artist.MusicBrainzDate > DateTime.UtcNow.AddDays(-120))
            {
                return new ArtistUpdated(artist);
            }

            var api = new Query(this._httpClient);


            var musicBrainzResults = await api.FindArtistsAsync(artist.Name, simple: true);
            Statistics.MusicBrainzApiCalls.Inc();

            var musicBrainzArtist =
                musicBrainzResults.Results
                    .OrderByDescending(o => o.Score)
                    .Select(s => s.Item).FirstOrDefault(f => f.Name?.ToLower() == artist.Name.ToLower());

            if (musicBrainzArtist != null)
            {
                musicBrainzArtist = await api.LookupArtistAsync(musicBrainzArtist.Id);
                Statistics.MusicBrainzApiCalls.Inc();

                var startDate = musicBrainzArtist.LifeSpan?.Begin?.NearestDate;
                var endDate = musicBrainzArtist.LifeSpan?.End?.NearestDate;

                artist.MusicBrainzDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                artist.Location = musicBrainzArtist.Area?.Name;
                artist.CountryCode = GetArtistCountryCode(musicBrainzArtist);
                artist.Type = musicBrainzArtist.Type;
                artist.Disambiguation = musicBrainzArtist.Disambiguation;
                artist.Gender = musicBrainzArtist.Gender;
                artist.StartDate = startDate.HasValue ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) : null;
                artist.EndDate = endDate.HasValue ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) : null;
                artist.Mbid = musicBrainzArtist.Id;

                updated = true;
            }

            return new ArtistUpdated(artist, updated);
        }
        catch (Exception e)
        {
            Log.Error(e, "error in musicbrainzservice");
            return new ArtistUpdated(artist);
        }
    }

    private static string GetArtistCountryCode(IArtist musicBrainzArtist)
    {
        var country = musicBrainzArtist.Country
                      ?? musicBrainzArtist.Area?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.Area?.Iso31661Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31661Codes?.FirstOrDefault();

        if (country == null)
        {
            return null;
        }

        return country.Contains('-') ? country.Split("-").First() : country;
    }

    public record ArtistUpdated(Artist Artist, bool Updated = false);
}
