using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Core.Charts;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.Images.Models;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;
using SkiaSharp;

namespace FMBot.Bot.Builders;

public class RecapBuilders
{
    private readonly UserService _userService;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly TimeService _timeService;
    private readonly ArtistsService _artistsService;
    private readonly AlbumService _albumService;
    private readonly TrackService _trackService;
    private readonly CensorService _censorService;
    private readonly PuppeteerService _puppeteerService;
    private readonly IChartImageFallback _chartImageFallback;
    private readonly TrackBuilders _trackBuilders;
    private readonly AlbumBuilders _albumBuilders;
    private readonly ArtistBuilders _artistBuilders;
    private readonly GenreBuilders _genreBuilders;
    private readonly CountryBuilders _countryBuilders;
    private readonly GameBuilders _gameBuilders;
    private readonly ChartBuilders _chartBuilders;

    public RecapBuilders(UserService userService, PlayService playService,
        GenreService genreService, IDataSourceFactory dataSourceFactory, TimeService timeService,
        ArtistsService artistsService, AlbumService albumService, TrackService trackService,
        CensorService censorService, PuppeteerService puppeteerService, IChartImageFallback chartImageFallback,
        TrackBuilders trackBuilders, AlbumBuilders albumBuilders, ArtistBuilders artistBuilders,
        GenreBuilders genreBuilders, CountryBuilders countryBuilders, GameBuilders gameBuilders,
        ChartBuilders chartBuilders)
    {
        this._userService = userService;
        this._playService = playService;
        this._genreService = genreService;
        this._dataSourceFactory = dataSourceFactory;
        this._timeService = timeService;
        this._artistsService = artistsService;
        this._albumService = albumService;
        this._trackService = trackService;
        this._censorService = censorService;
        this._puppeteerService = puppeteerService;
        this._chartImageFallback = chartImageFallback;
        this._trackBuilders = trackBuilders;
        this._albumBuilders = albumBuilders;
        this._artistBuilders = artistBuilders;
        this._genreBuilders = genreBuilders;
        this._countryBuilders = countryBuilders;
        this._gameBuilders = gameBuilders;
        this._chartBuilders = chartBuilders;
    }

