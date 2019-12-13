using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FMBot.LastFM.Models;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;

namespace FMBot.LastFM.Services
{
    public class Api
    {
        private readonly HttpClient _client = new HttpClient();

        private const string apiUrl = "http://ws.audioscrobbler.com/2.0/";

        private readonly string _key;
        private readonly string _secret;

        public Api(string key, string secret)
        {
            this._key = key;
            this._secret = secret;
        }

        public async Task<T> CallApiAsync<T>(Dictionary<string, string> parameters, Call call)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"api_key", this._key },
                {"api_secret", this._secret },
                {"format", "json" },
                {"method", call.ToString() }
            };

            foreach (var parameter in parameters)
            {
                queryParams.Add(parameter.Key, parameter.Value);
            }

            var url = QueryHelpers.AddQueryString(apiUrl, queryParams);

            var httpResponse = await this._client.GetAsync(url);

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new Exception("Cannot retrieve tasks");
            }

            var content = await httpResponse.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<T>(content);
        }
    }
}
