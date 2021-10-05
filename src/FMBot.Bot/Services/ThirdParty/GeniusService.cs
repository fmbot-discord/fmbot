using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using Genius;
using Genius.Models;
using Genius.Models.Song;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Services.ThirdParty
{
    public class GeniusService
    {
        private readonly BotSettings _botSettings;

        public GeniusService(IOptions<BotSettings> botSettings)
        {
            this._botSettings = botSettings.Value;
        }

        public async Task<List<SearchHit>> SearchGeniusAsync(string searchValue)
        {
            var client = new GeniusClient(this._botSettings.Genius.AccessToken);

            var result = await client.SearchClient.Search(searchValue);

            if (result.Response?.Hits == null || !result.Response.Hits.Any())
            {
                return null;
            }

            return result.Response.Hits
                .Where(w => w.Result.PrimaryArtist.Name.ToLower() != "Spotify")
                .OrderByDescending(o => o.Result.PyongsCount).ToList();
        }

        public async Task<Song> GetSong(ulong id)
        {
            var client = new GeniusClient(this._botSettings.Genius.AccessToken);

            var result = await client.SongClient.GetSong(id);

            if (result.Response?.Song == null)
            {
                return null;
            }

            return result.Response.Song;
        }
    }
}
