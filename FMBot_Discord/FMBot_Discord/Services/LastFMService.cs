using Discord;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    class LastFMService
    {
        UserService userService = new UserService();

        public static JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        public LastfmClient lastfmClient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

        // Recent scrobbles for user
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesForUserAsync(IUser discordUser, int count = 2)
        {
            Data.Entities.Settings userSettings = await userService.GetUserSettingsAsync(discordUser);

            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(userSettings.UserNameLastFM, null, 1, count);

            return tracks;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> GetRecentScrobblesAsync(string lastFMUserName, int count = 2)
        {
            PageResponse<LastTrack> tracks = await lastfmClient.User.GetRecentScrobbles(lastFMUserName, null, 1, count);

            return tracks;
        }


        // User for user
        public async Task<LastResponse<LastUser>> GetUserInfoForUserAsync(IUser discordUser)
        {
            Data.Entities.Settings userSettings = await userService.GetUserSettingsAsync(discordUser);

            LastResponse<LastUser> userInfo = await lastfmClient.User.GetInfoAsync(userSettings.UserNameLastFM);

            return userInfo;
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
