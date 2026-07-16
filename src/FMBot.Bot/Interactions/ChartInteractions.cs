using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class ChartInteractions(
    UserService userService,
    SettingService settingService,
    ChartBuilders chartBuilders,
    ArtistsService artistsService,
    GenreService genreService,
    IDataSourceFactory dataSourceFactory)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Chart.EditButton)]
    public async Task EditChartButtonAsync(
        string creatorId, string chartType, string size, string timePeriodStr,
        int titleSetting, int skip, int sfw, int rainbow,
        string yearFilter, string decadeFilter, string artistFilterId, string filterSingles,
        string targetLfm)
    {
        try
        {
            if (Context.User.Id.ToString() != creatorId)
            {
                await RespondAsync(InteractionCallback.Message(
                    new InteractionMessageProperties()
                        .WithContent("Only the chart creator can edit this chart.")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var yearFilterValue = int.TryParse(yearFilter, out var yf) && yf > 0 ? yf : (int?)null;
            var decadeFilterValue = int.TryParse(decadeFilter, out var df) && df > 0 ? df : (int?)null;
            var filterSinglesValue = int.TryParse(filterSingles, out var fs) && fs == 1;

            var lfmParts = targetLfm.Split(':', 2);
            var lastFmUserName = lfmParts[0];
            var selectedGenres = (lfmParts.Length > 1 ? lfmParts[1] : string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var chartSettings = new ChartSettings(this.Context.User);
            chartSettings = ChartService.GetDimensions(chartSettings, size).newChartSettings;

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await settingService.GetUser(
                lastFmUserName, contextUser, this.Context.Guild, this.Context.User, true);

            var menuGenres = await GetChartGenreOptions(chartType, userSettings, timePeriodStr, selectedGenres);

            var modalCustomId =
                $"{InteractionConstants.Chart.EditModal}:{creatorId}:{chartType}:{artistFilterId}:{lastFmUserName}";

            if (chartType == "a")
            {
                var modal = ModalFactory.CreateAlbumChartSettingsModal(
                    modalCustomId, chartSettings.Width, chartSettings.Height, timePeriodStr,
                    titleSetting, skip == 1, sfw == 1, rainbow == 1,
                    yearFilterValue, decadeFilterValue, filterSinglesValue,
                    menuGenres, selectedGenres);
                await RespondAsync(InteractionCallback.Modal(modal));
            }
            else
            {
                var modal = ModalFactory.CreateArtistChartSettingsModal(
                    modalCustomId, chartSettings.Width, chartSettings.Height, timePeriodStr,
                    titleSetting, skip == 1, rainbow == 1,
                    menuGenres, selectedGenres);
                await RespondAsync(InteractionCallback.Modal(modal));
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task<List<string>> GetChartGenreOptions(
        string chartType, UserSettingsModel userSettings, string timePeriodStr, List<string> selectedGenres)
    {
        var timeSettings = SettingService.GetTimePeriod(timePeriodStr, timeZone: userSettings.TimeZone, language: LocalizationService.GetLanguage(this.Context.Interaction.GuildId, this.Context.Interaction.GuildLocale));

        List<string> artistNames;
        if (chartType == "a")
        {
            var topAlbums = await dataSourceFactory.GetTopAlbumsAsync(
                userSettings.UserNameLastFm, timeSettings, 250, useCache: true);
            artistNames = topAlbums?.Content?.TopAlbums?
                .Select(a => a.ArtistName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList() ?? [];
        }
        else
        {
            var topArtists = await dataSourceFactory.GetTopArtistsAsync(
                userSettings.UserNameLastFm, timeSettings, 250, useCache: true);
            artistNames = topArtists?.Content?.TopArtists?
                .Select(a => a.ArtistName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? [];
        }

        var chartGenres = await genreService.GetTopGenresForTopArtistsString(artistNames);

        var menuGenres = new List<string>();
        foreach (var genre in selectedGenres)
        {
            if (!menuGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                menuGenres.Add(genre);
            }
        }

        foreach (var genre in chartGenres)
        {
            if (menuGenres.Count >= 25)
            {
                break;
            }

            if (!menuGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                menuGenres.Add(genre);
            }
        }

        return menuGenres;
    }

    [ComponentInteraction(InteractionConstants.Chart.EditModal)]
    public async Task EditChartModalAsync(
        string creatorId, string chartType, string artistFilterId, string targetLfm)
    {
        try
        {
            if (Context.User.Id.ToString() != creatorId)
            {
                await RespondAsync(InteractionCallback.Message(
                    new InteractionMessageProperties()
                        .WithContent("Only the chart creator can edit this chart.")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await settingService.GetUser(
                null, contextUser, this.Context.Guild, this.Context.User, true);

            if (!string.Equals(userSettings.UserNameLastFm, targetLfm, StringComparison.OrdinalIgnoreCase))
            {
                userSettings = await settingService.GetUser(
                    targetLfm, contextUser, this.Context.Guild, this.Context.User, true);
            }

            var sizeStr = this.Context.GetModalValue("size") ?? "3x3";
            var timePeriodValue = this.Context.GetModalMenuValue("time_period");
            var checkedOptions = this.Context.GetModalCheckboxValues("options");

            var hasTitles = checkedOptions.Contains("titles");
            var hasSkip = checkedOptions.Contains("skip");
            var hasSfw = checkedOptions.Contains("sfw");
            var hasRainbow = checkedOptions.Contains("rainbow");
            var hasHideSingles = checkedOptions.Contains("hidesingles");

            var titleSetting = hasTitles ? TitleSetting.Titles : TitleSetting.TitlesDisabled;
            var skip = hasSkip || hasRainbow;

            Persistence.Domain.Models.Artist filteredArtist = null;
            if (int.TryParse(artistFilterId, out var artId) && artId > 0)
            {
                filteredArtist = await artistsService.GetArtistForId(artId);
            }

            var filteredGenres = this.Context.GetModalMenuValues("genre")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            int? releaseYear = null;
            int? releaseDecade = null;
            if (chartType == "a")
            {
                var releaseFilterStr = this.Context.GetModalValue("release_filter")?.Trim();
                if (!string.IsNullOrWhiteSpace(releaseFilterStr))
                {
                    if (releaseFilterStr.EndsWith("s") &&
                        int.TryParse(releaseFilterStr.TrimEnd('s'), out var decade) &&
                        decade >= 1900 && decade % 10 == 0)
                    {
                        releaseDecade = decade;
                    }
                    else if (int.TryParse(releaseFilterStr, out var year) && year is >= 1900 and <= 2100)
                    {
                        releaseYear = year;
                    }
                }
            }

            var hasFilters = releaseYear.HasValue || releaseDecade.HasValue || filteredArtist != null;
            var timeSettings = SettingService.GetTimePeriod(
                timePeriodValue,
                hasFilters ? TimePeriod.AllTime : TimePeriod.Weekly,
                timeZone: userSettings.TimeZone,
                language: LocalizationService.GetLanguage(this.Context.Interaction.GuildId, this.Context.Interaction.GuildLocale));

            var chartSettings = new ChartSettings(this.Context.User)
            {
                ArtistChart = chartType == "r",
                FilteredArtist = filteredArtist,
                TitleSetting = titleSetting,
                SkipWithoutImage = skip,
                SkipNsfw = hasSfw,
                RainbowSortingEnabled = hasRainbow,
                FilterSingles = hasHideSingles,
                TimeSettings = timeSettings,
                TimespanString = timeSettings.Description,
                TimespanUrlString = timeSettings.UrlParameter,
                ReleaseYearFilter = releaseYear,
                ReleaseDecadeFilter = releaseDecade,
                FilteredGenres = filteredGenres,
                CustomOptionsEnabled = titleSetting != TitleSetting.Titles || skip || hasSfw || hasRainbow ||
                                       hasHideSingles || filteredGenres.Count > 0
            };

            chartSettings = ChartService.GetDimensions(chartSettings, sizeStr).newChartSettings;

            ResponseModel response;
            if (chartType == "a")
            {
                response = await chartBuilders.AlbumChartAsync(
                    new ContextModel(this.Context, contextUser), userSettings, chartSettings);
            }
            else
            {
                response = await chartBuilders.ArtistChartAsync(
                    new ContextModel(this.Context, contextUser), userSettings, chartSettings);
            }

            var components = response.GetComponentsV2() ?? [];
            var attachments = response.Stream != null
                ? new[]
                {
                    new AttachmentProperties(
                        response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                        response.Stream)
                }
                : Array.Empty<AttachmentProperties>();

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = components;
                m.Embeds = [];
                m.Attachments = attachments;
                m.AllowedMentions = AllowedMentionsProperties.None;
                m.Flags = MessageFlags.IsComponentsV2;
            });

            if (response.Stream != null)
            {
                await response.Stream.DisposeAsync();
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
