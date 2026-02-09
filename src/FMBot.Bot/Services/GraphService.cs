using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services;

public class GraphService
{
    private readonly BotSettings _botSettings;

    public GraphService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<(List<ArtistGraphData> GraphData, List<(string ArtistName, int PlayCount)> TopArtists)>
        GetArtistGrowthData(int userId, DataSource dataSource, TimeSettingsModel timeSettings,
            List<string> selectedArtists = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var start = timeSettings.StartDateTime ?? DateTime.UtcNow.AddDays(-timeSettings.PlayDays.GetValueOrDefault(365));
        var end = timeSettings.EndDateTime ?? DateTime.UtcNow;

        var playDays = (int)(end - start).TotalDays;
        if (playDays < 1)
        {
            playDays = 1;
        }

        var interval = GraphHelpers.GetIntervalForPlayDays(playDays);
        var intervalString = GraphHelpers.GetIntervalString(interval);

        var topArtists = await PlayRepository.GetTopArtistNamesForPeriod(
            userId, connection, dataSource, start, end, 25);

        if (topArtists == null || topArtists.Count == 0)
        {
            return (new List<ArtistGraphData>(), new List<(string, int)>());
        }

        var artistsForGraph = selectedArtists?.Count > 0
            ? selectedArtists.ToArray()
            : topArtists.Take(5).Select(a => a.ArtistName).ToArray();

        var intervalData = await PlayRepository.GetArtistPlayCountsByInterval(
            userId, connection, dataSource, start, end, intervalString, artistsForGraph);

        var graphData = BuildCumulativeData(intervalData, artistsForGraph, start, end, interval);

        return (graphData, topArtists.ToList());
    }

    private static List<ArtistGraphData> BuildCumulativeData(
        IList<(string ArtistName, DateTime IntervalDate, int PlayCount)> intervalData,
        string[] artistNames, DateTime start, DateTime end, GraphTimeInterval interval)
    {
        var result = new List<ArtistGraphData>();

        foreach (var artistName in artistNames)
        {
            var artistIntervals = intervalData
                .Where(d => string.Equals(d.ArtistName, artistName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d.IntervalDate)
                .ToList();

            var dataPoints = new List<GraphDataPoint>();
            var cumulative = 0;

            var allDates = GenerateIntervalDates(start, end, interval);

            foreach (var date in allDates)
            {
                var match = artistIntervals.FirstOrDefault(d =>
                    d.IntervalDate.Date == date.Date);

                if (match.ArtistName != null)
                {
                    cumulative += match.PlayCount;
                }

                dataPoints.Add(new GraphDataPoint
                {
                    Date = date,
                    Count = cumulative
                });
            }

            result.Add(new ArtistGraphData
            {
                ArtistName = artistName,
                TotalPlays = cumulative,
                DataPoints = dataPoints
            });
        }

        return result;
    }

    private static List<DateTime> GenerateIntervalDates(DateTime start, DateTime end, GraphTimeInterval interval)
    {
        var dates = new List<DateTime>();
        var truncatedStart = TruncateToInterval(start, interval);
        var current = truncatedStart;

        while (current <= end)
        {
            dates.Add(current);

            current = interval switch
            {
                GraphTimeInterval.Daily => current.AddDays(1),
                GraphTimeInterval.Weekly => current.AddDays(7),
                GraphTimeInterval.Monthly => current.AddMonths(1),
                _ => current.AddDays(1)
            };
        }

        return dates;
    }

    private static DateTime TruncateToInterval(DateTime date, GraphTimeInterval interval)
    {
        return interval switch
        {
            GraphTimeInterval.Daily => date.Date,
            GraphTimeInterval.Weekly => date.DayOfWeek == DayOfWeek.Sunday
                ? date.Date.AddDays(-6)
                : date.Date.AddDays(-(int)date.DayOfWeek + (int)DayOfWeek.Monday),
            GraphTimeInterval.Monthly => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => date.Date
        };
    }
}
