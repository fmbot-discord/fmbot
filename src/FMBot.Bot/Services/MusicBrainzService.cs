using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;
using MetaBrainz.MusicBrainz;
using Serilog;

namespace FMBot.Bot.Services
{
    public class MusicBrainzService
    {
        public static async Task<ArtistUpdated> AddMusicBrainzDataToArtistAsync(Artist artist)
        {
            try
            {
                var updated = false;
                var api = new Query("fmbot-discord", "1.0", "https://fmbot.xyz");

                if (artist.MusicBrainzDate.HasValue && artist.MusicBrainzDate > DateTime.UtcNow.AddDays(-90))
                {
                    return new ArtistUpdated(artist);
                }

                if (artist.Mbid.HasValue)
                {
                    var musicBrainzArtist = await api.LookupArtistAsync(artist.Mbid.Value);

                    if (musicBrainzArtist.Name != null)
                    {
                        artist.MusicBrainzDate = DateTime.UtcNow;
                        artist.Country = musicBrainzArtist.Area?.Name;
                        artist.Type = musicBrainzArtist.Type;
                        artist.Disambiguation = musicBrainzArtist.Disambiguation;
                        artist.Gender = musicBrainzArtist.Gender;
                        artist.StartDate = musicBrainzArtist.LifeSpan?.Begin?.NearestDate;
                        artist.EndDate = musicBrainzArtist.LifeSpan?.End?.NearestDate;

                        updated = true;
                    }
                }
                else
                {
                    var musicBrainzResults = await api.FindArtistsAsync(artist.Name, simple: true);
                    var musicBrainzArtist =
                        musicBrainzResults.Results.Select(s => s.Item).FirstOrDefault(f => f.Name?.ToLower() == artist.Name.ToLower());

                    if (musicBrainzArtist != null)
                    {
                        artist.MusicBrainzDate = DateTime.UtcNow;
                        artist.Country = musicBrainzArtist.Area?.Name;
                        artist.Type = musicBrainzArtist.Type;
                        artist.Disambiguation = musicBrainzArtist.Disambiguation;
                        artist.Gender = musicBrainzArtist.Gender;
                        artist.StartDate = musicBrainzArtist.LifeSpan?.Begin?.NearestDate;
                        artist.EndDate = musicBrainzArtist.LifeSpan?.End?.NearestDate;

                        updated = true;
                    }
                }

                return new ArtistUpdated(artist, updated);
            }
            catch (Exception e)
            {
                Log.Error(e, "error in musicbrainzservice");
                return new ArtistUpdated(artist);
            }
        }

        public record ArtistUpdated(Artist Artist, bool Updated = false);
    }
}
