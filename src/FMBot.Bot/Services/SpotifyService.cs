using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.LastFM.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using Artist = FMBot.Persistence.Domain.Models.Artist;

namespace FMBot.Bot.Services
{
    internal class SpotifyService
    {
        public async Task<SearchItem> GetSearchResultAsync(string searchValue, SearchType searchType = SearchType.Track)
        {
            var spotify = await GetSpotifyWebApi();

            return await spotify.SearchItemsAsync(searchValue, searchType);
        }

        

        public async Task<string> GetOrStoreArtistImageAsync(ArtistResponse lastFmArtist, string artistNameBeforeCorrect)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var dbArtist = await db.Artists
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == lastFmArtist.Artist.Name.ToLower());

            var imageUrlToReturn = "";
            try
            {
                if (dbArtist == null)
                {
                    var spotifyArtist = await GetArtistFromSpotify(lastFmArtist.Artist.Name);

                    var artistToAdd = new Artist
                    {
                        Name = lastFmArtist.Artist.Name,
                        LastFmUrl = lastFmArtist.Artist.Url,
                        Mbid = lastFmArtist.Artist.Mbid
                    };

                    if (spotifyArtist != null)
                    {
                        artistToAdd.SpotifyId = spotifyArtist.Id;

                        if (spotifyArtist.Images.Any())
                        {
                            artistToAdd.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                            artistToAdd.SpotifyImageDate = DateTime.UtcNow;
                            imageUrlToReturn = artistToAdd.SpotifyImageUrl;
                        }
                    }

                    if (!string.Equals(artistNameBeforeCorrect, lastFmArtist.Artist.Name, StringComparison.CurrentCultureIgnoreCase))
                    {
                        artistToAdd.Aliases = new[] { artistNameBeforeCorrect };
                    }

                    await db.Artists.AddAsync(artistToAdd);
                    await db.SaveChangesAsync();
                }
                else
                {
                    if (!string.Equals(artistNameBeforeCorrect, lastFmArtist.Artist.Name, StringComparison.CurrentCultureIgnoreCase))
                    {
                        AddAliasToExistingArtist(artistNameBeforeCorrect, dbArtist);

                        db.Entry(dbArtist).State = EntityState.Modified;
                    }

                    if (dbArtist.SpotifyImageUrl == null || dbArtist.SpotifyImageDate < DateTime.UtcNow.AddMonths(-2))
                    {
                        var spotifyArtist = await GetArtistFromSpotify(lastFmArtist.Artist.Name);

                        if (spotifyArtist != null && spotifyArtist.Images.Any())
                        {
                            dbArtist.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                            imageUrlToReturn = dbArtist.SpotifyImageUrl;
                        }

                        dbArtist.SpotifyImageDate = DateTime.UtcNow;
                        db.Entry(dbArtist).State = EntityState.Modified;
                    }
                    else
                    {
                        imageUrlToReturn = dbArtist.SpotifyImageUrl;
                    }

                    await db.SaveChangesAsync();
                }

                return !string.IsNullOrEmpty(imageUrlToReturn) ? imageUrlToReturn : null;
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while retrieving artist image");
                return null;
            }
        }

        private static void AddAliasToExistingArtist(string artistNameBeforeCorrect, Artist dbArtist)
        {
            if (dbArtist.Aliases != null && dbArtist.Aliases.Length > 0 && !dbArtist.Aliases.Contains(artistNameBeforeCorrect))
            {
                var aliases = dbArtist.Aliases;
                Array.Resize(ref aliases, aliases.Length + 1);
                aliases[^1] = artistNameBeforeCorrect;
                dbArtist.Aliases = aliases;
            }
            else
            {
                dbArtist.Aliases = new[] {artistNameBeforeCorrect};
            }
        }

        private static async Task<FullArtist> GetArtistFromSpotify(string artistName)
        {
            var spotify = await GetSpotifyWebApi();

            var results = await spotify.SearchItemsAsync(artistName, SearchType.Artist);

            if (results.Artists?.Items?.Any() == true)
            {
                var spotifyArtist = results.Artists.Items
                    .OrderByDescending(o => o.Popularity)
                    .FirstOrDefault(w => w.Name.ToLower() == artistName.ToLower());

                if (spotifyArtist != null && !spotifyArtist.HasError())
                {
                    return spotifyArtist;
                }
            }

            return null;
        }

        public async Task<Persistence.Domain.Models.Track> GetOrStoreTrackAsync(ResponseTrack track)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var dbTrack = await db.Tracks
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == track.Name.ToLower() && f.ArtistName.ToLower() == track.Artist.Name.ToLower());

            if (dbTrack == null)
            {
                var trackToAdd = new FMBot.Persistence.Domain.Models.Track
                {
                    Name = track.Name,
                    AlbumName = track.Album?.Title,
                    ArtistName = track.Artist?.Name
                };

                var artist = await db.Artists
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.Name.ToLower() == track.Artist.Name.ToLower());

                if (artist != null)
                {
                    trackToAdd.Artist = artist;
                }

                var spotifyTrack = await GetTrackFromSpotify(track.Name, track.Artist.Name.ToLower());

                if (spotifyTrack != null)
                {
                    trackToAdd.SpotifyId = spotifyTrack.Id;
                    trackToAdd.DurationMs = spotifyTrack.DurationMs;

                    var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                    if (audioFeatures != null)
                    {
                        trackToAdd.Key = audioFeatures.Key;
                        trackToAdd.Tempo = audioFeatures.Tempo;
                    }
                }

                trackToAdd.SpotifyLastUpdated = DateTime.UtcNow;

                await db.Tracks.AddAsync(trackToAdd);
                await db.SaveChangesAsync();

                return trackToAdd;
            }
            else
            {
                if (dbTrack.Artist == null)
                {
                    var artist = await db.Artists
                        .AsQueryable()
                        .FirstOrDefaultAsync(f => f.Name.ToLower() == track.Artist.Name.ToLower());

                    if (artist != null)
                    {
                        dbTrack.Artist = artist;
                        db.Entry(dbTrack).State = EntityState.Modified;
                    }
                }
                if (string.IsNullOrEmpty(dbTrack.SpotifyId) && dbTrack.SpotifyLastUpdated < DateTime.UtcNow.AddMonths(-2))
                {
                    var spotifyTrack = await GetTrackFromSpotify(track.Name, track.Artist.Name.ToLower());

                    if (spotifyTrack != null)
                    {
                        dbTrack.SpotifyId = spotifyTrack.Id;
                        dbTrack.DurationMs = spotifyTrack.DurationMs;

                        var audioFeatures = await GetAudioFeaturesFromSpotify(spotifyTrack.Id);

                        if (audioFeatures != null)
                        {
                            dbTrack.Key = audioFeatures.Key;
                            dbTrack.Tempo = audioFeatures.Tempo;
                        }
                    }

                    dbTrack.SpotifyLastUpdated = DateTime.UtcNow;
                    db.Entry(dbTrack).State = EntityState.Modified;
                }

                await db.SaveChangesAsync();

                return dbTrack;
            }
        }

        private static async Task<FullTrack> GetTrackFromSpotify(string trackName, string artistName)
        {
            //Create the auth object
            var spotify = await GetSpotifyWebApi();

            var results = await spotify.SearchItemsAsync($"{trackName} {artistName}", SearchType.Track);

            if (results.Tracks?.Items?.Any() == true)
            {
                var spotifyTrack = results.Tracks.Items
                    .OrderByDescending(o => o.Popularity)
                    .FirstOrDefault(w => w.Name.ToLower() == trackName.ToLower() && w.Artists.Select(s => s.Name.ToLower()).Contains(artistName.ToLower()));

                if (spotifyTrack != null && !spotifyTrack.HasError())
                {
                    return spotifyTrack;
                }
            }

            return null;
        }

        private static async Task<AudioFeatures> GetAudioFeaturesFromSpotify(string spotifyId)
        {
            //Create the auth object
            var spotify = await GetSpotifyWebApi();

            var result = await spotify.GetAudioFeaturesAsync(spotifyId);

            if (!result.HasError())
            {
                return result;
            }

            return null;
        }

        private static async Task<SpotifyWebAPI> GetSpotifyWebApi()
        {
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            return new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };
    }
    }
}
