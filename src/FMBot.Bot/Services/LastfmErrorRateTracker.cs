using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace FMBot.Bot.Services;

public static class LastfmErrorRateTracker
{
    private const string PrometheusBaseUrl = "http://localhost:9090";

    private const string Query =
        "sum(rate(lastfm_errors_failure[5m])) / (sum(rate(lastfm_api_calls[5m])) + sum(rate(lastfm_authorized_api_calls[5m])))";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private static readonly TimeSpan Step = TimeSpan.FromMinutes(3);

    private static readonly char[] Blocks = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private static Snapshot _snapshot;
    private static int _refreshing;

    public static string GetFailureRateDescription()
    {
        var snap = _snapshot;

        if (snap == null || DateTime.UtcNow - snap.FetchedAt > CacheTtl)
        {
            TriggerRefresh();
        }

        if (snap is not { HasData: true })
        {
            return string.Empty;
        }

        return
            $"\n\nIn the last hour, Last.fm has returned this error on {snap.AveragePercent.ToString("0.#", CultureInfo.InvariantCulture)}% of our requests.  `{snap.Sparkline}`";
    }

    private static void TriggerRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Prometheus failure-rate refresh failed");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    private static async Task RefreshAsync()
    {
        var end = DateTimeOffset.UtcNow;
        var start = end - Window;

        var url = $"{PrometheusBaseUrl}/api/v1/query_range"
                  + $"?query={Uri.EscapeDataString(Query)}"
                  + $"&start={start.ToUnixTimeSeconds()}"
                  + $"&end={end.ToUnixTimeSeconds()}"
                  + $"&step={(int)Step.TotalSeconds}";

        using var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode)
        {
            return;
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var result = doc.RootElement.GetProperty("data").GetProperty("result");
        if (result.GetArrayLength() == 0)
        {
            _snapshot = new Snapshot(DateTime.UtcNow, false, 0, string.Empty);
            return;
        }

        var values = result[0].GetProperty("values");
        var count = values.GetArrayLength();
        if (count == 0)
        {
            _snapshot = new Snapshot(DateTime.UtcNow, false, 0, string.Empty);
            return;
        }

        var series = new double[count];
        double sum = 0;
        double max = 0;

        for (var i = 0; i < count; i++)
        {
            var raw = values[i][1].GetString();
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                v = 0;
            }

            series[i] = v;
            sum += v;
            if (v > max)
            {
                max = v;
            }
        }

        var avgPercent = sum / count * 100d;

        var sb = new StringBuilder(count);
        for (var i = 0; i < count; i++)
        {
            var idx = max <= 0 ? 0 : (int)Math.Round(series[i] / max * (Blocks.Length - 1));
            if (idx < 0)
            {
                idx = 0;
            }

            if (idx >= Blocks.Length)
            {
                idx = Blocks.Length - 1;
            }

            sb.Append(Blocks[idx]);
        }

        _snapshot = new Snapshot(DateTime.UtcNow, true, avgPercent, sb.ToString());
    }

    private sealed record Snapshot(DateTime FetchedAt, bool HasData, double AveragePercent, string Sparkline);
}
