using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using Genius;
using Genius.Models;

namespace FMBot.Bot.Services.ThirdParty
{
    public class GeniusService
    {
        public async Task<List<SearchHit>> SearchGeniusAsync(string searchValue)
        {
            var client = new GeniusClient(ConfigData.Data.Genius.AccessToken);

            var result = await client.SearchClient.Search(searchValue);

            if (result.Response?.Hits == null || !result.Response.Hits.Any())
            {
                return null;
            }

            return result.Response.Hits.OrderByDescending(o => o.Result.PyongsCount).ToList();
        }
    }
}
