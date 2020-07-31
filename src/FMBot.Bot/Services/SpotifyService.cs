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
            //Create the auth object
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

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
            //Create the auth object
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

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

        public async Task<AudioFeatures> GetOrStoreTrackAsync(Track track)
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
                }



            }

            return await spotify.GetAudioFeaturesAsync("sadas");
        }

        private static async Task<FullTrack> GetTrackFromSpotify(string trackName, string artistName)
        {
            //Create the auth object
            var auth = new CredentialsAuth(ConfigData.Data.Spotify.Key, ConfigData.Data.Spotify.Secret);

            var token = await auth.GetToken();

            var spotify = new SpotifyWebAPI
            {
                TokenType = token.TokenType,
                AccessToken = token.AccessToken,
                UseAuth = true
            };

            var results = await spotify.SearchItemsAsync($"{trackName} {artistName}", SearchType.Track);

            if (results.Tracks?.Items?.Any() == true)
            {
                var spotifyTrack = results.Tracks.Items
                    .OrderByDescending(o => o.Popularity)
                    .FirstOrDefault(w => w.Name.ToLower() == trackName.ToLower() && w.Artists.Select(s => s.Name).Contains(artistName));

                if (spotifyTrack != null && !spotifyTrack.HasError())
                {
                    return spotifyTrack;
                }
            }

            return null;
        }
    }
}
