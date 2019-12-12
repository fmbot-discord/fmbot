using System;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Services
{
    internal class LastFMService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            var tracks = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);
            GlobalVars.LastFMApiCalls++;

            return tracks.Content[0];
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            var recentScrobbles = await this._lastFMClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);
            GlobalVars.LastFMApiCalls++;

            return recentScrobbles;
        }


        public static string TrackToLinkedString(LastTrack track)
        {
            if (track.Url.ToString().IndexOfAny(new[] { '(', ')' }) >= 0)
            {
                return TrackToString(track);
            }

            return $"[{track.Name}]({track.Url})\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        public static string TrackToString(LastTrack track)
        {
            return $"{track.Name}\n" +
                   $"By **{track.ArtistName}**" +
                   (string.IsNullOrWhiteSpace(track.AlbumName)
                       ? "\n"
                       : $" | *{track.AlbumName}*\n");
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            var user = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            GlobalVars.LastFMApiCalls++;

            return user;
        }

        // Track info
        public async Task<LastResponse<LastTrack>> GetTrackInfoAsync(string trackName, string artistName, string username = null)
        {
            var trackInfo = await this._lastFMClient.Track.GetInfoAsync(trackName, artistName, username);
            GlobalVars.LastFMApiCalls++;

            return trackInfo;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName, string username = null)
        {
            var albumInfo = await this._lastFMClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return albumInfo;
        }

        // Album images
        public async Task<LastImageSet> GetAlbumImagesAsync(string artistName, string albumName)
        {
            var album = await this._lastFMClient.Album.GetInfoAsync(artistName, albumName);
            GlobalVars.LastFMApiCalls++;

            return album?.Content?.Images;
        }

        // Top albums
        public async Task<PageResponse<LastAlbum>> GetTopAlbumsAsync(string lastFMUserName, LastStatsTimeSpan timespan,
            int count = 2)
        {
            var topAlbums = await this._lastFMClient.User.GetTopAlbums(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topAlbums;
        }

        // Artist info
        public async Task<LastResponse<LastArtist>> GetArtistInfoAsync(string artistName, string userName = null)
        {
            var artistInfo = await this._lastFMClient.Artist.GetInfoAsync(artistName);
            GlobalVars.LastFMApiCalls++;

            return artistInfo;
        }

        // Artist info
        public async Task<LastImageSet> GetArtistImageAsync(string artistName)
        {
            var artist = await this._lastFMClient.Artist.GetInfoAsync(artistName);

            var artist2 = await this._lastFMClient.Artist.GetInfoByMbidAsync(artist.Content.Mbid);
            GlobalVars.LastFMApiCalls++;

            return artist2?.Content?.MainImage;
        }

        // Top artists
        public async Task<PageResponse<LastArtist>> GetTopArtistsAsync(string lastFMUserName,
            LastStatsTimeSpan timespan, int count = 2)
        {
            var topArtists = await this._lastFMClient.User.GetTopArtists(lastFMUserName, timespan, 1, count);
            GlobalVars.LastFMApiCalls++;

            return topArtists;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            var lastFMUser = await this._lastFMClient.User.GetInfoAsync(lastFMUserName);
            GlobalVars.LastFMApiCalls++;

            return lastFMUser.Success;
        }

        public LastStatsTimeSpan GetLastStatsTimeSpan(ChartTimePeriod timePeriod)
        {
            switch (timePeriod)
            {
                case ChartTimePeriod.Weekly:
                    return LastStatsTimeSpan.Week;
                case ChartTimePeriod.Monthly:
                    return LastStatsTimeSpan.Month;
                case ChartTimePeriod.Yearly:
                    return LastStatsTimeSpan.Year;
                case ChartTimePeriod.AllTime:
                    return LastStatsTimeSpan.Overall;
                default:
                    return LastStatsTimeSpan.Week;
            }
        }

        public LastStatsTimeSpan StringToLastStatsTimeSpan(string timespan)
        {
            if (timespan.Equals("monthly") || timespan.Equals("month") || timespan.Equals("m"))
            {
                return LastStatsTimeSpan.Month;
            }

            if (timespan.Equals("yearly") || timespan.Equals("year") || timespan.Equals("y"))
            {
                return LastStatsTimeSpan.Year;
            }

            if (timespan.Equals("overall") || timespan.Equals("alltime") || timespan.Equals("o") ||
                timespan.Equals("at") || timespan.Equals("a"))
            {
                return LastStatsTimeSpan.Overall;
            }

            return LastStatsTimeSpan.Week;
        }
    }
}
