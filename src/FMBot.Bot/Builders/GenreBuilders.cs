using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Images.Generators;
using Humanizer;
using Guild = FMBot.Persistence.Domain.Models.Guild;
using NetCord;
using NetCord.Rest;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class GenreBuilders
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GenreService _genreService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly PlayService _playService;
    private readonly ArtistsService _artistsService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly IndexService _indexService;
    private readonly PuppeteerService _puppeteerService;
    private readonly CensorService _censorService;
    private readonly MusicDataFactory _musicDataFactory;

    public GenreBuilders(UserService userService,
        GuildService guildService,
        GenreService genreService,
        WhoKnowsArtistService whoKnowsArtistService,
        PlayService playService,
        ArtistsService artistsService,
        IDataSourceFactory dataSourceFactory,
        IndexService indexService,
        PuppeteerService puppeteerService,
        CensorService censorService,
        MusicDataFactory musicDataFactory)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._genreService = genreService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
        this._artistsService = artistsService;
        this._dataSourceFactory = dataSourceFactory;
        this._indexService = indexService;
        this._puppeteerService = puppeteerService;
        this._censorService = censorService;
        this._musicDataFactory = musicDataFactory;
    }

    public async Task<ResponseModel> GetGuildGenres(ContextModel context, Guild guild,
        GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        IList<GuildGenre> topGuildGenres;
        IList<GuildGenre> previousTopGuildGenres = null;

        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildGenres =
                await this._genreService.GetTopGenresForGuildAllTime(guild.GuildId, guildListSettings.OrderType);
        }
        else
        {
            topGuildGenres = await this._playService.GetGuildTopGenresPlays(guild.GuildId,
                guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.EndDateTime);
            previousTopGuildGenres = await this._playService.GetGuildTopGenresPlays(guild.GuildId,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.BillboardEndDateTime);
        }

        var title = $"Top {guildListSettings.TimeDescription.ToLower()} genres in {context.DiscordGuild.Name}";

        var footerLabel = guildListSettings.OrderType == OrderType.Listeners
            ? "Listener count"
            : "Play count";

        string footerHint = new Random().Next(0, 5) switch
        {
            1 => $"View specific genre listeners with '{context.Prefix}whoknowsgenre'",
            2 => "Available time periods: alltime, monthly, weekly, current and last month",
            3 => "Available sorting options: plays and listeners",
            _ => null
        };

        var genrePages = topGuildGenres.Chunk(12).ToList();

        var counter = 1;
        var pageDescriptions = new List<string>();
        foreach (var page in genrePages)
        {
            var pageString = new StringBuilder();
            foreach (var genre in page)
            {
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{genre.ListenerCount.Format(context.NumberFormat)}` · **{genre.GenreName.Transform(To.TitleCase)}** - *{genre.TotalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(genre.TotalPlaycount)}*"
                    : $"`{genre.TotalPlaycount.Format(context.NumberFormat)}` · **{genre.GenreName.Transform(To.TitleCase)}** - *{genre.ListenerCount.Format(context.NumberFormat)} {StringExtensions.GetListenersString(genre.ListenerCount)}*";

                if (previousTopGuildGenres != null && previousTopGuildGenres.Any())
                {
                    var previousTopGenre = previousTopGuildGenres.FirstOrDefault(f => f.GenreName == genre.GenreName);
                    int? previousPosition = previousTopGenre == null
                        ? null
                        : previousTopGuildGenres.IndexOf(previousTopGenre);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false)
                        .Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            pageDescriptions.Add(pageString.ToString());
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;

        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
            }

            container.WithSeparator();

            var pageFooter = $"-# {footerLabel} - Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count}";
            if (footerHint != null)
            {
                pageFooter += $"\n-# {footerHint}";
            }

            container.WithTextDisplay(pageFooter);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    public async Task<ResponseModel> TopGenresAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        TopListSettings topListSettings,
        ResponseMode mode)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        var authorUrl = $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}";
        var requesterName = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);
        var userTitle = userSettings.DifferentUser
            ? $"{userSettings.DisplayName}, requested by {requesterName}"
            : requesterName;
        var userTitleWithLink = userSettings.DifferentUser
            ? $"{StringExtensions.MarkdownLink(userSettings.DisplayName, authorUrl)}, requested by {requesterName}"
            : StringExtensions.MarkdownLink(requesterName, authorUrl);

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000, useCache: true);

            if (!artists.Success || artists.Content == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    $"Something went wrong while trying to get top genres for {userSettings.UserNameLastFm}.");
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.ComponentsV2;
                return response;
            }
        }
        else if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            artists = new Response<TopArtistList>
            {
                Content = new TopArtistList
                {
                    TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                }
            };
        }
        else
        {
            artists = new Response<TopArtistList>
            {
                Content = await this._playService.GetUserTopArtists(userSettings.UserId,
                    timeSettings.PlayDays.GetValueOrDefault())
            };
        }

        if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                "Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                "Please try again later or try a different time period.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.ComponentsV2;
            return response;
        }

        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._dataSourceFactory
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var genres = timeSettings.TimePeriod == TimePeriod.AllTime
            ? await this._genreService.GetTopGenresForUser(userSettings.UserId)
            : await this._genreService.GetTopGenresForTopArtists(artists.Content.TopArtists);
        var previousTopGenres = await this._genreService.GetTopGenresForTopArtists(previousTopArtists);

        if (genres == null || genres.Count == 0)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                "Sorry, no genre data could be found for your top artists in the selected time period.\n\n" +
                "Genre data is sourced from Spotify and may not be available for all artists.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.ComponentsV2;
            return response;
        }

        if (mode == ResponseMode.Image && genres.Any())
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            artists.Content.TopArtists = await this._artistsService.FillArtistImages(artists.Content.TopArtists);

            var genresAsString = genres.Select(s => s.GenreName).Take(1).ToList();
            var userArtistsWithGenres =
                await this._genreService.GetArtistsForGenres(genresAsString, artists.Content.TopArtists);

            var validArtists = userArtistsWithGenres.First().Artists.Select(s => s.ArtistName.ToLower()).ToArray();
            var firstArtistImage =
                artists.Content.TopArtists
                    .FirstOrDefault(f => validArtists.Contains(f.ArtistName.ToLower()) && f.ArtistImageUrl != null)
                    ?.ArtistImageUrl;

            using var image = await this._puppeteerService.GetTopList(userTitle, "Top Genres", "genres",
                timeSettings.Description,
                genres.Count, totalPlays.GetValueOrDefault(), firstArtistImage,
                this._genreService.GetTopListForTopGenres(genres), context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-genres-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var genrePages = genres.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageDescriptions = new List<string>();
        var rnd = new Random().Next(0, 4);

        foreach (var genrePage in genrePages)
        {
            var genrePageString = new StringBuilder();
            foreach (var genre in genrePage)
            {
                var name =
                    $"**{genre.GenreName.Transform(To.TitleCase)}** - *{genre.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(genre.UserPlaycount)}*";

                if (topListSettings.Billboard && previousTopGenres.Any())
                {
                    var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == genre.GenreName);
                    int? previousPosition =
                        previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

                    genrePageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition)
                        .Text);
                }
                else
                {
                    genrePageString.Append($"{counter}. ");
                    genrePageString.AppendLine(name);
                }

                counter++;
            }

            pageDescriptions.Add(genrePageString.ToString());
        }

        var footerBase = new StringBuilder();
        footerBase.Append("Genre source: Spotify");
        if (topListSettings.Billboard)
        {
            footerBase.Append($" · {StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm)}");
        }
        if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
        {
            footerBase.Append(" · View as billboard by adding 'billboard' or 'bb'");
        }

        var footerBaseText = footerBase.ToString();

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;
        response.ResponseType = ResponseType.Paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### Top {timeSettings.Description.ToLower()} artist genres for {userTitleWithLink}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
            }

            container.WithSeparator();

            var pageFooter = $"-# {footerBaseText} - {genres.Count.Format(context.NumberFormat)} total genres";
            if (pageDescriptions.Count > 1)
            {
                pageFooter = $"-# Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} · {footerBaseText} · {genres.Count.Format(context.NumberFormat)} total genres";
            }

            container.WithTextDisplay(pageFooter);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            if (context.SelectMenu != null)
            {
                container.AddComponent(context.SelectMenu);
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    private async Task<(List<string> genres, StringMenuProperties selectMenu)> GetGenreOrRespond(string genreOptions,
        ContextModel context, ResponseModel response, User userSettings, string commandDescription,
        string selectedValue = null,
        string selectCommandId = null, string selectCommandDescription = null)
    {
        var genres = new List<string>();
        StringMenuProperties selectMenu = null;

        if (string.IsNullOrWhiteSpace(genreOptions))
        {
            var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, true,
                userSettings.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                response = GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks,
                    userSettings.UserNameLastFM);
                response.ResponseType = ResponseType.ComponentsV2;
                return (null, selectMenu);
            }

            var artistName = recentTracks.Content.RecentTracks.First().ArtistName;

            var foundGenres = await this._genreService.GetGenresForArtist(artistName);

            if (foundGenres == null)
            {
                var artistCall =
                    await this._dataSourceFactory.GetArtistInfoAsync(artistName, userSettings.UserNameLastFM);
                if (artistCall.Success)
                {
                    var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);

                    if (cachedArtist.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
                    {
                        genres.AddRange(cachedArtist.ArtistGenres.Select(s => s.Name));
                    }
                }
            }
            else
            {
                genres.AddRange(foundGenres);
            }

            var artist = await this._artistsService.GetArtistFromDatabase(artistName);
            if (artist == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    "Sorry, the genre or artist you're searching for does not exist or do not have any stored genres.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.ComponentsV2;
                return (null, selectMenu);
            }

            response.ReferencedMusic = new ReferencedMusic { Artist = artist.Name };

            var headerText = $"### Genres for '{StringExtensions.Sanitize(artistName)}'";
            var genreDescription = new StringBuilder();

            if (genres.Count != 0 && artist.ArtistGenres.Count != 0)
            {
                selectMenu = new StringMenuProperties(InteractionConstants.Genre.GenreSelectMenu)
                    .WithPlaceholder(selectCommandDescription)
                    .WithMinValues(1)
                    .WithMaxValues(1);

                foreach (var artistGenre in artist.ArtistGenres)
                {
                    genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");

                    var selected = selectedValue != null &&
                                   artistGenre.Name.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                    var optionId = StringExtensions.TruncateLongString(
                        $"{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:{selectCommandId}:{artistGenre.Name}:{artist.Name}", 100);
                    selectMenu.AddOption(artistGenre.Name.Transform(To.TitleCase), optionId, selected);
                }
            }
            else
            {
                genreDescription.AppendLine("*No provided genres for this artist.*");
            }

            var sectionText = $"{headerText}\n{genreDescription.ToString().TrimEnd()}";

            var safeForChannel =
                await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, artist.Name);
            if (artist.SpotifyImageUrl != null && safeForChannel == CensorService.CensorResult.Safe)
            {
                var accentColor = await this._artistsService.GetArtistAccentColorAsync(
                    artist.SpotifyImageUrl, artist.Id, artist.Name);
                response.ComponentsContainer.WithAccentColor(accentColor);
                response.ComponentsContainer.WithSection([
                    new TextDisplayProperties(sectionText)
                ], artist.SpotifyImageUrl);
            }
            else
            {
                response.ComponentsContainer.WithTextDisplay(sectionText);
            }

            if (selectMenu != null)
            {
                response.ComponentsContainer.WithSeparator();
                response.ComponentsContainer.WithTextDisplay($"-# Genre source: Spotify\n-# Add a genre to this command to see {commandDescription}");
                response.ComponentsContainer.AddComponent(selectMenu);
            }

            response.ResponseType = ResponseType.ComponentsV2;
            return (null, selectMenu);
        }

        var genreResults = await this._genreService.GetValidGenres(genreOptions);

        if (!genreResults.Any())
        {
            var artist = await this._artistsService.GetArtistFromDatabase(genreOptions);

            if (artist != null)
            {
                var headerText = $"### Genres for '{StringExtensions.Sanitize(artist.Name)}'";

                var genreDescription = new StringBuilder();
                if (artist.ArtistGenres != null && artist.ArtistGenres.Any())
                {
                    selectMenu = new StringMenuProperties(InteractionConstants.Genre.GenreSelectMenu)
                        .WithPlaceholder(selectCommandDescription)
                        .WithMinValues(1)
                        .WithMaxValues(1);

                    foreach (var artistGenre in artist.ArtistGenres)
                    {
                        genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");

                        var selected = selectedValue != null &&
                                       artistGenre.Name.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                        var optionId = StringExtensions.TruncateLongString(
                            $"{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:{selectCommandId}:{artistGenre.Name}:{genreOptions}", 100);
                        selectMenu.AddOption(StringExtensions.TruncateLongString(artistGenre.Name.Transform(To.TitleCase), 25), optionId,
                            isDefault: selected);
                    }
                }
                else
                {
                    genreDescription.AppendLine("*No provided genres for this artist.*");
                }

                response.ReferencedMusic = new ReferencedMusic { Artist = artist.Name };

                var sectionText = $"{headerText}\n{genreDescription.ToString().TrimEnd()}";

                var safeForChannel =
                    await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                        artist.Name);
                if (artist.SpotifyImageUrl != null && safeForChannel == CensorService.CensorResult.Safe)
                {
                    var accentColor = await this._artistsService.GetArtistAccentColorAsync(
                        artist.SpotifyImageUrl, artist.Id, artist.Name);
                    response.ComponentsContainer.WithAccentColor(accentColor);
                    response.ComponentsContainer.WithSection([
                        new TextDisplayProperties(sectionText)
                    ], artist.SpotifyImageUrl);
                }
                else
                {
                    response.ComponentsContainer.WithTextDisplay(sectionText);
                }

                if (selectMenu != null)
                {
                    response.ComponentsContainer.WithSeparator();
                    response.ComponentsContainer.WithTextDisplay($"-# Genre source: Spotify\n-# Add a genre to this command to see {commandDescription}");
                    response.ComponentsContainer.AddComponent(selectMenu);
                }

                response.ResponseType = ResponseType.ComponentsV2;
                return (null, selectMenu);
            }

            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                "Sorry, there are no provided genres for the artist you're searching for.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.ComponentsV2;
            return (null, selectMenu);
        }

        genres = genreResults;

        if (!genres.Any())
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                "Sorry, we don't have any registered genres for the artist you're currently listening to.\n\n" +
                $"Please try again later or manually enter a genre (example: `{context.Prefix}genre hip hop`)");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.ComponentsV2;
            return (null, null);
        }

        if (genreResults.Count > 1)
        {
            selectMenu = await GetGenreSearchOptions(context, userSettings, genreOptions, genreResults, selectCommandId,
                selectedValue);
        }

        return (genres, selectMenu);
    }

    private async Task<StringMenuProperties> GetGenreSearchOptions(ContextModel context, User userSettings,
        string genreOptions, List<string> genreResults, string selectCommandId, string selectedValue)
    {
        StringMenuProperties selectMenu = null;

        var topGenres = await this._genreService.GetTopGenresForUser(userSettings.UserId);

        if (genreResults.Count > 1)
        {
            selectMenu = new StringMenuProperties(InteractionConstants.Genre.GenreSelectMenu)
                .WithPlaceholder("Select similar genres")
                .WithMinValues(1)
                .WithMaxValues(1);

            var topGenresList = topGenres.OrderByDescending(o => o.UserPlaycount).ToList();
            var resultSet = genreResults.ToHashSet();

            var firstResult = topGenresList.FirstOrDefault(f =>
                f.GenreName.Equals(genreResults.First(), StringComparison.OrdinalIgnoreCase));
            var firstOptionId = StringExtensions.TruncateLongString(
                $"{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:{selectCommandId}:{genreResults.First()}:{genreOptions}", 100);
            selectMenu.AddOption(genreResults.First().Transform(To.TitleCase),
                firstOptionId,
                description: firstResult == null
                    ? null
                    : $"{firstResult.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(firstResult.UserPlaycount)}");

            foreach (var genre in topGenresList.Where(w =>
                         resultSet.Contains(w.GenreName) && !w.GenreName.Equals(genreResults.First(),
                             StringComparison.OrdinalIgnoreCase)).Take(24))
            {
                var selected = selectedValue != null &&
                               genre.GenreName.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                var optionId = StringExtensions.TruncateLongString(
                    $"{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:{selectCommandId}:{genre.GenreName}:{genreOptions}", 100);
                selectMenu.AddOption(genre.GenreName.Transform(To.TitleCase), optionId,
                    description: $"{genre.UserPlaycount} {StringExtensions.GetPlaysString(genre.UserPlaycount)}",
                    isDefault: selected);
            }
        }

        if (selectMenu == null || selectMenu.Options.Count() == 1)
        {
            return null;
        }

        return selectMenu;
    }

    public async Task<ResponseModel> GenreAsync(
        ContextModel context,
        string genreOptions,
        UserSettingsModel userSettings,
        Guild guild,
        bool userView = true,
        string originalSearch = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        if (context.ReferencedMessage != null)
        {
            var internalLookup = CommandContextExtensions.GetReferencedMusic(context.ReferencedMessage.Id)
                                 ??
                                 await this._userService.GetReferencedMusic(context.ReferencedMessage.Id);

            if (internalLookup?.Artist != null)
            {
                genreOptions = internalLookup.Artist;
            }
        }

        var user = await this._userService.GetUserForIdAsync(userSettings.UserId);
        var genres = await GetGenreOrRespond(genreOptions, context, response, user, "top artists", null,
            userView ? "genre" : "guild-genre", "Select genre to view top artists");

        if (genres.genres == null)
        {
            return response;
        }

        if (originalSearch != null)
        {
            var tempResponse = new ResponseModel();
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, tempResponse, user, "top artists",
                genres.genres.First(), userView ? "genre" : "guild-genre", "Select genre to view top artists");
            genres.selectMenu = originalSearchResponse.selectMenu;
        }

        List<string> pageDescriptions;
        bool anyMatches;
        string title;
        string view;
        TopGenre displayGenre;
        TopGenre userGenre = null;

        if (userView)
        {
            var userArtistsWithGenres = await this._genreService.GetUserArtistsForGenres(userSettings.UserId, genres.genres);
            userGenre = userArtistsWithGenres.FirstOrDefault();

            if (userGenre == null || !userGenre.Artists.Any())
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    "Sorry, we couldn't find any top artists for your selected genres or we don't have any registered artists for the genres.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.ComponentsV2;
                return response;
            }

            var userGenreArtistPages = userGenre.Artists.ChunkBy(10);

            string userTitle;
            if (!userSettings.DifferentUser)
            {
                userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
            }
            else
            {
                userTitle =
                    $"{userSettings.DisplayName}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            }

            (pageDescriptions, anyMatches) = CreateGenrePageDescriptions(userGenreArtistPages, context.NumberFormat);

            title = $"Top '{userGenre.GenreName.Transform(To.TitleCase)}' artists for {userTitle}";
            view = "User view";
            displayGenre = userGenre;
        }
        else
        {
            var guildArtists = await this._genreService.GetGuildArtistsForGenre(guild.GuildId, genres.genres.First());

            var guildGenre = new TopGenre
            {
                GenreName = genres.genres.First(),
                Artists = guildArtists
            };

            if (!guildGenre.Artists.Any())
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(
                    "Sorry, we don't have any registered artists for the genre you're searching for.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.ComponentsV2;
                return response;
            }

            var userArtistsForComparison = await this._genreService.GetUserArtistsForGenre(userSettings.UserId, genres.genres.First());

            var guildGenreArtistPages = guildGenre.Artists.ChunkBy(10);
            (pageDescriptions, anyMatches) = CreateGenrePageDescriptions(guildGenreArtistPages, context.NumberFormat,
                userArtistsForComparison);

            title = $"Top '{genres.genres.First().Transform(To.TitleCase)}' artists for {context.DiscordGuild.Name}";
            view = "Server view";
            displayGenre = guildGenre;
        }

        var interaction = userView ? InteractionConstants.Genre.GenreGuild : InteractionConstants.Genre.GenreUser;
        var optionEmote =
            userView ? EmojiProperties.Custom(DiscordConstants.Server) : EmojiProperties.Custom(DiscordConstants.User);
        var optionDescription = userView ? "View server overview" : "View user overview";
        var originalSearchValue = !string.IsNullOrWhiteSpace(originalSearch) ? originalSearch : "0";
        var optionId =
            $"{interaction}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:{displayGenre.GenreName}:{originalSearchValue}";

        if (userView && context.DiscordGuild == null)
        {
            optionId = null;
            optionEmote = null;
            optionDescription = null;
        }

        var footerStats = $"{view} · {displayGenre.Artists.Count} total artists · {displayGenre.Artists.Sum(s => s.UserPlaycount)} total plays";

        if (pageDescriptions.Count == 1)
        {
            response.ResponseType = ResponseType.ComponentsV2;

            response.ComponentsContainer.WithTextDisplay($"### {title}");
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay(pageDescriptions[0].TrimEnd());
            response.ComponentsContainer.WithSeparator();
            var footer = new StringBuilder();
            footer.AppendLine("Genre source: Spotify");
            footer.Append(footerStats);
            if (anyMatches)
            {
                footer.AppendLine();
                footer.Append($"Artists {StringExtensions.Sanitize(userSettings.DisplayName)} knows are underlined");
            }

            var footerText = string.Join("\n", footer.ToString().Split('\n').Select(l => $"-# {l}"));
            response.ComponentsContainer.WithTextDisplay(footerText);

            if (optionId != null)
            {
                var actionRow = new ActionRowProperties()
                    .WithButton(optionDescription, customId: optionId, emote: optionEmote, style: ButtonStyle.Secondary);
                response.ComponentsContainer.WithActionRow(actionRow);
            }

            if (genres.selectMenu != null)
            {
                response.ComponentsContainer.AddComponent(genres.selectMenu);
            }
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;

            var selectMenu = genres.selectMenu;
            var paginator = new ComponentPaginatorBuilder()
                .WithPageFactory(GeneratePage)
                .WithPageCount(Math.Max(1, pageDescriptions.Count))
                .WithActionOnTimeout(ActionOnStop.DisableInput);

            response.ComponentPaginator = paginator;

            IPage GeneratePage(IComponentPaginator p)
            {
                var container = new ComponentContainerProperties();

                container.WithTextDisplay($"### {title}");
                container.WithSeparator();

                var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
                if (currentPage != null)
                {
                    container.WithTextDisplay(currentPage.TrimEnd());
                }

                container.WithSeparator();
                var pageFooter = new StringBuilder();
                pageFooter.AppendLine($"Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} · Genre source: Spotify");
                pageFooter.Append(footerStats);
                if (anyMatches)
                {
                    pageFooter.AppendLine();
                    pageFooter.Append($"Artists {StringExtensions.Sanitize(userSettings.DisplayName)} knows are underlined");
                }

                var pageFooterText = string.Join("\n", pageFooter.ToString().Split('\n').Select(l => $"-# {l}"));
                container.WithTextDisplay(pageFooterText);

                var navRow = new ActionRowProperties()
                    .AddFirstButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesFirst))
                    .AddPreviousButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious))
                    .AddNextButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesNext))
                    .AddLastButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesLast));

                if (optionId != null)
                {
                    navRow.WithButton(customId: optionId, emote: optionEmote, label: null, style: ButtonStyle.Secondary);
                }

                container.WithActionRow(navRow);

                if (selectMenu != null)
                {
                    container.AddComponent(selectMenu);
                }

                return new PageBuilder()
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithMessageFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([container])
                    .Build();
            }
        }

        return response;
    }

    private static (List<string> pageDescriptions, bool anyMatches) CreateGenrePageDescriptions(
        List<List<TopArtist>> topArtists,
        NumberFormat numberFormat,
        IReadOnlyCollection<TopArtist> allUserTopArtists = null)
    {
        var pageDescriptions = new List<string>();
        if (!topArtists.Any())
        {
            pageDescriptions.Add("No results. Try the other view with the button below.");
            return (pageDescriptions, false);
        }

        var counter = 1;
        var anyMatches = false;
        foreach (var genreArtistPage in topArtists)
        {
            var genrePageString = new StringBuilder();
            foreach (var genreArtist in genreArtistPage)
            {
                var counterString = $"{counter}. ";
                var match = false;
                if (allUserTopArtists != null)
                {
                    var userTopArtist = allUserTopArtists.FirstOrDefault(f =>
                        string.Equals(f.ArtistName, genreArtist.ArtistName, StringComparison.OrdinalIgnoreCase));
                    if (userTopArtist != null)
                    {
                        match = true;
                        anyMatches = true;
                    }
                }

                genrePageString.Append(counterString);

                if (match)
                {
                    genrePageString.Append("__");
                }

                genrePageString.Append($"**{StringExtensions.Sanitize(genreArtist.ArtistName)}**");

                if (match)
                {
                    genrePageString.Append("__");
                }

                genrePageString.Append(
                    $" - *{genreArtist.UserPlaycount.Format(numberFormat)} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)}*");
                genrePageString.AppendLine();

                counter++;
            }

            pageDescriptions.Add(genrePageString.ToString());
        }

        return (pageDescriptions, anyMatches);
    }

    public async Task<ResponseModel> WhoKnowsGenreAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string genreValues,
        string originalSearch = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var genres = await GetGenreOrRespond(genreValues, context, response, context.ContextUser, "WhoKnows genre",
            null, "whoknows", "Select genre to view WhoKnows");

        if (genres.genres == null)
        {
            return response;
        }

        if (originalSearch != null)
        {
            var tempResponse = new ResponseModel();
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, tempResponse, context.ContextUser,
                "WhoKnows genre", genres.genres.First(), "whoknows", "Select genre to view WhoKnows");
            genres.selectMenu = originalSearchResponse.selectMenu;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithGenre = await this._genreService.GetGuildUsersForGenre(guild.GuildId, genres.genres.First(), guildUsers);

        var discordGuildUser = await context.DiscordGuild.GetCachedGuildUserAsync(context.ContextUser.DiscordUserId);
        var currentUser =
            await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);

        var (filterStats, filteredUsersWithGenre) =
            WhoKnowsService.FilterWhoKnowsObjects(usersWithGenre, guildUsers, guild, context.ContextUser.UserId);

        var title = $"{genres.genres.First().Transform(To.TitleCase)} in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.DiscordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
        {
            footer.AppendLine($"Missing members? Update with {context.Prefix}refreshmembers");
        }

        if (filteredUsersWithGenre.Any() && filteredUsersWithGenre.Count > 1)
        {
            var serverListeners = filteredUsersWithGenre.Count;
            var serverPlaycount = filteredUsersWithGenre.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithGenre.Average(a => a.Playcount);

            footer.Append($"Genre - ");
            footer.Append($"{serverListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append($"{serverPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.Append($"{((int)avgServerPlaycount).Format(context.NumberFormat)} avg");
            footer.AppendLine();
        }

        if (filterStats.FullDescription != null)
        {
            footer.AppendLine(filterStats.FullDescription);
        }

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithGenre,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString());

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(filteredUsersWithGenre, context.ContextUser.UserId,
                PrivacyLevel.Server, context.NumberFormat, doNotLinkEmojis: true);
        if (filteredUsersWithGenre.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this genre.";
        }

        response.ComponentsContainer.WithTextDisplay($"### {title}");
        response.ComponentsContainer.WithSeparator();
        response.ComponentsContainer.WithTextDisplay(serverUsers);

        var footerText = footer.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            response.ComponentsContainer.WithSeparator();
            var footerLines = string.Join("\n", footerText.Split('\n').Select(l => $"-# {l}"));
            response.ComponentsContainer.WithTextDisplay(footerLines);
        }

        if (genres.selectMenu != null)
        {
            response.ComponentsContainer.AddComponent(genres.selectMenu);
        }

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowsGenreAsync(
        ContextModel context,
        WhoKnowsResponseMode mode,
        string genreValues,
        string originalSearch = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        if (context.ContextUser.Friends?.Any() != true)
        {
            response.ComponentsContainer.WithTextDisplay("We couldn't find any friends. To add friends:\n" +
                                           $"`{context.Prefix}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`\n\n" +
                                           $"Or right-click a user, go to apps and click 'Add as friend'");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var genres = await GetGenreOrRespond(genreValues, context, response, context.ContextUser,
            "Friends WhoKnow genre", null, "friendwhoknows", "Select genre to view Friends WhoKnow");

        if (genres.genres == null)
        {
            return response;
        }

        if (originalSearch != null)
        {
            var tempResponse = new ResponseModel();
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, tempResponse, context.ContextUser,
                "Friends WhoKnow genre", genres.genres.First(), "friendwhoknows",
                "Select genre to view Friends WhoKnow");
            genres.selectMenu = originalSearchResponse.selectMenu;
        }

        IDictionary<int, FullGuildUser> guildUsers = null;
        Guild guild = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);
            guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);
        }

        var usersWithGenre =
            await this._genreService.GetFriendUsersForGenre(context.ContextUser.UserId,
                genres.genres.First(), guildUsers, context.ContextUser.Friends);

        if (context.DiscordGuild != null)
        {
            var discordGuildUser = await context.DiscordGuild.GetCachedGuildUserAsync(context.ContextUser.DiscordUserId);
            var currentUser =
                await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
            await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);
        }

        var title = $"{genres.genres.First().Transform(To.TitleCase)} with friends";

        var footer = new StringBuilder();

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        footer.AppendLine($"Friends WhoKnow genre for {userTitle}");

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer.AppendLine(
                $"{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible");
        }

        if (usersWithGenre.Any() && usersWithGenre.Count > 1)
        {
            var serverListeners = usersWithGenre.Count;
            var serverPlaycount = usersWithGenre.Sum(a => a.Playcount);
            var avgServerPlaycount = usersWithGenre.Average(a => a.Playcount);

            footer.Append($"Genre - ");
            footer.Append($"{serverListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append($"{serverPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.Append($"{((int)avgServerPlaycount).Format(context.NumberFormat)} avg");
            footer.AppendLine();
        }

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(usersWithGenre.ToList(),
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString());

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithGenre.ToList(), context.ContextUser.UserId,
            PrivacyLevel.Server, context.NumberFormat, doNotLinkEmojis: true);
        if (usersWithGenre.Count == 0)
        {
            serverUsers = "None of your friends have listened to this genre.";
        }

        response.ComponentsContainer.WithTextDisplay($"### {title}");
        response.ComponentsContainer.WithSeparator();
        response.ComponentsContainer.WithTextDisplay(serverUsers);

        var footerText = footer.ToString().TrimEnd();
        if (!string.IsNullOrWhiteSpace(footerText))
        {
            response.ComponentsContainer.WithSeparator();
            var footerLines = string.Join("\n", footerText.Split('\n').Select(l => $"-# {l}"));
            response.ComponentsContainer.WithTextDisplay(footerLines);
        }

        if (genres.selectMenu != null)
        {
            response.ComponentsContainer.AddComponent(genres.selectMenu);
        }

        return response;
    }
}
