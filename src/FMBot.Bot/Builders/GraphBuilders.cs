using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using Npgsql;
using SkiaSharp;

namespace FMBot.Bot.Builders;

public class GraphBuilders
{
    private readonly GraphService _graphService;
    private readonly PuppeteerService _puppeteerService;
    private readonly BotSettings _botSettings;

    private static readonly string[] ChartColors =
    [
        "rgba(186, 0, 0, 1)",      // fmbot red
        "rgba(68, 138, 255, 1)",    // blue
        "rgba(16, 185, 129, 1)",    // emerald
        "rgba(245, 189, 65, 1)",    // gold
        "rgba(255, 111, 97, 1)",    // coral
        "rgba(162, 89, 255, 1)",    // purple
        "rgba(20, 184, 166, 1)",    // teal
        "rgba(255, 159, 64, 1)",    // orange
        "rgba(236, 72, 153, 1)",    // pink
        "rgba(34, 211, 238, 1)"     // cyan
    ];

    private static readonly string[] ColorEmojis =
    [
        "\U0001f7e5",  // red square
        "\U0001f7e6",  // blue square
        "\U0001f7e9",  // green square
        "\U0001f7e8",  // yellow square
        "\U0001f7e7",  // orange square
        "\U0001f7ea",  // purple square
        "\U0001f7e9",  // green square (teal)
        "\U0001f7e7",  // orange square
        "\U0001f7ea",  // purple square (pink)
        "\U0001f7e6"   // blue square (cyan)
    ];

