using System;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using Genius;
using Genius.Models;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json.Linq;

namespace FMBot.Bot.Services
{
    internal class GeniusService
    {
        public async Task<string> GetUrlAsync(string searchValue)
        {
            //Create the auth object
            var client = new GeniusClient(ConfigData.Data.GeniusAccessToken);

            var result = await client.SearchClient.Search(TextFormat.Html, searchValue);

            if (!result.Response.Any())
            {
                return null;
            }

            var firstResult = result.Response[0].Result.ToString();

            return JObject.Parse(firstResult)["url"].ToString();
        }
    }
}
