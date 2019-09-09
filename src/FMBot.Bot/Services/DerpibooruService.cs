using System.Collections.Generic;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Services
{
    internal class DerpibooruService
    {
        public static JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();

        // Last scrobble
        public async Task<List<string>> GetImages(string query)
        {
            // temporary disabled due to .net core 
            //CoolSearchQuery sq = new CoolSearchQuery();

            //sq.SortFormat = CoolStuff.SORT_RANDOM;
            //sq.APIKey = cfgjson.DerpiKey;
            //sq.Query = query;
            //var s = await CoolStuff.Search(sq);
            //var res = s.search;

            return null;
        }
    }
}