    public GraphBuilders(GraphService graphService, PuppeteerService puppeteerService,
        IOptions<BotSettings> botSettings)
    {
        this._graphService = graphService;
        this._puppeteerService = puppeteerService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ResponseModel> ArtistGrowthAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        List<string> selectedArtists = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        var importUser = await UserRepository.GetImportUserForUserId(userSettings.UserId, connection);
        var dataSource = importUser?.DataSource ?? DataSource.LastFm;

        var (graphData, topArtists) = await this._graphService.GetArtistGrowthData(
            userSettings.UserId, dataSource, timeSettings, selectedArtists);

        if (topArtists.Count == 0 || graphData.Count == 0)
        {
            response.Embed.WithDescription(
                "Sorry, you or the user you're searching for don't have enough plays in the selected time period.\n\n" +
                "Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var chartDataJson = BuildChartDataJson(graphData);
        var chartOptionsJson = BuildChartOptionsJson(timeSettings.Description);

        using var image = await this._puppeteerService.GetGraph(chartDataJson, chartOptionsJson);
        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.FileName = "graph-artist-growth.png";

        response.ComponentsContainer.AccentColor = DiscordConstants.LastFmColorRed;
        response.ComponentsContainer.AddComponent(
            new TextDisplayProperties($"**Artist Scrobble Growth - {timeSettings.Description}**"));
        response.ComponentsContainer.AddComponent(
            new TextDisplayProperties($"-# {userSettings.DisplayName}'s top artists - Cumulative scrobbles"));

        var mediaGallery = new MediaGalleryItemProperties(
            new ComponentMediaProperties($"attachment://{response.FileName}"));
        response.ComponentsContainer.AddComponent(new MediaGalleryProperties { mediaGallery });

        var startUnixTs = (timeSettings.StartDateTime ?? DateTime.UtcNow.AddDays(-timeSettings.PlayDays.GetValueOrDefault(365)))
            .ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalSeconds;
        var endUnixTs = (timeSettings.EndDateTime ?? DateTime.UtcNow)
            .ToUniversalTime().Subtract(DateTime.UnixEpoch).TotalSeconds;

        var artistMenu = new StringMenuProperties(InteractionConstants.Graph.ArtistSelectMenu)
            .WithPlaceholder("Select artists to display")
            .WithMinValues(1)
            .WithMaxValues(Math.Min(10, topArtists.Count));

        var selectedArtistNames = selectedArtists ?? graphData.Select(g => g.ArtistName).ToList();
        var selectedColorIndex = 0;

        foreach (var artist in topArtists)
        {
            var isSelected = selectedArtistNames.Any(s =>
                string.Equals(s, artist.ArtistName, StringComparison.OrdinalIgnoreCase));

            var truncatedName = artist.ArtistName.Length > 60
                ? artist.ArtistName[..57] + "..."
                : artist.ArtistName;

            var value = $"{userSettings.UserId}:{(long)startUnixTs}:{(long)endUnixTs}:{artist.ArtistName}";
            if (value.Length > 100)
            {
                value = value[..100];
            }

            var option = new StringMenuSelectOptionProperties(truncatedName, value)
            {
                Default = isSelected,
                Description = $"{artist.PlayCount} plays"
            };

            if (isSelected)
            {
                option.Emoji = EmojiProperties.Standard(ColorEmojis[selectedColorIndex % ColorEmojis.Length]);
                selectedColorIndex++;
            }

            artistMenu.AddOption(option);
        }

        response.ComponentsContainer.AddComponents(artistMenu);

        var graphTypeMenu = new StringMenuProperties(InteractionConstants.Graph.TypeSelectMenu)
            .WithPlaceholder("Select graph type")
            .WithMinValues(1)
            .WithMaxValues(1);

        var graphTypeValue = $"{userSettings.UserId}:{(long)startUnixTs}:{(long)endUnixTs}:{(int)GraphType.ArtistGrowth}";
        graphTypeMenu.AddOption("Artist Growth", graphTypeValue, true, "Cumulative scrobbles per artist over time");

        response.ComponentsContainer.AddComponents(graphTypeMenu);

        response.ResponseType = ResponseType.ComponentsV2;

        return response;
    }

    private static string BuildChartDataJson(List<ArtistGraphData> graphData)
    {
        var datasets = new List<object>();

        for (var i = 0; i < graphData.Count; i++)
        {
            var artist = graphData[i];
            var color = ChartColors[i % ChartColors.Length];

            var data = artist.DataPoints.Select(dp => new
            {
                x = dp.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                y = dp.Count
            }).ToList();

            datasets.Add(new
            {
                label = artist.ArtistName,
                data,
                borderColor = color,
                backgroundColor = color.Replace(", 1)", ", 0.1)"),
                borderWidth = 2,
                pointRadius = 0,
                pointHoverRadius = 4,
                tension = 0.3,
                fill = false
            });
        }

        var chartData = new
        {
            datasets
        };

        return JsonSerializer.Serialize(chartData);
    }

    private static string BuildChartOptionsJson(string timeDescription)
    {
        var options = new
        {
            responsive = true,
            maintainAspectRatio = false,
            interaction = new
            {
                mode = "index",
                intersect = false
            },
            plugins = new
            {
                legend = new
                {
                    display = false
                },
                title = new
                {
                    display = false
                },
                tooltip = new
                {
                    backgroundColor = "rgba(47,49,54,0.95)",
                    titleColor = "#dcddde",
                    bodyColor = "#dcddde",
                    borderColor = "rgba(220,221,222,0.2)",
                    borderWidth = 1
                }
            },
            scales = new
            {
                x = new
                {
                    type = "time",
                    time = new
                    {
                        tooltipFormat = "MMM d, yyyy"
                    },
                    ticks = new
                    {
                        color = "#dcddde",
                        font = new { family = "worksans", size = 11 },
                        maxTicksLimit = 12
                    },
                    grid = new
                    {
                        color = "rgba(220,221,222,0.1)"
                    }
                },
                y = new
                {
                    beginAtZero = true,
                    ticks = new
                    {
                        color = "#dcddde",
                        font = new { family = "worksans", size = 11 }
                    },
                    grid = new
                    {
                        color = "rgba(220,221,222,0.1)"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(options);
    }
}
