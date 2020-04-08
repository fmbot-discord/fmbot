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
        public async Task<SongResult> GetUrlAsync(string searchValue)
        {
            var client = new GeniusClient(ConfigData.Data.GeniusAccessToken);

            var result = await client.SearchClient.Search(TextFormat.Dom, searchValue);

            if (!result.Response.Any())
            {
                return null;
            }

            var songObject = result.Response[0].Result as JObject;

            return songObject?.ToObject<SongResult>();
        }
    }
}
