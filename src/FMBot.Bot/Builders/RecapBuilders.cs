using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;

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

    public bool RecapCacheHot(string timePeriod, string lastFmUserName, Language language)
    {
        return this._openAiService.RecapCacheHot(timePeriod, lastFmUserName, language);
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
        response.StringMenus.Add(viewType);

        switch (view)
        {
            case RecapPage.Overview:
            {
                response.Embed.WithAuthor(context.Localize("recap.title",
                    ("period", context.Localizer.PeriodLabel(timeSettings)),
                    ("user", userSettings.DisplayName)));

                var plays = await this._playService.GetAllUserPlays(userSettings.UserId);
                var filteredPlays = plays
                    .Where(w =>
                        w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
                    .ToList();

                var topArtists =
                    await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, 500,
                        useCache: true);

                var description = await this._openAiService.GetPlayRecap(timeSettings.Description, filteredPlays,
                    userSettings.UserNameLastFm, topArtists, context.Localizer.Language);
                response.Embed.WithDescription(description);

                if (topArtists.Content?.TopArtists != null)
                {
                    var genres = await this._genreService.GetTopGenresForTopArtists(topArtists.Content.TopArtists);
                    var genreString = new StringBuilder();
                    for (var index = 0; index < Math.Min(genres.Count, 6); index++)
                    {
                        var genre = genres[index];
                        genreString.AppendLine($"{index + 1}. **{genre.GenreName}**");
                    }

                    if (genreString.Length > 0)
                    {
                        response.Embed.AddField(context.Localize("recap.pages.topGenres"), genreString.ToString(), true);
                    }

                    var topArtistString = new StringBuilder();
                    for (var index = 0; index < Math.Min(topArtists.Content.TopArtists.Count, 6); index++)
                    {
                        var topArtist = topArtists.Content.TopArtists[index];
                        topArtistString.AppendLine($"{index + 1}. **{topArtist.ArtistName}**");
                    }

                    if (topArtistString.Length > 0)
                    {
                        response.Embed.AddField(context.Localize("recap.pages.topArtists"), topArtistString.ToString(), true);
                    }
                }

                if (SupporterService.IsSupporter(userSettings.UserType))
                {
                    var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(filteredPlays);
                    response.Embed.AddField(context.Localize("recap.fieldScrobbles"),
                        context.LocalizeCount("recap.scrobblesWithTime", filteredPlays.Count,
                            ("minutes", enrichedPlays.totalPlayTime.TotalMinutes.ToString("0"))));
                }
                else
                {
                    var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                        timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

                    response.Embed.AddField(context.Localize("recap.fieldScrobbles"),
                        context.LocalizeCount("recap.scrobbleCount", count.GetValueOrDefault()));
                }

                if (!userSettings.DifferentUser && timeSettings.EndDateTime >= botStatsCutoff)
                {
                    var differentArtists =
                        userInteractions.Where(w => w.Artist != null).GroupBy(g => g.Artist.ToLower()).Count();
                    response.Embed.AddField(context.Localize("recap.fieldBotStats"),
                        context.Localize("recap.botStatsSummary",
                            ("commands", userInteractions.Count.Format(context.NumberFormat)),
                            ("artists", differentArtists.Format(context.NumberFormat))));
                }

                response.Embed.WithFooter(context.Localize("recap.footerDiveDeeper"));

                break;
            }
            case RecapPage.BotStats:
            {
                response.Embed.WithAuthor(context.Localize("recap.botTitle",
                    ("user", userSettings.DisplayName),
                    ("period", context.Localizer.PeriodLabel(timeSettings))));

                var stats = this._userService.CalculateBotStats(userInteractions);

                var searchStats = new StringBuilder();
                searchStats.AppendLine(context.LocalizeCount("recap.viewedArtists", stats.UniqueArtistsSearched));
                searchStats.AppendLine(context.LocalizeCount("recap.viewedAlbums", stats.UniqueAlbumsSearched));
                searchStats.AppendLine(context.LocalizeCount("recap.viewedTracks", stats.UniqueTracksSearched));
                response.Embed.AddField(context.Localize("recap.fieldViewedCounts"), searchStats.ToString(), true);

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
                response.Embed.AddField(context.Localize("recap.fieldActivityPatterns"), activityField.ToString(), true);

                var usageField = new StringBuilder();
                usageField.AppendLine(context.Localize("recap.totalCommands",
                    ("amount", stats.TotalCommands.Format(context.NumberFormat))));
                usageField.AppendLine(context.Localize("recap.serversUsedIn",
                    ("amount", stats.ServersUsedIn.Format(context.NumberFormat))));
                usageField.AppendLine(context.Localize("recap.reliability",
                    ("percentage", $"{(100 - stats.ErrorRate):F1}")));
                response.Embed.AddField(context.Localize("recap.fieldUsageStats"), usageField.ToString(), true);

                var peakDaysField = new StringBuilder();
                foreach (var (date, count) in stats.PeakUsageDays)
                {
                    var unixTimestamp = ((DateTimeOffset)date).ToUnixTimeSeconds();
                    peakDaysField.AppendLine(context.LocalizeCount("recap.peakDay", count,
                        ("date", $"<t:{unixTimestamp}:D>")));
                }

                response.Embed.AddField(context.Localize("recap.fieldMostActiveDays"), peakDaysField.ToString(), true);

                var patternsField = new StringBuilder();
                patternsField.AppendLine(context.LocalizeCount("recap.longestDailyStreak", stats.LongestCommandStreak));
                patternsField.AppendLine(context.Localize("recap.avgTimeBetweenCommands",
                    ("hours", stats.AverageTimeBetweenCommands.Hours.ToString()),
                    ("minutes", stats.AverageTimeBetweenCommands.Minutes.ToString())));
                patternsField.AppendLine(context.Localize("recap.avgCommandsPerSession",
                    ("amount", $"{stats.AverageCommandsPerSession:F1}")));
                response.Embed.AddField(context.Localize("recap.fieldUsagePatterns"), patternsField.ToString(), true);

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField(context.Localize("recap.fieldDataIncomplete"),
                        context.Localize("recap.dataIncompleteCommands", ("date", "<t:1701385200:D>")));
                }

                footer.AppendLine(context.Localize("recap.footerPrivateBotStats"));
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsCommands:
            {
                response.Embed.WithAuthor(context.Localize("recap.botCommandsTitle",
                    ("period", context.Localizer.PeriodLabel(timeSettings)),
                    ("user", userSettings.DisplayName)));

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

                response.Embed.WithDescription(commandsField.ToString());

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField(context.Localize("recap.fieldDataIncomplete"),
                        context.Localize("recap.dataIncompleteCommands", ("date", "<t:1701385200:D>")));
                }

                footer.AppendLine(context.Localize("recap.footerPrivateBotStats"));
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsArtists:
            {
                response.Embed.WithAuthor(context.Localize("recap.botArtistsTitle",
                    ("period", context.Localizer.PeriodLabel(timeSettings)),
                    ("user", userSettings.DisplayName)));

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

                    response.Embed.WithDescription(artistsField.ToString());
                }

                if (timeSettings.StartDateTime < botStatsCutoff)
                {
                    response.Embed.AddField(context.Localize("recap.fieldDataIncomplete"),
                        context.Localize("recap.dataIncompleteCommands", ("date", "<t:1701385200:D>")));
                }

                footer.AppendLine(context.Localize("recap.footerPrivateBotStats"));
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.TopTracks:
            {
                var trackResponse = await this._trackBuilders.TopTracksAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.ComponentPaginator = trackResponse.ComponentPaginator;
                response.Embed = trackResponse.Embed;
                response.ResponseType = trackResponse.ResponseType;

                break;
            }
            case RecapPage.TopAlbums:
            {
                var albumResponse = await this._albumBuilders.TopAlbumsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.ComponentPaginator = albumResponse.ComponentPaginator;
                response.Embed = albumResponse.Embed;
                response.ResponseType = albumResponse.ResponseType;

                break;
            }
            case RecapPage.TopArtists:
            {
                var artistResponse = await this._artistBuilders.TopArtistsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.ComponentPaginator = artistResponse.ComponentPaginator;
                response.Embed = artistResponse.Embed;
                response.ResponseType = artistResponse.ResponseType;

                break;
            }
            case RecapPage.TopGenres:
            {
                var genreResponse = await this._genreBuilders.TopGenresAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.ComponentPaginator = genreResponse.ComponentPaginator;
                response.Embed = genreResponse.Embed;
                response.ResponseType = genreResponse.ResponseType;

                break;
            }
            case RecapPage.TopCountries:
            {
                var countryResponse = await this._countryBuilders.TopCountriesAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.ComponentPaginator = countryResponse.ComponentPaginator;
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

                    response.ComponentPaginator = artistResponse.ComponentPaginator;
                    response.Embed = artistResponse.Embed;
                    response.ResponseType = artistResponse.ResponseType;
                }
                else
                {
                    response.Embed.Description = ArtistBuilders.DiscoverySupporterRequired(context, userSettings).Embed
                        .Description;
                    response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                    response.Components.WithButton(context.Localize("buttons.getSupporter"), style: ButtonStyle.Primary,
                        customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "recap-discoveries"));
                }

                break;
            }
            case RecapPage.ListeningTime:
            {
                if (SupporterService.IsSupporter(userSettings.UserType))
                {
                    response.Embed.WithAuthor(context.Localize("recap.listeningTimeTitle",
                        ("user", userSettings.DisplayName),
                        ("period", context.Localizer.PeriodLabel(timeSettings))));
                    var timeZone =
                        SettingService.ResolveTimeZone(userSettings.TimeZone ?? "Eastern Standard Time");

                    var plays = await this._playService.GetAllUserPlays(userSettings.UserId);
                    var filteredPlays = plays
                        .Where(w =>
                            w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
                        .ToList();

                    if (filteredPlays.Count == 0)
                    {
                        response.Embed.WithDescription(context.Localize("recap.noPlaysInPeriod"));
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


                    if (listeningTimeDescription.Length > 0)
                    {
                        response.Embed.WithDescription(listeningTimeDescription.ToString());
                    }
                }
                else
                {
                    response.Embed.WithDescription(context.Localize("recap.listeningTimeSupporterRequired"));
                    response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                    response.Components.WithButton(context.Localize("buttons.getSupporter"), style: ButtonStyle.Primary,
                        customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "recap-listeningtime"));
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
