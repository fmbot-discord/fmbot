using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using Genius;
using Genius.Models;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json.Linq;

namespace FMBot.Bot.Services
{
    internal class GeniusService
    {
        public async Task<SearchHit> SearchGeniusAsync(string searchValue)
        {
            var client = new GeniusClient(ConfigData.Data.Genius.AccessToken);

            var result = await client.SearchClient.Search(searchValue);

            if (!result.Response.Hits.Any())
            {
                return null;
            }

            return result.Response.Hits[0];
        }
    }
}
