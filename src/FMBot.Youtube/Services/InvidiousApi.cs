using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Youtube.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FMBot.Youtube.Services
{
    public class InvidiousApi
    {
        private readonly HttpClient _client;

        // https://instances.invidio.us/
        // https://vid.puffyan.us/api/v1/search?q=linus
        // https://vid.puffyan.us/api/v1/videos/n3XTZde8ZvQ?fields=videoId,title,description,publishedText,viewCount,likeCount,isFamilyFriendly,subCountText
        private readonly string _url;
        private readonly string _backupUrl;

        public InvidiousApi(IConfiguration configuration, HttpClient httpClient)
        {
            this._url = configuration.GetSection("Google:InvidiousUrl").Value;
            this._backupUrl = configuration.GetSection("Google:InvidiousBackupUrl").Value;
            this._client = httpClient;
        }

        public async Task<InvidiousSearchResult> SearchVideoAsync(string searchQuery)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"q", searchQuery },
            };

            var url = this._url + "api/v1/search";
            url = QueryHelpers.AddQueryString(url, queryParams);

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Get
            };
            
            try
            {
                using var httpResponse = await this._client.SendAsync(request);

                if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                var stream = await httpResponse.Content.ReadAsStreamAsync();
                using var streamReader = new StreamReader(stream);
                var requestBody = await streamReader.ReadToEndAsync();

                if (!httpResponse.IsSuccessStatusCode && this._backupUrl != null)
                {
                    var backupUrl = this._backupUrl + "api/v1/search";
                    backupUrl = QueryHelpers.AddQueryString(backupUrl, queryParams);

                    var backupRequest = new HttpRequestMessage
                    {
                        RequestUri = new Uri(backupUrl),
                        Method = HttpMethod.Get
                    };

                    using var backupHttpResponse = await this._client.SendAsync(backupRequest);

                    var backupStream = await backupHttpResponse.Content.ReadAsStreamAsync();
                    using var backupStreamReader = new StreamReader(backupStream);
                    requestBody = await backupStreamReader.ReadToEndAsync();
                }

                var jsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<List<InvidiousSearchResult>>(requestBody, jsonSerializerOptions);

                if (result != null && result.Any(a => a.Type == "video"))
                {
                    return result.First(f => f.Type == "video");
                }

                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Something went wrong while deserializing the stuff from the Invidious API", ex);
                return null;
            }
        }

        public async Task<InvidiousVideoResult> GetVideoAsync(string videoId)
        {
            var query =
                $"api/v1/videos/{videoId}?fields=videoId,title,description,publishedText,viewCount,likeCount,isFamilyFriendly,subCountText";

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(this._url + query),
                Method = HttpMethod.Get
            };

            try
            {
                using var httpResponse = await this._client.SendAsync(request);
                
                var stream = await httpResponse.Content.ReadAsStreamAsync();
                using var streamReader = new StreamReader(stream);
                var requestBody = await streamReader.ReadToEndAsync();
                
                if (!httpResponse.IsSuccessStatusCode && this._backupUrl != null)
                {
                    var backupRequest = new HttpRequestMessage
                    {
                        RequestUri = new Uri(this._backupUrl + query),
                        Method = HttpMethod.Get
                    };

                    using var backupHttpResponse = await this._client.SendAsync(backupRequest);

                    var backupStream = await backupHttpResponse.Content.ReadAsStreamAsync();
                    using var backupStreamReader = new StreamReader(backupStream);
                    requestBody = await backupStreamReader.ReadToEndAsync();
                }

                var jsonSerializerOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                return JsonSerializer.Deserialize<InvidiousVideoResult>(requestBody, jsonSerializerOptions);
            }
            catch (Exception ex)
            {
                Log.Error("Something went wrong while deserializing the stuff from the Invidious API (video)", ex);
                return null;
            }
        }
    }
}
