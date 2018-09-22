using Discord;
using FMBot.Data.Entities;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System.Linq;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    class LastFMService
    {
        public static JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        public LastfmClient lastfmClient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);


        // Last scrobble
        public async Task<LastTrack> GetLastScrobbleAsync(string lastFMUserName)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, 1);

            LastTrack track = tracks.Content.ElementAt(0);

            return track;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);

            return tracks;
        }

        // User
        public async Task<LastResponse<LastUser>> GetUserInfoAsync(string lastFMUserName)
        {
            LastResponse<LastUser> userInfo = await lastfmClient.User.GetInfoAsync(lastFMUserName);

            return userInfo;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> GetAlbumInfoAsync(string artistName, string albumName)
        {
            LastResponse<LastAlbum> album = await lastfmClient.Album.GetInfoAsync(artistName, albumName);

            return album;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            LastResponse<LastUser> lastFMUser = await lastfmClient.User.GetInfoAsync(lastFMUserName);

            return lastFMUser.Success;
        }
    }
}
