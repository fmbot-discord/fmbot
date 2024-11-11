using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.Subscriptions.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using NAudio.Wave;
using Serilog;

namespace FMBot.Subscriptions.Services;

public class DiscordSkuService
{
    private readonly HttpClient _client;

    private readonly string _baseUrl;

    private readonly string _token;
    private readonly string _appId;

    public DiscordSkuService(IConfiguration configuration, HttpClient client)
    {
        this._token = configuration.GetSection("Discord:Token").Value;
        this._appId = configuration.GetSection("Discord:ApplicationId").Value;

        this._baseUrl = "https://discord.com/api/v10/";

        this._client = client;
    }

    public async Task<List<DiscordEntitlementResponseModel>> GetEntitlementsFromDiscord(ulong? discordUserId = null,
        ulong? after = null, ulong? before = null)
    {
        var fetchEntitlements = this._baseUrl + $"applications/{this._appId}/entitlements";

        var request = new HttpRequestMessage
        {
            RequestUri = new Uri(fetchEntitlements),
            Method = HttpMethod.Get
        };

        request.Headers.Add("Authorization", $"Bot {this._token}");

        try
        {
            if (discordUserId != null || before != null || after != null)
            {
                var queryParams = new Dictionary<string, string>();

                if (discordUserId != null)
                {
                    queryParams.Add("user_id", discordUserId.ToString());
                }

                if (after != null)
                {
                    queryParams.Add("after", after.ToString());
                }

                if (before != null)
                {
                    queryParams.Add("before", before.ToString());
                }

                request.RequestUri = new Uri(QueryHelpers.AddQueryString(fetchEntitlements, queryParams));
            }

            var result = await GetDiscordEntitlements(request);

            Log.Information("Found {entitlementsCount} Discord entitlements", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Something went wrong while deserializing Discord entitlements", ex);
            return null;
        }
    }

    public async Task<List<DiscordEntitlement>> GetEntitlements(ulong? discordUserId = null, ulong? after = null,
        ulong? before = null)
    {
        var discordEntitlements = await GetEntitlementsFromDiscord(discordUserId, after, before);

        return DiscordEntitlementsToGrouped(discordEntitlements);
    }

    public static List<DiscordEntitlement> DiscordEntitlementsToGrouped(
        IEnumerable<DiscordEntitlementResponseModel> discordEntitlements)
    {
        return discordEntitlements
            .Where(w => w.UserId.HasValue)
            .GroupBy(g => g.UserId.Value)
            .Select(DiscordEntitlement)
            .ToList();
    }

    private static DiscordEntitlement DiscordEntitlement(IGrouping<ulong, DiscordEntitlementResponseModel> s)
    {
        var noEndDate = s.Any(a => !a.EndsAt.HasValue);

        return new DiscordEntitlement
        {
            DiscordUserId = s.OrderByDescending(o => o.EndsAt).First().UserId.Value,
            Active = noEndDate
                ? true
                : !s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue ||
                  s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value > DateTime.UtcNow.AddDays(-2),
            StartsAt = s.OrderByDescending(o => o.EndsAt).First().StartsAt.HasValue
                ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().StartsAt.Value, DateTimeKind.Utc)
                : null,
            EndsAt = noEndDate
                ? null
                : s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue
                    ? DateTime.SpecifyKind(s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value, DateTimeKind.Utc)
                    : null
        };
    }

    async Task<List<DiscordEntitlementResponseModel>> GetDiscordEntitlements(HttpRequestMessage httpRequestMessage)
    {
        using var httpResponse = await this._client.SendAsync(httpRequestMessage);

        if (!httpResponse.IsSuccessStatusCode)
        {
            Log.Error("DiscordEntitlements: HTTP status code {statusCode} - {reason}", httpResponse.StatusCode,
                httpResponse.ReasonPhrase);
        }

        var stream = await httpResponse.Content.ReadAsStreamAsync();
        using var streamReader = new StreamReader(stream);
        var requestBody = await streamReader.ReadToEndAsync();

        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var result =
            JsonSerializer.Deserialize<List<DiscordEntitlementResponseModel>>(requestBody, jsonSerializerOptions);
        return result;
    }

    public async Task SendVoiceMessage(string audioUrl, string interactionToken)
    {
        var audioBytes = await _client.GetByteArrayAsync(audioUrl);

        var (waveform, durationSecs) = await GenerateWaveformFromAudioBytesAsync(audioBytes);

        using var multipartContent = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(audioBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
        multipartContent.Add(audioContent, "files[0]", "voice_message.mp3");

        var payload = new
        {
            flags = 8192, // IS_VOICE_MESSAGE flag
            attachments = new[]
            {
                new
                {
                    id = "0",
                    filename = "voice_message.mp3",
                    duration_secs = durationSecs,
                    waveform = waveform
                }
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"
        );
        multipartContent.Add(jsonContent, "payload_json");

        // Create and send request
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri($"{_baseUrl}webhooks/{this._appId}/{interactionToken}"),
            Method = HttpMethod.Post,
            Content = multipartContent
        };
        request.Headers.Add("Authorization", $"Bot {_token}");

        var response = await _client.SendAsync(request);

        var stream = await response.Content.ReadAsStreamAsync();
        using var streamReader = new StreamReader(stream);
        var requestBody = await streamReader.ReadToEndAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Information("error in music preview {requestBody}", requestBody);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<(string waveform, double durationSecs)> GenerateWaveformFromAudioBytesAsync(byte[] audioBytes)
    {
        var duration = 30;

        using var audioStream = new MemoryStream(audioBytes);
        using var pcmStream = new MemoryStream();

        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-i pipe:0 " + // Read from stdin
                            "-ac 1 " + // Convert to mono
                            "-ar 48000 " + // Sample rate
                            "-f s16le " + // 16-bit PCM
                            "pipe:1", // Output to stdout
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            ffmpegProcess.Start();

            var inputTask = audioStream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream)
                .ContinueWith(t => ffmpegProcess.StandardInput.Close());

            var outputTask = ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(pcmStream);

            var errorBuilder = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                while (await ffmpegProcess.StandardError.ReadLineAsync() is { } line)
                {
                    errorBuilder.AppendLine(line);
                }
            });

            await Task.WhenAll(inputTask, outputTask, errorTask);
            await ffmpegProcess.WaitForExitAsync();

            if (ffmpegProcess.ExitCode != 0)
            {
                throw new Exception(
                    $"FFmpeg failed with exit code: {ffmpegProcess.ExitCode}. Error output: {errorBuilder}");
            }

            // Process PCM data to generate waveform
            pcmStream.Position = 0;
            var waveformPoints = new byte[256];
            var samplesPerPoint = (int)(pcmStream.Length / (2 * 256)); // 2 bytes per sample

            using var reader = new BinaryReader(pcmStream);
            for (int i = 0; i < 256; i++)
            {
                float maxAmplitude = 0;
                for (int j = 0; j < samplesPerPoint && pcmStream.Position < pcmStream.Length; j++)
                {
                    // Read 16-bit sample and convert to float
                    var sample = reader.ReadInt16() / 32768f;
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                }

                waveformPoints[i] = (byte)Math.Min(255, Math.Floor(maxAmplitude * 255));
            }

            return (Convert.ToBase64String(waveformPoints), duration);
        }
        finally
        {
            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
            }

            ffmpegProcess.Dispose();
        }
    }
}
