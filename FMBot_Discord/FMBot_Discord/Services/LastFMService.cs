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

        public JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        // Recent scrobbles for user
        public async Task<PageResponse<LastTrack>> getRecentScrobblesForUserAsync(IUser discordUser)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            Data.Entities.Settings userSettings = await userService.GetUserSettingsAsync(discordUser);

            PageResponse<LastTrack> tracks = await client.User.GetRecentScrobbles(userSettings.UserNameLastFM, null, 1, 5);

            return tracks;
        }

        // Recent scrobbles
        public async Task<PageResponse<LastTrack>> getRecentScrobblesAsync(string lastFMUserName)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            PageResponse<LastTrack> tracks = await client.User.GetRecentScrobbles(lastFMUserName, null, 1, 5);

            return tracks;
        }


        // User for user
        public async Task<LastResponse<LastUser>> getUserInfoForUserAsync(IUser discordUser)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            Data.Entities.Settings userSettings = await userService.GetUserSettingsAsync(discordUser);

            LastResponse<LastUser> userInfo = await client.User.GetInfoAsync(userSettings.UserNameLastFM);

            return userInfo;
        }


        // User
        public async Task<LastResponse<LastUser>> getUserInfoAsync(string lastFMUserName)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            LastResponse<LastUser> userInfo = await client.User.GetInfoAsync(lastFMUserName);

            return userInfo;
        }

        // Album info
        public async Task<LastResponse<LastAlbum>> getAlbumInfoAsync(string artistName, string albumName)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            LastResponse<LastAlbum> album = await client.Album.GetInfoAsync(artistName, albumName);

            return album;
        }

        // Check if lastfm user exists
        public async Task<bool> LastFMUserExistsAsync(string lastFMUserName)
        {
            LastfmClient client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

            LastResponse<LastUser> lastFMUser = await client.User.GetInfoAsync(lastFMUserName);

            return lastFMUser.Success;
        }
    }
}