    public async Task<ResponseModel> RecapAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        RecapPage view)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var botStatsCutoff = new DateTime(2023, 12, 1);
        var jumbleCutoff = new DateTime(2024, 04, 1);

        var topListSettings = new TopListSettings
        {
            Type = TopListType.Plays,
            EmbedSize = EmbedSize.Large
        };

        var chartSettings = new ChartSettings()
        {
            Width = 3,
            Height = 3,
            TimeSettings = timeSettings,
            TitleSetting = TitleSetting.TitlesDisabled,
            TimespanString = timeSettings.Description
        };

        var viewType = new StringMenuProperties(InteractionConstants.RecapPicker)
            .WithPlaceholder(context.Localize("recap.pagePicker"))
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

            var name = context.LocalizeOption(option);
            var value =
                $"{Enum.GetName(option)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}-{timeSettings.Description}";

            var active = option == view;

            viewType.AddOptions(new StringMenuSelectOptionProperties(name, value)
            {
                Default = active
            });
        }

        context.SelectMenu = viewType;

        var displayName = StringExtensions.Sanitize(userSettings.DisplayName);

        switch (view)
        {
            case RecapPage.Overview:
            {
                response = await OverviewAsync(context, userSettings, timeSettings);
                break;
            }
            case RecapPage.BotStats:
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
                response.ComponentsContainer.WithTextDisplay($"### {context.Localize("recap.botTitle",
                    ("user", displayName),
                    ("period", context.Localizer.PeriodLabel(timeSettings)))}");
                response.ComponentsContainer.WithSeparator();

                var userInteractions =
                    await this._userService.GetUserInteractions(userSettings.UserId, timeSettings);
                var stats = this._userService.CalculateBotStats(userInteractions);

                var searchStats = new StringBuilder();
                searchStats.AppendLine(context.LocalizeCount("recap.viewedArtists", stats.UniqueArtistsSearched));
                searchStats.AppendLine(context.LocalizeCount("recap.viewedAlbums", stats.UniqueAlbumsSearched));
                searchStats.AppendLine(context.LocalizeCount("recap.viewedTracks", stats.UniqueTracksSearched));
                AddSection(response, context.Localize("recap.fieldViewedCounts"), searchStats);

                var activityField = new StringBuilder();
                var timeForHour = DateTime.UtcNow.Date.AddHours(stats.MostActiveHour);
                var activeHourTimestamp = ((DateTimeOffset)timeForHour).ToUnixTimeSeconds();
                activityField.AppendLine(context.Localize("recap.mostActiveHour",
                    ("time", $"<t:{activeHourTimestamp}:t>")));
                activityField.AppendLine(context.Localize("recap.mostActiveDay",
                    ("day", context.Localizer.DayName(stats.MostActiveDayOfWeek))));
                var avgDays = Math.Max(1, (timeSettings.EndDateTime.Value - timeSettings.StartDateTime.Value).Days);
                activityField.AppendLine(context.Localize("recap.avgCommandsPerDay",
                    ("amount", $"{stats.TotalCommands / avgDays:F1}")));
                AddSection(response, context.Localize("recap.fieldActivityPatterns"), activityField);

                var usageField = new StringBuilder();
                usageField.AppendLine(context.Localize("recap.totalCommands",
                    ("amount", stats.TotalCommands.Format(context.NumberFormat))));
                usageField.AppendLine(context.Localize("recap.serversUsedIn",
                    ("amount", stats.ServersUsedIn.Format(context.NumberFormat))));
                usageField.AppendLine(context.Localize("recap.reliability",
                    ("percentage", $"{(100 - stats.ErrorRate):F1}")));
                AddSection(response, context.Localize("recap.fieldUsageStats"), usageField);

                var peakDaysField = new StringBuilder();
                foreach (var (date, count) in stats.PeakUsageDays)
                {
                    var unixTimestamp = ((DateTimeOffset)date).ToUnixTimeSeconds();
                    peakDaysField.AppendLine(context.LocalizeCount("recap.peakDay", count,
                        ("date", $"<t:{unixTimestamp}:D>")));
                }

                AddSection(response, context.Localize("recap.fieldMostActiveDays"), peakDaysField);

                var patternsField = new StringBuilder();
                patternsField.AppendLine(context.LocalizeCount("recap.longestDailyStreak", stats.LongestCommandStreak));
                patternsField.AppendLine(context.Localize("recap.avgTimeBetweenCommands",
                    ("hours", stats.AverageTimeBetweenCommands.Hours.ToString()),
                    ("minutes", stats.AverageTimeBetweenCommands.Minutes.ToString())));
                patternsField.AppendLine(context.Localize("recap.avgCommandsPerSession",
                    ("amount", $"{stats.AverageCommandsPerSession:F1}")));
                AddSection(response, context.Localize("recap.fieldUsagePatterns"), patternsField);

                AddBotStatsFooter();

                break;
            }
            case RecapPage.BotStatsCommands:
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
                response.ComponentsContainer.WithTextDisplay($"### {context.Localize("recap.botCommandsTitle",
                    ("period", context.Localizer.PeriodLabel(timeSettings)),
                    ("user", displayName))}");

                var userInteractions =
                    await this._userService.GetUserInteractions(userSettings.UserId, timeSettings);
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
                    commandsField.AppendLine(context.LocalizeCount("recap.commandUsage", count,
                        ("rank", (index + 1).ToString()),
                        ("command", command),
                        ("percentage", $"{percentage:F1}")));
                }

                if (commandsField.Length > 0)
                {
                    response.ComponentsContainer.WithSeparator();
                    response.ComponentsContainer.WithTextDisplay(commandsField.ToString().TrimEnd());
                }

                AddBotStatsFooter();

                break;
            }
            case RecapPage.BotStatsArtists:
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
                response.ComponentsContainer.WithTextDisplay($"### {context.Localize("recap.botArtistsTitle",
                    ("period", context.Localizer.PeriodLabel(timeSettings)),
                    ("user", displayName))}");

                var userInteractions =
                    await this._userService.GetUserInteractions(userSettings.UserId, timeSettings);
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
                        artistsField.AppendLine(context.LocalizeCount("recap.artistShown", count,
                            ("rank", (index + 1).ToString()),
                            ("artist", artist)));
                    }

                    response.ComponentsContainer.WithSeparator();
                    response.ComponentsContainer.WithTextDisplay(artistsField.ToString().TrimEnd());
                }

                AddBotStatsFooter();

                break;
            }
            case RecapPage.TopTracks:
            {
                response = await this._trackBuilders.TopTracksAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);
                break;
            }
            case RecapPage.TopAlbums:
            {
                response = await this._albumBuilders.TopAlbumsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);
                break;
            }
            case RecapPage.TopArtists:
            {
                response = await this._artistBuilders.TopArtistsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);
                break;
            }
            case RecapPage.TopGenres:
            {
                response = await this._genreBuilders.TopGenresAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);
                break;
            }
            case RecapPage.TopCountries:
            {
                response = await this._countryBuilders.TopCountriesAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);
                break;
            }
            case RecapPage.ArtistChart:
            {
                chartSettings.ArtistChart = true;
                response = await this._chartBuilders.ArtistChartAsync(context, userSettings, chartSettings);
                break;
            }
            case RecapPage.AlbumChart:
            {
                response = await this._chartBuilders.AlbumChartAsync(context, userSettings, chartSettings);
                break;
            }
            case RecapPage.Discoveries:
            {
                response = SupporterService.IsSupporter(userSettings.UserType)
                    ? await this._artistBuilders.ArtistDiscoveriesAsync(context, topListSettings, timeSettings,
                        userSettings, ResponseMode.Embed)
                    : ArtistBuilders.DiscoverySupporterRequired(context, userSettings, "recap-discoveries");
                break;
            }
            case RecapPage.ListeningTime:
            {
                if (!SupporterService.IsSupporter(userSettings.UserType))
                {
                    response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
                    response.ComponentsContainer.WithTextDisplay(context.Localize("recap.listeningTimeSupporterRequired"));
                    response.ComponentsContainer.WithSeparator();
                    response.ComponentsContainer.WithActionRow(new ActionRowProperties()
                        .AddComponents(new ButtonProperties(
                            InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "recap-listeningtime"),
                            context.Localize("buttons.getFmbotSupporter"), ButtonStyle.Primary)));
                    break;
                }

                response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);
                response.ComponentsContainer.WithTextDisplay($"### {context.Localize("recap.listeningTimeTitle",
                    ("user", displayName),
                    ("period", context.Localizer.PeriodLabel(timeSettings)))}");
                response.ComponentsContainer.WithSeparator();

                var timeZone =
                    SettingService.ResolveTimeZone(userSettings.TimeZone ?? "Eastern Standard Time");

                var filteredPlays = (await this._playService.GetUserPlaysForPeriod(userSettings.UserId,
                    timeSettings.StartDateTime, timeSettings.EndDateTime)).ToList();

                if (filteredPlays.Count == 0)
                {
                    response.ComponentsContainer.WithTextDisplay(context.Localize("recap.noPlaysInPeriod"));
                    break;
                }

                var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(filteredPlays);

                var coveredTimePeriod = filteredPlays.OrderByDescending(o => o.TimePlayed).First().TimePlayed -
                                        filteredPlays.OrderBy(o => o.TimePlayed).First().TimePlayed;
                var listeningTimeDescription = new StringBuilder();

                listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                    enrichedPlays.enrichedPlays.Count,
                    ("label", context.Localize("recap.rowLabelAll")),
                    ("time", context.Localizer.LongListeningTime(enrichedPlays.totalPlayTime))));

                if (coveredTimePeriod.Days <= 32)
                {
                    var dayGroups = enrichedPlays.enrichedPlays
                        .OrderBy(o => o.TimePlayed)
                        .GroupBy(g => new
                        {
                            TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Day,
                            TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Month,
                            TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Year
                        });

                    var numberOfDays = dayGroups.Count();
                    if (numberOfDays > 0)
                    {
                        var averagePlays = enrichedPlays.enrichedPlays.Count / numberOfDays;
                        var averageTime =
                            TimeSpan.FromMilliseconds(enrichedPlays.totalPlayTime.TotalMilliseconds / numberOfDays);

                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            averagePlays,
                            ("label", context.Localize("recap.rowLabelAvg")),
                            ("time", context.Localizer.LongListeningTime(averageTime))));
                    }

                    foreach (var day in dayGroups)
                    {
                        var time = TimeService.GetPlayTimeForEnrichedPlays(day);
                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            day.Count(),
                            ("label", day.Key.Day.ToString()),
                            ("time", context.Localizer.LongListeningTime(time))));
                    }
                }
                else if (coveredTimePeriod.Days <= 800)
                {
                    var monthGroups = enrichedPlays.enrichedPlays
                        .OrderBy(o => o.TimePlayed)
                        .GroupBy(g => new
                        {
                            TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Month,
                            TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Year
                        });

                    var numberOfMonths = monthGroups.Count();
                    if (numberOfMonths > 0)
                    {
                        var averagePlays = enrichedPlays.enrichedPlays.Count / numberOfMonths;
                        var averageTime =
                            TimeSpan.FromMilliseconds(
                                enrichedPlays.totalPlayTime.TotalMilliseconds / numberOfMonths);

                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            averagePlays,
                            ("label", context.Localize("recap.rowLabelAvg")),
                            ("time", context.Localizer.LongListeningTime(averageTime))));
                    }

                    foreach (var month in monthGroups)
                    {
                        var time = TimeService.GetPlayTimeForEnrichedPlays(month);
                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            month.Count(),
                            ("label", context.Localizer.Language.GetCultureInfo().DateTimeFormat
                                .GetAbbreviatedMonthName(month.Key.Month)),
                            ("time", context.Localizer.LongListeningTime(time))));
                    }
                }
                else
                {
                    var yearGroups = enrichedPlays.enrichedPlays
                        .OrderBy(o => o.TimePlayed)
                        .GroupBy(g => new { TimeZoneInfo.ConvertTime(g.TimePlayed, timeZone).Year });

                    var numberOfYears = yearGroups.Count();
                    if (numberOfYears > 0)
                    {
                        var averagePlays = enrichedPlays.enrichedPlays.Count / numberOfYears;
                        var averageTime =
                            TimeSpan.FromMilliseconds(enrichedPlays.totalPlayTime.TotalMilliseconds /
                                                      numberOfYears);

                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            averagePlays,
                            ("label", context.Localize("recap.rowLabelAvg")),
                            ("time", context.Localizer.LongListeningTime(averageTime))));
                    }

                    foreach (var year in yearGroups)
                    {
                        var time = TimeService.GetPlayTimeForEnrichedPlays(year);
                        listeningTimeDescription.AppendLine(context.LocalizeCount("recap.listeningRow",
                            year.Count(),
                            ("label", year.Key.Year.ToString()),
                            ("time", context.Localizer.LongListeningTime(time))));
                    }
                }

                response.ComponentsContainer.WithTextDisplay(listeningTimeDescription.ToString().TrimEnd());

                break;
            }
            case RecapPage.Games:
            {
                response =
                    await this._gameBuilders.GetJumbleUserStats(context, userSettings, JumbleType.Artist, timeSettings);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(view), view, null);
        }

        if (response.ResponseType == ResponseType.ComponentsV2)
        {
            if (view == RecapPage.Overview)
            {
                response.TopLevelComponents.Add(viewType);
            }
            else
            {
                response.ComponentsContainer.AddComponent(viewType);
            }
        }

        return response;

        void AddBotStatsFooter()
        {
            if (timeSettings.StartDateTime < botStatsCutoff)
            {
                AddSection(response, context.Localize("recap.fieldDataIncomplete"),
                    new StringBuilder(context.Localize("recap.dataIncompleteCommands", ("date", "<t:1701385200:D>"))));
            }

            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay($"-# {context.Localize("recap.footerPrivateBotStats")}");
        }
    }

    private static void AddSection(ResponseModel response, string title, StringBuilder body)
    {
        if (body.Length == 0)
        {
            return;
        }

        response.ComponentsContainer.WithTextDisplay($"**{title}**\n{body.ToString().TrimEnd()}");
    }

    private async Task<ResponseModel> OverviewAsync(ContextModel context, UserSettingsModel userSettings,
        TimeSettingsModel timeSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        var supporter = SupporterService.IsSupporter(userSettings.UserType);

        var topArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings,
            500, useCache: true);
        var topTracksTask = this._dataSourceFactory.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings,
            5, useCache: true);
        var topAlbumsTask = this._dataSourceFactory.GetTopAlbumsAsync(userSettings.UserNameLastFm, timeSettings,
            100, useCache: true);
        var playsTask = this._playService.GetUserPlaysForPeriod(userSettings.UserId, timeSettings.StartDateTime,
            timeSettings.EndDateTime);
        var scrobbleCountTask = supporter
            ? Task.FromResult<long?>(null)
            : this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

        var topArtists = await topArtistsTask;
        var topTracks = await topTracksTask;
        var topAlbums = await topAlbumsTask;
        var plays = await playsTask;
        var scrobbleCount = await scrobbleCountTask;

        if (!topArtists.Success)
        {
            response.ComponentsContainer.ErrorResponse(topArtists.Error, topArtists.Message, "recap",
                context.Localizer, context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        if (topArtists.Content?.TopArtists == null || topArtists.Content.TopArtists.Count == 0)
        {
            response.ComponentsContainer.WithTextDisplay(context.Localize("recap.noPlaysInPeriod"));
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var artists = topArtists.Content.TopArtists.Take(5).ToList();
        var tracks = topTracks.Content?.TopTracks?.Take(5).ToList() ?? [];

        await this._artistsService.FillArtistImages(artists);
        await FillTrackCovers(tracks, plays, userSettings.UserNameLastFm);

        var artistItems = new List<RecapCardItem>();
        foreach (var artist in artists)
        {
            artistItems.Add(new RecapCardItem(artist.ArtistName,
                context.LocalizeCount("shared.plays", artist.UserPlaycount),
                await SafeArtistImage(context, artist.ArtistImageUrl, artist.ArtistName)));
        }

        var trackItems = new List<RecapCardItem>();
        foreach (var track in tracks)
        {
            trackItems.Add(new RecapCardItem(track.TrackName, track.ArtistName,
                await SafeAlbumImage(context, track.AlbumCoverUrl, track.AlbumName, track.ArtistName)));
        }

        var topArtist = artists[0];

        var photoUrl = artistItems[0].ImageUrl;
        if (photoUrl == null)
        {
            var topArtistAlbum = topAlbums.Content?.TopAlbums?.FirstOrDefault(f =>
                string.Equals(f.ArtistName, topArtist.ArtistName, StringComparison.OrdinalIgnoreCase));

            if (topArtistAlbum != null)
            {
                await this._albumService.FillMissingAlbumCovers([topArtistAlbum]);
                photoUrl = await SafeAlbumImage(context, topArtistAlbum.AlbumCoverUrl, topArtistAlbum.AlbumName,
                    topArtistAlbum.ArtistName);
            }
        }

        var topArtistLabel = context.Localize("recap.card.topArtist");
        if (plays.Count > 0 &&
            (supporter || await this._playService.GetNonSupporterHistoryStart(userSettings.UserId) <=
                timeSettings.StartDateTime))
        {
            var periodStart = timeSettings.StartDateTime ?? plays.Min(m => m.TimePlayed);
            var daily = timeSettings.PlayDays <= 120;
            var bucketTopArtists = plays
                .GroupBy(g => (int)((g.TimePlayed - periodStart).TotalDays / (daily ? 1 : 7)))
                .Select(s => s
                    .GroupBy(a => a.ArtistName, StringComparer.OrdinalIgnoreCase)
                    .MaxBy(m => m.Count())
                    .Key)
                .ToList();

            var bucketsAsTopArtist = bucketTopArtists.Count(c =>
                string.Equals(c, topArtist.ArtistName, StringComparison.OrdinalIgnoreCase));

            if (bucketTopArtists.Count >= 2 && bucketsAsTopArtist > 0)
            {
                topArtistLabel = daily
                    ? context.Localize("recap.card.topArtistDays",
                        ("days", bucketsAsTopArtist.Format(context.NumberFormat)),
                        ("total", bucketTopArtists.Count.Format(context.NumberFormat)))
                    : context.Localize("recap.card.topArtistWeeks",
                        ("weeks", bucketsAsTopArtist.Format(context.NumberFormat)),
                        ("total", bucketTopArtists.Count.Format(context.NumberFormat)));
            }
        }

        var topArtistLine = context.LocalizeCount("shared.plays", topArtist.UserPlaycount);

        var scrobbles = supporter ? plays.Count : scrobbleCount.GetValueOrDefault();
        var stats = new List<RecapCardStat>
        {
            CardStat(context, scrobbles, context.LocalizeCount("shared.scrobbles", scrobbles))
        };

        if (supporter)
        {
            var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(plays);
            var minutes = (long)enrichedPlays.totalPlayTime.TotalMinutes;
            stats.Add(CardStat(context, minutes, context.LocalizeCount("shared.minutes", minutes)));

            var topArtistTime = TimeService.GetPlayTimeForEnrichedPlays(enrichedPlays.enrichedPlays.Where(w =>
                string.Equals(w.ArtistName, topArtist.ArtistName, StringComparison.OrdinalIgnoreCase)));
            if (topArtistTime.TotalMinutes >= 1)
            {
                topArtistLine +=
                    $" - {context.LocalizeCount("shared.minutes", (long)topArtistTime.TotalMinutes)}";
            }
        }

        var artistCount = supporter || topArtists.Content.TotalAmount is not > 0
            ? plays.Select(s => s.ArtistName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : topArtists.Content.TotalAmount.Value;
        stats.Add(CardStat(context, artistCount, context.LocalizeCount("shared.artists", artistCount)));

        if (!supporter)
        {
            var albumCount = topAlbums.Content?.TotalAmount is > 0
                ? topAlbums.Content.TotalAmount.Value
                : plays.Where(w => w.AlbumName != null)
                    .Select(s => $"{s.ArtistName} - {s.AlbumName}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            stats.Add(CardStat(context, albumCount, context.LocalizeCount("shared.albums", albumCount)));
        }

        var trackCount = supporter || topTracks.Content?.TotalAmount is not > 0
            ? plays.Select(s => $"{s.ArtistName} - {s.TrackName}").Distinct(StringComparer.OrdinalIgnoreCase).Count()
            : topTracks.Content.TotalAmount.Value;
        stats.Add(CardStat(context, trackCount, context.LocalizeCount("shared.tracks", trackCount)));

        var genres = await this._genreService.GetTopGenresForTopArtists(topArtists.Content.TopArtists);

        var period = context.Localizer.PeriodLabel(timeSettings);
        if (period.Length > 0)
        {
            period = char.ToUpper(period[0], context.Localizer.Language.GetCultureInfo()) + period[1..];
        }

        var card = new RecapCard
        {
            Title = context.Localize("recap.card.title",
                ("period", period),
                ("user", userSettings.DisplayName)),
            PhotoUrl = photoUrl,
            TopArtistLabel = topArtistLabel,
            TopArtistName = topArtist.ArtistName,
            TopArtistLine = topArtistLine,
            Stats = stats,
            TopArtistsLabel = context.Localize("recap.pages.topArtists"),
            Artists = artistItems,
            TopTracksLabel = context.Localize("recap.pages.topTracks"),
            Tracks = trackItems,
            Genres = genres.Take(4).Select(s => s.GenreName).ToList()
        };

        using var image = await this._puppeteerService.GetRecap(card);
        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.FileName = $"recap-{userSettings.UserId}.png";

        response.TopLevelComponents.Add(new MediaGalleryProperties
        {
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            {
                Description = StringExtensions.TruncateLongString(card.Title, 256)
            }
        });

        return response;
    }

    private async Task FillTrackCovers(List<TopTrack> tracks, ICollection<UserPlay> plays, string lastFmUserName)
    {
        await this._trackService.FillMissingTrackCovers(tracks);

        var trackAlbums = new List<(TopTrack Track, TopAlbum Album)>();
        foreach (var track in tracks.Where(w => w.AlbumCoverUrl == null))
        {
            var albumName = track.AlbumName ?? plays
                .Where(w => w.AlbumName != null &&
                            string.Equals(w.ArtistName, track.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(w.TrackName, track.TrackName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(g => g.AlbumName, StringComparer.OrdinalIgnoreCase)
                .MaxBy(m => m.Count())?.Key;

            if (albumName != null)
            {
                trackAlbums.Add((track, new TopAlbum { ArtistName = track.ArtistName, AlbumName = albumName }));
            }
        }

        if (trackAlbums.Count == 0)
        {
            return;
        }

        await this._albumService.FillMissingAlbumCovers(trackAlbums.Select(s => s.Album).ToList());

        var fetched = 0;
        foreach (var (track, album) in trackAlbums)
        {
            if (album.AlbumCoverUrl == null && fetched < 3)
            {
                album.AlbumCoverUrl = await this._chartImageFallback.GetAlbumCoverAsync(album, lastFmUserName);
                fetched++;
            }

            track.AlbumName = album.AlbumName;
            track.AlbumCoverUrl = album.AlbumCoverUrl;
        }
    }

    private static RecapCardStat CardStat(ContextModel context, long count, string text)
    {
        var number = count.Format(context.NumberFormat);
        return new RecapCardStat(number, text.Replace(number, "").Trim());
    }

    private async Task<string> SafeArtistImage(ContextModel context, string imageUrl, string artistName)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var result =
            await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, artistName);

        return result == CensorService.CensorResult.Safe ? imageUrl : null;
    }

    private async Task<string> SafeAlbumImage(ContextModel context, string imageUrl, string albumName,
        string artistName)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        var result = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
            albumName, artistName, imageUrl);

        return result == CensorService.CensorResult.Safe ? imageUrl : null;
    }
}
