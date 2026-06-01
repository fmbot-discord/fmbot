using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FMBot.Subscriptions.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
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

    public async Task<List<DiscordEntitlementResponseModel>> GetRawEntitlementsFromDiscord(ulong? discordUserId = null,
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
            var queryParams = new Dictionary<string, string>();

            queryParams.Add("exclude_deleted", "false");

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

            var result = await GetDiscordEntitlements(request);

            Log.Information("Found {entitlementsCount} Discord entitlements", result.Count);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Something went wrong while deserializing Discord entitlements");
            return null;
        }
    }

    public async Task<List<DiscordEntitlement>> GetGroupedEntitlements(ulong? discordUserId = null, ulong? after = null,
        ulong? before = null)
    {
        var discordEntitlements = await GetRawEntitlementsFromDiscord(discordUserId, after, before);

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
        var noStartDate = s.Where(w => w.Deleted != true).Any(a => !a.StartsAt.HasValue);
        var noEndDate = s.Where(w => w.Deleted != true).Any(a => !a.EndsAt.HasValue);

        return new DiscordEntitlement
        {
            DiscordUserId = s.OrderByDescending(o => o.EndsAt).First().UserId.Value,
            Active = s.All(a => a.Deleted)
                ? false
                : noEndDate
                    ? true
                    : !s.OrderByDescending(o => o.EndsAt).First().EndsAt.HasValue ||
                      s.OrderByDescending(o => o.EndsAt).First().EndsAt.Value > DateTime.UtcNow.AddDays(-2),
            StartsAt = noStartDate
                ? null
                : s.OrderByDescending(o => o.EndsAt).First().StartsAt.HasValue
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

        await using var stream = await httpResponse.Content.ReadAsStreamAsync();
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

    public async Task<bool> RemoveEntitlement(ulong entitlementId)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri($"{_baseUrl}applications/{this._appId}/entitlements/{entitlementId}"),
            Method = HttpMethod.Delete,
        };
        request.Headers.Add("Authorization", $"Bot {_token}");

        using var httpResponse = await this._client.SendAsync(request);

        if (!httpResponse.IsSuccessStatusCode)
        {
            Log.Error("DiscordEntitlements - Remove entitlement - HTTP status code {statusCode} - {reason}", httpResponse.StatusCode,
                httpResponse.ReasonPhrase);
            return false;
        }

        return true;
    }

    public async Task<bool> AddStripeEntitlement(ulong discordUserId)
    {
        var request = new HttpRequestMessage
        {
            RequestUri = new Uri($"{_baseUrl}applications/{this._appId}/entitlements"),
            Method = HttpMethod.Post,
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    sku_id = 1120720816154345532,
                    owner_id = discordUserId.ToString(),
                    owner_type = 2
                }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Authorization", $"Bot {_token}");

        using var httpResponse = await this._client.SendAsync(request);

        if (!httpResponse.IsSuccessStatusCode)
        {
            Log.Error("DiscordEntitlements - Add entitlement - HTTP status code {statusCode} - {reason}", httpResponse.StatusCode,
                httpResponse.ReasonPhrase);
            return false;
        }

        return true;
    }

    public async Task SendVoiceMessage(string audioUrl, string interactionToken)
    {
        var audioBytes = await _client.GetByteArrayAsync(audioUrl);

        var isAac = audioUrl.Contains(".m4a", StringComparison.OrdinalIgnoreCase) ||
                    audioUrl.Contains("itunes", StringComparison.OrdinalIgnoreCase);

        var (waveform, durationSecs, oggBytes) = await GenerateWaveformAndTranscodeAsync(audioBytes, isAac);

        using var multipartContent = new MultipartFormDataContent();

        var audioContent = new ByteArrayContent(oggBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        multipartContent.Add(audioContent, "files[0]", "voice-message.ogg");

        var payload = new
        {
            flags = 8192, // IS_VOICE_MESSAGE flag
            attachments = new[]
            {
                new
                {
                    id = "0",
                    filename = "voice-message.ogg",
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

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var streamReader = new StreamReader(stream);
        var requestBody = await streamReader.ReadToEndAsync();

        if (!response.IsSuccessStatusCode)
        {
            Log.Information("error in music preview {requestBody}", requestBody);
        }

        response.EnsureSuccessStatusCode();
    }

    private static async Task<(string waveform, double durationSecs, byte[] oggBytes)> GenerateWaveformAndTranscodeAsync(
        byte[] audioBytes, bool isAac)
    {
        // Step 1: Decode source to PCM once (this is the expensive decode operation)
        var pcmBytes = await DecodeToPcmAsync(audioBytes, isAac);

        // Step 2: Generate waveform from PCM data
        var (waveform, durationSecs) = GenerateWaveformFromPcm(pcmBytes);

        // Step 3: Encode PCM to OGG Opus (fast - no decoding needed, just encoding)
        var oggBytes = await EncodePcmToOggOpusAsync(pcmBytes);

        return (waveform, durationSecs, oggBytes);
    }

    private static async Task<byte[]> DecodeToPcmAsync(byte[] audioBytes, bool isAac)
    {
        using var audioStream = new MemoryStream(audioBytes);
        using var pcmStream = new MemoryStream();

        // Use explicit format for AAC/M4A since pipe input can't seek for container parsing
        var formatArg = isAac ? "-f m4a" : "-f mp3";

        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"{formatArg} -i pipe:0 -ac 2 -ar 48000 -f s16le pipe:1",
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var inputTask = audioStream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream, cts.Token)
                .ContinueWith(t => ffmpegProcess.StandardInput.Close(), cts.Token);

            var outputTask = ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(pcmStream, cts.Token);

            var errorBuilder = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested &&
                       await ffmpegProcess.StandardError.ReadLineAsync(cts.Token) is { } line)
                {
                    errorBuilder.AppendLine(line);
                    Log.Debug("FFmpeg decode: {line}", line);
                }
            }, cts.Token);

            await Task.WhenAll(inputTask, outputTask, errorTask);
            await ffmpegProcess.WaitForExitAsync(cts.Token);

            if (ffmpegProcess.ExitCode != 0)
            {
                throw new Exception(
                    $"FFmpeg decode failed with exit code: {ffmpegProcess.ExitCode}. Error output: {errorBuilder}");
            }

            return pcmStream.ToArray();
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

    private static (string waveform, double durationSecs) GenerateWaveformFromPcm(byte[] pcmBytes)
    {
        const int bytesPerSample = 2;
        const int channels = 2;

        var durationSecs = (double)pcmBytes.Length / (48000 * bytesPerSample * channels);

        var totalSamples = pcmBytes.Length / bytesPerSample;
        if (totalSamples <= 0)
        {
            return (Convert.ToBase64String(new byte[] { 0 }), durationSecs);
        }

        const double floorDb = -60;

        var pointCount = Math.Clamp((int)Math.Round(durationSecs * 10), 1, 256);
        var waveformPoints = new byte[pointCount];

        for (var i = 0; i < pointCount; i++)
        {
            var start = (int)((long)i * totalSamples / pointCount);
            var end = (int)((long)(i + 1) * totalSamples / pointCount);

            double sumSquares = 0;
            var count = 0;
            for (var s = start; s < end; s++)
            {
                var sample = BitConverter.ToInt16(pcmBytes, s * bytesPerSample) / 32768f;
                sumSquares += (double)sample * sample;
                count++;
            }

            var rms = count > 0 ? Math.Sqrt(sumSquares / count) : 0;
            var db = rms > 0 ? 20 * Math.Log10(rms) : floorDb;
            var normalized = Math.Clamp((db - floorDb) / -floorDb, 0, 1);
            waveformPoints[i] = (byte)Math.Clamp((int)Math.Round(normalized * 255), 0, 255);
        }

        return (Convert.ToBase64String(waveformPoints), durationSecs);
    }

    private static async Task<byte[]> EncodePcmToOggOpusAsync(byte[] pcmBytes)
    {
        using var pcmStream = new MemoryStream(pcmBytes);
        using var oggStream = new MemoryStream();

        var ffmpegProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                // Input is raw PCM: stereo, 16-bit signed little-endian, 48kHz
                // Output is OGG Opus for Discord voice messages
                Arguments = "-f s16le -ar 48000 -ac 2 -i pipe:0 -c:a libopus -b:a 128k -application audio -f ogg pipe:1",
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var inputTask = pcmStream.CopyToAsync(ffmpegProcess.StandardInput.BaseStream, cts.Token)
                .ContinueWith(t => ffmpegProcess.StandardInput.Close(), cts.Token);

            var outputTask = ffmpegProcess.StandardOutput.BaseStream.CopyToAsync(oggStream, cts.Token);

            var errorBuilder = new StringBuilder();
            var errorTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested &&
                       await ffmpegProcess.StandardError.ReadLineAsync(cts.Token) is { } line)
                {
                    errorBuilder.AppendLine(line);
                    Log.Debug("FFmpeg encode: {line}", line);
                }
            }, cts.Token);

            await Task.WhenAll(inputTask, outputTask, errorTask);
            await ffmpegProcess.WaitForExitAsync(cts.Token);

            if (ffmpegProcess.ExitCode != 0)
            {
                throw new Exception(
                    $"FFmpeg encode failed with exit code: {ffmpegProcess.ExitCode}. Error output: {errorBuilder}");
            }

            return oggStream.ToArray();
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
