using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.LastFM.Converters;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FMBot.LastFM.Services
{
    public class LastfmApi : ILastfmApi
    {
        private const string apiUrl = "http://ws.audioscrobbler.com/2.0/";

        private readonly HttpClient _client;

        private readonly string _key;
        private readonly string _secret;

        public LastfmApi(IConfigurationRoot configuration, IHttpClientFactory httpClientFactory)
        {
            this._key = configuration.GetSection("LastFm:Key").Value;
            this._secret = configuration.GetSection("LastFm:Secret").Value;
            this._client = httpClientFactory.CreateClient();
        }

        public async Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call, bool generateSignature = false)
        {
            var queryParams = new Dictionary<string, string>
            {
                {"api_key", this._key },
                {"format", "json" },
                {"method", call }
            };

            foreach (var (key, value) in queryParams
                .OrderBy(o => o.Key)
                .Where(w => !parameters.ContainsKey(w.Key.ToLower())))
            {
                parameters.Add(key, value);
            }

            if (generateSignature)
            {
                var signature = new StringBuilder();

                foreach (var (key, value) in parameters.OrderBy(o => o.Key).Where(w => !w.Key.Contains("format")))
                {
                    signature.Append(key);
                    signature.Append(value);
                }

                signature.Append(this._secret);
                parameters.Add("api_sig", CreateMd5(signature.ToString()));
                
                Statistics.LastfmAuthorizedApiCalls.Inc();
            }
            else
            {
                Statistics.LastfmApiCalls.Inc();
            }

            var url = QueryHelpers.AddQueryString(apiUrl, parameters);

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Post
            };

            using var httpResponse = await this._client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new Response<T>
                {
                    Success = false,
                    Error = ResponseStatus.MissingParameters,
                    Message = "Not found"
                };
            }

            var response = new Response<T>();

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            jsonSerializerOptions.Converters.Add(new LongToStringConverter());
            jsonSerializerOptions.Converters.Add(new GuidConverter());
            jsonSerializerOptions.Converters.Add(new BooleanConverter());

            var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var streamReader = new StreamReader(stream);
            var requestBody = await streamReader.ReadToEndAsync();

            try
            {
                // Check for error response first since last.fm returns 200 ok even if something isn't found
                var errorResponse = CheckForError(response, requestBody, jsonSerializerOptions);

                if (errorResponse.Message == null)
                {
                    var deserializeObject = JsonSerializer.Deserialize<T>(requestBody, jsonSerializerOptions);
                    response.Content = deserializeObject;
                    response.Success = true;
                }
                else
                {
                    response.Success = false;
                    response.Message = errorResponse.Message;
                    response.Error = errorResponse.Error;
                }
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Something went wrong while deserializing the object last.fm returned";
                Console.WriteLine(ex);
                Log.Error("Something went wrong while deserializing the object Last.fm returned:", ex);
            }

            return response;
        }

        private static Response<T> CheckForError<T>(Response<T> response, string requestBody,
            JsonSerializerOptions jsonSerializerOptions)
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(requestBody, jsonSerializerOptions);

                if (errorResponse != null)
                {
                    response.Error = errorResponse.Error;
                    response.Message = errorResponse.Message;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return response;
        }

        private static string CreateMd5(string input)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);

            var sb = new StringBuilder();
            foreach (var t in hashBytes)
            {
                sb.Append(t.ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
