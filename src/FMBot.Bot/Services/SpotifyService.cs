using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.LastFM.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
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
            var artist = await db.Artists
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.Name.ToLower() == lastFmArtist.Artist.Name.ToLower());

            var imageUrlToReturn = "";
            if (artist == null)
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
                    if (artist.Aliases != null && artist.Aliases.Length > 0)
                    {
                        var aliases = artist.Aliases;
                        Array.Resize(ref aliases, aliases.Length + 1);
                        aliases[^1] = artistNameBeforeCorrect;
                        artist.Aliases = aliases;
                    }
                    else
                    {
                        artist.Aliases = new[] { artistNameBeforeCorrect };
                    }

                    db.Entry(artist).State = EntityState.Modified;
                }

                if (artist.SpotifyImageUrl == null || artist.SpotifyImageDate > DateTime.UtcNow.AddMonths(-3))
                {
                    var spotifyArtist = await GetArtistFromSpotify(lastFmArtist.Artist.Name);

                    if (spotifyArtist.Images.Any())
                    {
                        artist.SpotifyImageUrl = spotifyArtist.Images.OrderByDescending(o => o.Height).First().Url;
                        artist.SpotifyImageDate = DateTime.UtcNow;
                        imageUrlToReturn = artist.SpotifyImageUrl;

                        db.Entry(artist).State = EntityState.Modified;
                    }
                }
                else
                {
                    imageUrlToReturn = artist.SpotifyImageUrl;
                }

                await db.SaveChangesAsync();
            }

            return !string.IsNullOrEmpty(imageUrlToReturn) ? imageUrlToReturn : null;
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
                var spotifyArtist = results.Artists.Items.First();

                if (spotifyArtist.Name.ToLower() == artistName.ToLower())
                {
                    return spotifyArtist;
                }
            }

            return null;
        }
    }
}
