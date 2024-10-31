using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
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

    public RecapBuilders(UserService userService, PlayService playService, OpenAiService openAiService,
        GenreService genreService, IDataSourceFactory dataSourceFactory, TimeService timeService,
        TrackBuilders trackBuilders, AlbumBuilders albumBuilders, ArtistBuilders artistBuilders,
        GenreBuilders genreBuilders, CountryBuilders countryBuilders, GameBuilders gameBuilders)
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
    }

    public async Task<ResponseModel> RecapAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        RecapPage view)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithTitle($"Recap - {userSettings.DisplayName}");
        var footer = new StringBuilder();

        var userInteractions =
            await this._userService.GetUserInteractions(userSettings.UserId, timeSettings);

        var recapPeriod = RecapPeriod.CurrentYear;
        var topListSettings = new TopListSettings
        {
            Type = TopListType.Plays,
            EmbedSize = EmbedSize.Large
        };

        var viewType = new SelectMenuBuilder()
            .WithPlaceholder("Select recap page")
            .WithCustomId(InteractionConstants.Recap)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((RecapPage[])Enum.GetValues(typeof(RecapPage))))
        {
            var name = option.GetAttribute<ChoiceDisplayAttribute>().Name;
            var value =
                $"{Enum.GetName(option)}-{Enum.GetName(recapPeriod)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}";

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
                var plays = await this._playService.GetAllUserPlays(userSettings.UserId);
                var filteredPlays = plays
                    .Where(w =>
                        w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
                    .ToList();

                var description = await this._openAiService.GetPlayRecap(recapPeriod, filteredPlays);
                response.Embed.WithDescription(description);

                var genres = await this._genreService.GetTopGenresForPlays(filteredPlays, 6);
                var genreString = new StringBuilder();
                for (var index = 0; index < genres.Count; index++)
                {
                    var genre = genres[index];
                    genreString.AppendLine($"{index + 1}. **{genre}**");
                }

                response.Embed.AddField("Top genres", genreString.ToString(), true);

                var topArtists =
                    await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, 6);
                var topArtistString = new StringBuilder();
                for (var index = 0; index < topArtists.Content.TopArtists.Count; index++)
                {
                    var topArtist = topArtists.Content.TopArtists[index];
                    topArtistString.AppendLine($"{index + 1}. **{topArtist.ArtistName}**");
                }

                response.Embed.AddField("Top artists", topArtistString.ToString(), true);

                var enrichedPlays = await this._timeService.EnrichPlaysWithPlayTime(filteredPlays);
                response.Embed.AddField("Scrobbles",
                    $"You got {filteredPlays.Count} {StringExtensions.GetScrobblesString(filteredPlays.Count)} with around {StringExtensions.GetLongListeningTimeString(enrichedPlays.totalPlayTime)} of listening time.");

                if (!userSettings.DifferentUser)
                {
                    var differentArtists =
                        userInteractions.Where(w => w.Artist != null).GroupBy(g => g.Artist.ToLower()).Count();
                    response.Embed.AddField("Bot stats",
                        $"You ran {userInteractions.Count} commands, which showed you {differentArtists} different artists.");
                }

                break;
            }
            case RecapPage.BotStats:
            {
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

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsCommands:
            {
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

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.BotStatsArtists:
            {
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

                footer.AppendLine("Private - Only you can request your bot usage stats");
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);

                break;
            }
            case RecapPage.TopTracks:
            {
                var trackResponse = await this._trackBuilders.TopTracksAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = trackResponse.StaticPaginator;
                response.ResponseType = ResponseType.Paginator;

                break;
            }
            case RecapPage.TopAlbums:
            {
                var albumResponse = await this._albumBuilders.TopAlbumsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = albumResponse.StaticPaginator;
                response.ResponseType = ResponseType.Paginator;

                break;
            }
            case RecapPage.TopArtists:
            {
                var artistResponse = await this._artistBuilders.TopArtistsAsync(context, topListSettings, timeSettings,
                    userSettings, ResponseMode.Embed);

                response.StaticPaginator = artistResponse.StaticPaginator;
                response.ResponseType = ResponseType.Paginator;

                break;
            }
            case RecapPage.TopGenres:
            {
                var genreResponse = await this._genreBuilders.TopGenresAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.StaticPaginator = genreResponse.StaticPaginator;
                response.ResponseType = ResponseType.Paginator;

                break;
            }
            case RecapPage.TopCountries:
            {
                var countryResponse = await this._countryBuilders.TopCountriesAsync(context, userSettings, timeSettings,
                    topListSettings, ResponseMode.Embed);

                response.StaticPaginator = countryResponse.StaticPaginator;
                response.ResponseType = ResponseType.Paginator;

                break;
            }
            case RecapPage.Discoveries:
                break;
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
