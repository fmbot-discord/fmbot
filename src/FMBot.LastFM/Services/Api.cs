using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using FMBot.LastFM.Models;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Newtonsoft.Json.Schema.Generation;

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

        public async Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"api_key", this._key },
                {"api_secret", this._secret },
                {"format", "json" },
                {"method", call }
            };

            foreach (var (key, value) in parameters)
            {
                queryParams.Add(key, value);
            }

            var url = QueryHelpers.AddQueryString(apiUrl, queryParams);

            var httpResponse = await this._client.GetAsync(url);

            if (!httpResponse.IsSuccessStatusCode)
            {
                return new Response<T>
                {
                    Success = false,
                    Error = LastResponseStatus.Unknown
                };
            }

            var content = await httpResponse.Content.ReadAsStringAsync();

            var parsedContent = JObject.Parse(content);

            var schemaGenerator = new JSchemaGenerator();
            var errorSchema = schemaGenerator.Generate(typeof(ErrorResponse));
            if (parsedContent.IsValid(errorSchema))
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(content);
                return new Response<T>
                {
                    Success = false,
                    Error = errorResponse.Error,
                    Message = errorResponse.Message
                };
            }

            var parsedObject = JsonConvert.DeserializeObject<T>(content);
            return new Response<T>
            {
                Success = true,
                Content = parsedObject
            };
        }
    }
}
