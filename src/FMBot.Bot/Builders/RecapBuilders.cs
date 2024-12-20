using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class RecapBuilders
{
    private readonly UserService _userService;
    private readonly PlayService _playService;
    private readonly OpenAiService _openAiService;
    private readonly GenreService _genreService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly TimeService _timeService;
    private readonly TrackBuilders _trackBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly ArtistBuilders _artistBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly CountryBuilders _countryBuilders;
    private readonly GameBuilders _gameBuilders;
    private readonly ChartBuilders _chartBuilders;

    public RecapBuilders(UserService userService, PlayService playService, OpenAiService openAiService,
        GenreService genreService, IDataSourceFactory dataSourceFactory, TimeService timeService,
        TrackBuilders trackBuilders, AlbumBuilders albumBuilders, ArtistBuilders artistBuilders,
        GenreBuilders genreBuilders, CountryBuilders countryBuilders, GameBuilders gameBuilders,
        ChartBuilders chartBuilders)
    {
        this._userService = userService;
        this._playService = playService;
        this._openAiService = openAiService;
        this._genreService = genreService;
        this._dataSourceFactory = dataSourceFactory;
        this._timeService = timeService;
        this._trackBuilders = trackBuilders;
        this._albumBuilders = albumBuilders;
        this._artistBuilders = artistBuilders;
        this._genreBuilders = genreBuilders;
        this._countryBuilders = countryBuilders;
        this._gameBuilders = gameBuilders;
        this._chartBuilders = chartBuilders;
    }

    public bool RecapCacheHot(string timePeriod, string lastFmUserName)
    {
        return this._openAiService.RecapCacheHot(timePeriod, lastFmUserName);
    }

    public async Task<ResponseModel> RecapAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        RecapPage view)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Stream = null
        };

        var footer = new StringBuilder();

        var botStatsCutoff = new DateTime(2023, 12, 1);
        var jumbleCutoff = new DateTime(2024, 04, 1);

        var userInteractions =
            await this._userService.GetUserInteractions(userSettings.UserId, timeSettings);

        var topListSettings = new TopListSettings
        {
            Type = TopListType.Plays,
            EmbedSize = EmbedSize.Large
        };

        var chartSettings = new ChartSettings(context.DiscordUser)
        {
            Width = 3,
            Height = 3,
            TimeSettings = timeSettings,
            TitleSetting = TitleSetting.TitlesDisabled,
            TimespanString = timeSettings.Description
        };

        var viewType = new SelectMenuBuilder()
            .WithPlaceholder("Select recap page")
            .WithCustomId(InteractionConstants.Recap)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((RecapPage[])Enum.GetValues(typeof(RecapPage))))
        {
            if ((option == RecapPage.BotStats || option == RecapPage.BotStatsArtists ||
                 option == RecapPage.BotStatsCommands) &&
                timeSettings.EndDateTime <= botStatsCutoff)
            {
                continue;
            }

            if (option == RecapPage.Games && timeSettings.EndDateTime <= jumbleCutoff)
            {
                continue;
            }

            var name = option.GetAttribute<ChoiceDisplayAttribute>().Name;
            var value =
                $"{Enum.GetName(option)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}-{timeSettings.Description}";

            var active = option == view;

            viewType.AddOption(new SelectMenuOptionBuilder(name, value, null, isDefault: active));
        }

        var componentBuilder = new ComponentBuilder()
            .WithSelectMenu(viewType);
        response.Components = componentBuilder;
        context.SelectMenu = viewType;

        switch (view)
        {
            case RecapPage.Overview:
            {
                response.Embed.WithAuthor($"{timeSettings.Description} Recap for {userSettings.DisplayName}");

                var plays = await this._playService.GetAllUserPlays(userSettings.UserId);
                var filteredPlays = plays
                    .Where(w =>
                        w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
                    .ToList();

                var topArtists =
                    await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, 500,
                        useCache: true);

                var description = await this._openAiService.GetPlayRecap(timeSettings.Description, filteredPlays,
                    userSettings.UserNameLastFm, topArtists);
                response.Embed.WithDescription(description);

                var genres = await this._genreService.GetTopGenresForTopArtists(topArtists.Content.TopArtists);
                var genreString = new StringBuilder();
                for (var index = 0; index < Math.Min(genres.Count, 6); index++)
                {
                    var genre = genres[index];
                    genreString.AppendLine($"{index + 1}. **{genre.GenreName}**");
                }

                if (genreString.Length > 0)
                {
                    response.Embed.AddField("Top genres", genreString.ToString(), true);
                }

                var topArtistString = new StringBuilder();
                for (var index = 0; index < Math.Min(topArtists.Content.TopArtists.Count, 6); index++)
                {
                    var topArtist = topArtists.Content.TopArtists[index];
                    topArtistString.AppendLine($"{index + 1}. **{topArtist.ArtistName}**");
                }

                if (topArtistString.Length > 0)
                {
                    response.Embed.AddField("Top artists", topArtistString.ToString(), true);
                }

                if (SupporterService.IsSupporter(userSettings.UserType))
                {
                    var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(filteredPlays);
                    response.Embed.AddField("Scrobbles",
                        $"You got **{filteredPlays.Count}** {StringExtensions.GetScrobblesString(filteredPlays.Count)} with around **{StringExtensions.GetLongListeningTimeString(enrichedPlays.totalPlayTime)}** of listening time.");
                }
                else
                {
                    var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                        timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

                    response.Embed.AddField("Scrobbles",
                        $"You got **{count}** {StringExtensions.GetScrobblesString(count)}.");
                }

                if (!userSettings.DifferentUser && timeSettings.EndDateTime >= botStatsCutoff)
                {
                    var differentArtists =
                        userInteractions.Where(w => w.Artist != null).GroupBy(g => g.Artist.ToLower()).Count();
                    response.Embed.AddField("Bot stats",
                        $"You ran **{userInteractions.Count}** commands, which showed you **{differentArtists}** different artists.");
                }

                response.Embed.WithFooter("⬇️ Dive deeper with the dropdown below");

                break;
            }
            case RecapPage.BotStats:
            {
                response.Embed.WithAuthor($"Bot recap for {userSettings.DisplayName} - {timeSettings.Description}");

                var stats = this._userService.CalculateBotStats(userInteractions);

                var searchStats = new StringBuilder();
                searchStats.AppendLine($"**{stats.UniqueArtistsSearched}** different artists");
                searchStats.AppendLine($"**{stats.UniqueAlbumsSearched}** different albums");
                searchStats.AppendLine($"**{stats.UniqueTracksSearched}** different tracks");
                response.Embed.AddField("Viewed counts", searchStats.ToString(), true);

                var activityField = new StringBuilder();
                var timeForHour = DateTime.UtcNow.Date.AddHours(stats.MostActiveHour);
                var activeHourTimestamp = ((DateTimeOffset)timeForHour).ToUnixTimeSeconds();
                activityField.AppendLine($"Most active hour: <t:{activeHourTimestamp}:t>");
                activityField.AppendLine($"Most active day: **{stats.MostActiveDayOfWeek}**");
                activityField.AppendLine(
                    $"Avg commands per day: **{stats.TotalCommands / (timeSettings.EndDateTime.Value - timeSettings.StartDateTime.Value).Days:F1}**");
                response.Embed.AddField("Activity patterns", activityField.ToString(), true);

                var usageField = new StringBuilder();
                usageField.AppendLine($"Total commands: **{stats.TotalCommands}**");
                usageField.AppendLine($"Servers used in: **{stats.ServersUsedIn}**");
                usageField.AppendLine($"Reliability: **{(100 - stats.ErrorRate):F1}%**");
                response.Embed.AddField("Usage stats", usageField.ToString(), true);

                var peakDaysField = new StringBuilder();
                foreach (var (date, count) in stats.PeakUsageDays)
                {
                    var unixTimestamp = ((DateTimeOffset)date).ToUnixTimeSeconds();
                    peakDaysField.AppendLine($"<t:{unixTimestamp}:D> - *{count} commands*");
                }

                response.Embed.AddField("Most active days", peakDaysField.ToString(), true);

                var patternsField = new StringBuilder();
                patternsField.AppendLine($"Longest daily streak: **{stats.LongestCommandStreak} days**");
                patternsField.AppendLine(
                    $"Avg. time between commands: **{stats.AverageTimeBetweenCommands.Hours}h {stats.AverageTimeBetweenCommands.Minutes}m**");
                patternsField.AppendLine($"Avg. commands per session: **{stats.AverageCommandsPerSession:F1}**");
                response.Embed.AddField("Usage patterns", patternsField.ToString(), true);

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField("⚠️ Data possibly incomplete",
                        "*Commands before <t:1701385200:D> are not included*");
                }

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsCommands:
            {
                response.Embed.WithAuthor($"Top {timeSettings.Description} commands for {userSettings.DisplayName}");

                var stats = this._userService.CalculateBotStats(userInteractions);

                var topCommands = stats.CommandUsage
                    .OrderByDescending(x => x.Value)
                    .Take(16)
                    .ToList();

                var commandsField = new StringBuilder();
                for (var index = 0; index < topCommands.Count; index++)
                {
                    var (command, count) = topCommands[index];
                    var percentage = (count / (float)stats.TotalCommands) * 100;
                    commandsField.AppendLine($"{index + 1}. **{command}** — *{count} uses* — {percentage:F1}%");
                }

                response.Embed.WithDescription(commandsField.ToString());

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField("⚠️ Data possibly incomplete",
                        "*Commands before <t:1701385200:D> are not included*");
                }

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsArtists:
            {
                response.Embed.WithAuthor(
                    $"Top {timeSettings.Description} command artists for {userSettings.DisplayName}");

                var stats = this._userService.CalculateBotStats(userInteractions);

                if (stats.TopSearchedArtists.Any())
                {
                    var artistsField = new StringBuilder();
                    var topArtists = stats.TopSearchedArtists
                        .OrderByDescending(x => x.Value)
                        .Take(16)
                        .ToList();

                    for (var index = 0; index < topArtists.Count; index++)
                    {
                        var (artist, count) = topArtists[index];
                        artistsField.AppendLine($"{index + 1}. **{artist}** - *{count}x shown*");
                    }

                    response.Embed.WithDescription(artistsField.ToString());
                }

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField("⚠️ Data possibly incomplete",
                        "*Commands before <t:1701385200:D> are not included*");
                }

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.TopTracks:
            {
                var trackResponse = await this._trackBuilders.TopTracksAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = trackResponse.StaticPaginator;
                response.Embed = trackResponse.Embed;
                response.ResponseType = trackResponse.ResponseType;

                break;
            }
            case RecapPage.TopAlbums:
            {
                var albumResponse = await this._albumBuilders.TopAlbumsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = albumResponse.StaticPaginator;
                response.Embed = albumResponse.Embed;
                response.ResponseType = albumResponse.ResponseType;

                break;
            }
            case RecapPage.TopArtists:
            {
                var artistResponse = await this._artistBuilders.TopArtistsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = artistResponse.StaticPaginator;
                response.Embed = artistResponse.Embed;
                response.ResponseType = artistResponse.ResponseType;

                break;
            }
            case RecapPage.TopGenres:
            {
                var genreResponse = await this._genreBuilders.TopGenresAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.StaticPaginator = genreResponse.StaticPaginator;
                response.Embed = genreResponse.Embed;
                response.ResponseType = genreResponse.ResponseType;

                break;
            }
            case RecapPage.TopCountries:
            {
                var countryResponse = await this._countryBuilders.TopCountriesAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.StaticPaginator = countryResponse.StaticPaginator;
                response.Embed = countryResponse.Embed;
                response.ResponseType = countryResponse.ResponseType;

                break;
            }
            case RecapPage.ArtistChart:
            {
                chartSettings.ArtistChart = true;
                var chartResponse = await this._chartBuilders.ArtistChartAsync(context, userSettings, chartSettings);

                response.Stream = chartResponse.Stream;
                response.Embed = chartResponse.Embed;
                response.ResponseType = chartResponse.ResponseType;
                response.FileName = chartResponse.FileName;

                response.Embed.Description = null;
                response.Embed.Footer = null;

                break;
            }
            case RecapPage.AlbumChart:
            {
                var chartResponse = await this._chartBuilders.AlbumChartAsync(context, userSettings, chartSettings);

                response.Stream = chartResponse.Stream;
                response.Embed = chartResponse.Embed;
                response.ResponseType = chartResponse.ResponseType;
                response.FileName = chartResponse.FileName;

                response.Embed.Description = null;
                response.Embed.Footer = null;

                break;
            }
            case RecapPage.Discoveries:
            {
                if (SupporterService.IsSupporter(userSettings.UserType))
                {
                    var artistResponse = await this._artistBuilders.ArtistDiscoveriesAsync(context, topListSettings,
                        timeSettings,
                        userSettings, ResponseMode.Embed);

                    response.StaticPaginator = artistResponse.StaticPaginator;
                    response.Embed = artistResponse.Embed;
                    response.ResponseType = artistResponse.ResponseType;
                }
                else
                {
                    response.Embed.Description = ArtistBuilders.DiscoverySupporterRequired(context, userSettings).Embed
                        .Description;
                    response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                    response.Components.WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link,
                        url: Constants.GetSupporterDiscordLink);
                }
  
                break;
            }
            case RecapPage.ListeningTime:
            {
                if (SupporterService.IsSupporter(userSettings.UserType))
                {
                    var plays = await this._playService.GetAllUserPlays(userSettings.UserId);
                    var filteredPlays = plays
                        .Where(w =>
                            w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
                        .ToList();

                    var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(filteredPlays);

                    var monthDescription = new StringBuilder();
                    var monthGroups = enrichedPlays.enrichedPlays
                        .OrderBy(o => o.TimePlayed)
                        .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

                    monthDescription.AppendLine(
                        $"- **`All`** " +
                        $"— **{enrichedPlays.enrichedPlays.Count}** plays " +
                        $"— **{StringExtensions.GetLongListeningTimeString(enrichedPlays.totalPlayTime)}**");

                    var numberOfMonths = monthGroups.Count();
                    if (numberOfMonths > 0)
                    {
                        var averagePlays = enrichedPlays.enrichedPlays.Count / numberOfMonths;
                        var averageTime =
                            TimeSpan.FromMilliseconds(enrichedPlays.totalPlayTime.TotalMilliseconds / numberOfMonths);

                        monthDescription.AppendLine(
                            $"- **`Avg`** " +
                            $"— **{averagePlays}** plays " +
                            $"— **{StringExtensions.GetLongListeningTimeString(averageTime)}**");
                    }

                    foreach (var month in monthGroups.Take(15))
                    {
                        if (!enrichedPlays.enrichedPlays.Any(a =>
                                a.TimePlayed < DateTime.UtcNow.AddMonths(-month.Key.Month)))
                        {
                            break;
                        }

                        var time = TimeService.GetPlayTimeForEnrichedPlays(month);
                        monthDescription.AppendLine(
                            $"- **`{CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Key.Month)}`** " +
                            $"— **{month.Count()}** plays " +
                            $"— **{StringExtensions.GetLongListeningTimeString(time)}**");
                    }

                    if (monthDescription.Length > 0)
                    {
                        response.Embed.WithDescription(monthDescription.ToString());
                    }
                }
                else
                {
                    response.Embed.WithDescription(
                        $"To accurately calculate listening time we need to store your full Last.fm history. Your lifetime history and more are only available for supporters.");
                    response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                    response.Components.WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link,
                        url: Constants.GetSupporterDiscordLink);
                }

                break;
            }
            case RecapPage.Games:
            {
                var gameResponse =
                    await this._gameBuilders.GetJumbleUserStats(context, userSettings, JumbleType.Artist, timeSettings);
                response.Embed = gameResponse.Embed;
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(view), view, null);
        }

        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        return response;
    }
}
