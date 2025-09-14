using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Types;
using FMBot.LastFM.Converters;
using FMBot.LastFM.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Prometheus;
using Serilog;

namespace FMBot.LastFM.Api;

public class LastfmApi : ILastfmApi
{
    private const string ApiUrl = "https://ws.audioscrobbler.com/2.0/";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new TagsConverter(),
            new TrackConverter(),
            new LongToStringConverter(),
            new GuidConverter(),
            new BooleanConverter()
        }
    };

    private readonly HttpClient _client;

    private readonly string _privateKey;
    private readonly string _publicKey;
    private readonly string _publicKeySecret;

    public LastfmApi(IConfiguration configuration, HttpClient httpClient)
    {
        // Use a public key and a private key for talking to the Last.fm API
        // This is because the public key is visible when a user authenticates
        // This has been abused for triggering our ratelimit, which is why we use this setup
        this._privateKey = configuration.GetSection("LastFm:PrivateKey").Value;
        this._publicKey = configuration.GetSection("LastFm:PublicKey").Value;
        this._publicKeySecret = configuration.GetSection("LastFm:PublicKeySecret").Value;
        this._client = httpClient;
    }

    public async Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call,
        bool generateSignature = false, bool usePrivateKey = false)
    {
        var queryParams = new Dictionary<string, string>
        {
            { "api_key", generateSignature || usePrivateKey ? this._publicKey : this._privateKey },
            { "format", "json" },
            { "method", call }
        };

        foreach (var (key, value) in queryParams
                     .OrderBy(o => o.Key)
                     .Where(w => !parameters.ContainsKey(w.Key.ToLower())))
        {
            parameters.Add(key, value);
        }

        if (generateSignature)
        {
            parameters.Remove("api_sig");

            var signature = new StringBuilder();

            foreach (var (key, value) in parameters.OrderBy(o => o.Key).Where(w => !w.Key.Contains("format")))
            {
                signature.Append(key);
                signature.Append(value);
            }

            signature.Append(this._publicKeySecret);
            parameters.Add("api_sig", CreateMd5(signature.ToString()));

            Statistics.LastfmAuthorizedApiCalls.WithLabels(call).Inc();
        }
        else
        {
            Statistics.LastfmApiCalls.WithLabels(call).Inc();
        }

        var url = QueryHelpers.AddQueryString(ApiUrl, parameters);

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(url),
            Method = HttpMethod.Post
        };

        var timer = Statistics.LastfmApiResponseTime.WithLabels(call).NewTimer();
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
        var requestBody = await httpResponse.Content.ReadAsStringAsync();

        try
        {
            // Check for error response first since last.fm returns 200 ok even if something isn't found
            var errorResponse = CheckForError(response, requestBody, JsonOptions);

            if (errorResponse.Message == null)
            {
                var deserializeObject = JsonSerializer.Deserialize<T>(requestBody, JsonOptions);
                response.Content = deserializeObject;
                response.Success = true;
            }
            else
            {
                response.Success = false;
                response.Message = errorResponse.Message;
                response.Error = errorResponse.Error;
                Statistics.LastfmErrors.Inc();

                if (response.Error == ResponseStatus.Failure)
                {
                    Statistics.LastfmFailureErrors.Inc();
                }

                if (response.Error == ResponseStatus.BadAuth)
                {
                    Statistics.LastfmBadAuthErrors.Inc();
                }
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message =
                "Something went wrong while deserializing the object Last.fm returned. It could be that there are no results, or that they're having issues.";
            Log.Error(
                "Something went wrong while deserializing the object Last.fm returned - {request} - {requestBody}",
                call, requestBody, ex);

            var errorParameters = "";
            foreach (var parameter in parameters)
            {
                errorParameters += $"{parameter.Key}: {parameter.Value} - ";
            }

            Log.Error("Object error - Call: {call} - Parameters: {errorParameters} - RequestBody {requestBody}",
                call, errorParameters, requestBody);
            Statistics.LastfmErrors.Inc();
        }

        timer.Dispose();
        return response;
    }

    private static Response<T> CheckForError<T>(Response<T> response, string requestBody,
        JsonSerializerOptions jsonSerializerOptions)
    {
        try
        {
            if (requestBody.Contains(
                    "<h2>The server encountered a temporary error and could not complete your request.<p>Please try again in 30 seconds.</h2>",
                    StringComparison.OrdinalIgnoreCase))
            {
                response.Error = ResponseStatus.TemporaryError;
                response.Message =
                    "Last.fm said: The server encountered a temporary error and could not complete your request. Please try again in 30 seconds.";
                return response;
            }

            var errorResponse = JsonSerializer.Deserialize<ErrorResponseLfm>(requestBody, jsonSerializerOptions);

            if (errorResponse != null)
            {
                response.Error = errorResponse.Error;
                response.Message = errorResponse.Message;
            }
        }
        catch (Exception ex)
        {
            Log.Error("LastfmApi: Error while trying to check for error", ex);
        }

        return response;
    }

    private static string CreateMd5(string input)
    {
        var inputBytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

        var sb = new StringBuilder();
        foreach (var t in hashBytes)
        {
            sb.Append(t.ToString("X2"));
        }

        return sb.ToString();
    }
}
