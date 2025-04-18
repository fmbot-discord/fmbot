using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
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
using FMBot.Persistence.Domain.Models;
using Humanizer;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

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

        ICollection<GuildArtist> topGuildArtists;
        IList<GuildGenre> previousTopGuildGenres = null;

        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildArtists =
                await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuildWithListeners(guild.GuildId,
                    guildListSettings.OrderType);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId,
                guildListSettings.AmountOfDaysWithBillboard);

            topGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.StartDateTime,
                guildListSettings.OrderType, 8000, true);
            var previousTopGuildArtists = PlayService.GetGuildTopArtists(plays,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, 8000, true);

            previousTopGuildGenres =
                await this._genreService.GetTopGenresForGuildArtists(previousTopGuildArtists,
                    guildListSettings.OrderType);
        }

        var topGuildGenres =
            await this._genreService.GetTopGenresForGuildArtists(topGuildArtists, guildListSettings.OrderType);

        var title = $"Top {guildListSettings.TimeDescription.ToLower()} genres in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific genre listeners with '{context.Prefix}whoknowsgenre'");
                break;
            case 2:
                footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                break;
            case 3:
                footer.AppendLine($"Available sorting options: plays and listeners");
                break;
        }

        var genrePages = topGuildGenres.Chunk(12).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
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

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{genrePages.Count}");
            pageFooter.Append(footer);

            pages.Add(new PageBuilder()
                .WithTitle(title)
                .WithDescription(pageString.ToString())
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
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

        var pages = new List<PageBuilder>();

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (!userSettings.DifferentUser)
        {
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist genres for {userTitle}");
        response.EmbedAuthor.WithUrl(
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}");

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000, useCache: true);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "topgenres", context.DiscordUser);
                response.CommandResponse = CommandResponse.LastFmError;
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
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                $"Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
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

        var genres = await this._genreService.GetTopGenresForTopArtists(artists.Content.TopArtists);
        var previousTopGenres = await this._genreService.GetTopGenresForTopArtists(previousTopArtists);

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

            var image = await this._puppeteerService.GetTopList(userTitle, "Top Genres", "genres",
                timeSettings.Description,
                genres.Count, totalPlays.GetValueOrDefault(), firstArtistImage,
                this._genreService.GetTopListForTopGenres(genres), context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"top-genres-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var genrePages = genres.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;
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

            var footer = new StringBuilder();
            footer.AppendLine("Genre source: Spotify");
            footer.AppendLine($"Page {pageCounter}/{genrePages.Count} - {genres.Count.Format(context.NumberFormat)} total genres");

            if (topListSettings.Billboard)
            {
                footer.AppendLine(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine("View this list as a billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(genrePageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages, selectMenuBuilder: context.SelectMenu);
        return response;
    }

    private async Task<(List<string> genres, SelectMenuBuilder selectMenu)> GetGenreOrRespond(string genreOptions,
        ContextModel context, ResponseModel response, User userSettings, string commandDescription,
        string selectedValue = null,
        string selectCommandId = null, string selectCommandDescription = null)
    {
        var genres = new List<string>();
        SelectMenuBuilder selectMenu = null;

        if (string.IsNullOrWhiteSpace(genreOptions))
        {
            var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, true,
                userSettings.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                response = GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks,
                    userSettings.UserNameLastFM);
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
                response.Embed.WithDescription(
                    "Sorry, the genre or artist you're searching for does not exist or do not have any stored genres.");

                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return (null, selectMenu);
            }

            response.Embed.WithTitle($"Genres for '{artistName}'");

            var safeForChannel =
                await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, artist.Name);
            if (artist.SpotifyImageUrl != null && safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
            }

            PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, artist.Name);

            if (genres.Any() && genres.Count > 0)
            {
                var genreDescription = new StringBuilder();
                selectMenu = new SelectMenuBuilder()
                    .WithPlaceholder(selectCommandDescription)
                    .WithCustomId(InteractionConstants.Genre.GenreSelectMenu)
                    .WithMinValues(1)
                    .WithMaxValues(1);

                foreach (var artistGenre in artist.ArtistGenres)
                {
                    genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");

                    var selected = selectedValue != null &&
                                   artistGenre.Name.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                    var optionId =
                        $"{userSettings.DiscordUserId}~{context.ContextUser.DiscordUserId}~{selectCommandId}~{artistGenre.Name}~{artist.Name}";
                    selectMenu.AddOption(artistGenre.Name.Transform(To.TitleCase), optionId, isDefault: selected);
                }

                response.Embed.WithFooter($"Genre source: Spotify\n" +
                                          $"Add a genre to this command to see {commandDescription}");

                response.Components = new ComponentBuilder().WithSelectMenu(selectMenu);

                response.Embed.WithDescription(genreDescription.ToString());

                response.ResponseType = ResponseType.Embed;
                return (null, selectMenu);
            }

            response.Embed.WithDescription("*No provided genres for this artist.*");
            response.ResponseType = ResponseType.Embed;
            return (null, selectMenu);
        }

        var genreResults = await this._genreService.GetValidGenres(genreOptions);

        if (!genreResults.Any())
        {
            var artist = await this._artistsService.GetArtistFromDatabase(genreOptions);

            if (artist != null)
            {
                response.Embed.WithTitle($"Genres for '{artist.Name}'");

                var genreDescription = new StringBuilder();
                if (artist.ArtistGenres != null && artist.ArtistGenres.Any())
                {
                    selectMenu = new SelectMenuBuilder()
                        .WithPlaceholder(selectCommandDescription)
                        .WithCustomId(InteractionConstants.Genre.GenreSelectMenu)
                        .WithMinValues(1)
                        .WithMaxValues(1);

                    foreach (var artistGenre in artist.ArtistGenres)
                    {
                        genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");

                        var selected = selectedValue != null &&
                                       artistGenre.Name.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                        var optionId =
                            $"{userSettings.DiscordUserId}~{context.ContextUser.DiscordUserId}~{selectCommandId}~{artistGenre.Name}~{genreOptions}";
                        selectMenu.AddOption(artistGenre.Name.Transform(To.TitleCase), optionId, isDefault: selected);
                    }

                    response.Embed.WithFooter($"Genre source: Spotify\n" +
                                              $"Add a genre to this command to see {commandDescription}");

                    response.Components = new ComponentBuilder().WithSelectMenu(selectMenu);
                }
                else
                {
                    genreDescription.AppendLine("*No provided genres for this artist.*");
                }

                var safeForChannel =
                    await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                        artist.Name);
                if (artist.SpotifyImageUrl != null && safeForChannel == CensorService.CensorResult.Safe)
                {
                    response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                }

                PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, artist.Name);

                response.Embed.WithDescription(genreDescription.ToString());
                response.ResponseType = ResponseType.Embed;
                return (null, selectMenu);
            }

            response.Embed.WithDescription(
                "Sorry, there are no provided genres for the artist you're searching for.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return (null, selectMenu);
        }

        genres = genreResults;

        if (!genres.Any())
        {
            response.Embed.WithDescription(
                "Sorry, we don't have any registered genres for the artist you're currently listening to.\n\n" +
                $"Please try again later or manually enter a genre (example: `{context.Prefix}genre hip hop`)");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return (null, null);
        }

        if (genreResults.Count > 1)
        {
            selectMenu = await GetGenreSearchOptions(context, userSettings, genreOptions, genreResults, selectCommandId,
                selectedValue);
        }

        return (genres, selectMenu);
    }

    private async Task<SelectMenuBuilder> GetGenreSearchOptions(ContextModel context, User userSettings,
        string genreOptions, List<string> genreResults, string selectCommandId, string selectedValue)
    {
        SelectMenuBuilder selectMenu = null;

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);
        var topGenres = await this._genreService.GetTopGenresForTopArtists(topArtists);

        if (genreResults.Count > 1)
        {
            selectMenu = new SelectMenuBuilder()
                .WithPlaceholder("Select similar genres")
                .WithCustomId(InteractionConstants.Genre.GenreSelectMenu)
                .WithMinValues(1)
                .WithMaxValues(1);

            var topGenresList = topGenres.OrderByDescending(o => o.UserPlaycount).ToList();
            var resultSet = genreResults.ToHashSet();

            var firstResult = topGenresList.FirstOrDefault(f =>
                f.GenreName.Equals(genreResults.First(), StringComparison.OrdinalIgnoreCase));
            selectMenu.AddOption(genreResults.First().Transform(To.TitleCase),
                $"{userSettings.DiscordUserId}~{context.ContextUser.DiscordUserId}~{selectCommandId}~{genreResults.First()}~{genreOptions}",
                description: firstResult == null
                    ? null
                    : $"{firstResult.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(firstResult.UserPlaycount)}");

            foreach (var genre in topGenresList.Where(w =>
                         resultSet.Contains(w.GenreName) && !w.GenreName.Equals(genreResults.First(),
                             StringComparison.OrdinalIgnoreCase)).Take(24))
            {
                var selected = selectedValue != null &&
                               genre.GenreName.Equals(selectedValue, StringComparison.OrdinalIgnoreCase);

                var optionId =
                    $"{userSettings.DiscordUserId}~{context.ContextUser.DiscordUserId}~{selectCommandId}~{genre.GenreName}~{genreOptions}";
                selectMenu.AddOption(genre.GenreName.Transform(To.TitleCase), optionId,
                    description: $"{genre.UserPlaycount} {StringExtensions.GetPlaysString(genre.UserPlaycount)}",
                    isDefault: selected);
            }
        }

        if (selectMenu == null || selectMenu.Options.Count == 1)
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
            ResponseType = ResponseType.Embed
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
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, response, user, "top artists",
                genres.genres.First(), userView ? "genre" : "guild-genre", "Select genre to view top artists");
            genres.selectMenu = originalSearchResponse.selectMenu;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);
        if (topArtists.Count < 100)
        {
            if (userSettings.DifferentUser)
            {
                response.Embed.WithDescription(
                    $"Sorry, {userSettings.UserNameLastFm} doesn't have enough top artists yet to use this command (must have at least 100 - {userSettings.UserNameLastFm} has {topArtists.Count}).\n\n" +
                    "Please try again later.");
            }
            else
            {
                response.Embed.WithDescription(
                    $"Sorry, you don't have enough top artists yet to use this command (must have at least 100 - you have {topArtists.Count}).\n\n" +
                    "Please try again later.");
            }

            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userArtistsWithGenres = await this._genreService.GetArtistsForGenres(genres.genres, topArtists);
        var userGenre = userArtistsWithGenres.FirstOrDefault();

        List<PageBuilder> pages;
        if (userView)
        {
            if (userGenre == null || !userGenre.Artists.Any())
            {
                response.Embed.WithDescription(
                    "Sorry, we couldn't find any top artists for your selected genres or we don't have any registered artists for the genres.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
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

            pages = CreateGenrePageBuilder(userGenreArtistPages, response.EmbedAuthor, userGenre, "User view" ,context.NumberFormat);

            response.EmbedAuthor.WithName(
                $"Top '{userGenre.GenreName.Transform(To.TitleCase)}' artists for {userTitle}");
        }
        else
        {
            var topGuildArtists =
                await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuild(guild.GuildId, OrderType.Playcount,
                    limit: null);

            var guildArtistsWithGenres = await this._genreService.GetArtistsForGenres(genres.genres,
                topGuildArtists.Select(s => new TopArtist
                {
                    ArtistName = s.ArtistName,
                    UserPlaycount = s.TotalPlaycount
                }).ToList());

            var guildGenre = guildArtistsWithGenres.First();

            if (!guildGenre.Artists.Any())
            {
                response.Embed.WithDescription(
                    "Sorry, we don't have any registered artists for the genre you're searching for.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            var guildGenreArtistPages = guildGenre.Artists.ChunkBy(10);
            pages = CreateGenrePageBuilder(guildGenreArtistPages, response.EmbedAuthor, guildGenre, "Server view", context.NumberFormat,
                userSettings.DisplayName, userGenre?.Artists);

            response.EmbedAuthor.WithName(
                $"Top '{genres.genres.First().Transform(To.TitleCase)}' artists for {context.DiscordGuild.Name}");
        }

        if (!context.SlashCommand && !userSettings.DifferentUser)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        var interaction = userView ? InteractionConstants.Genre.GenreGuild : InteractionConstants.Genre.GenreUser;
        var optionEmote =
            userView ? Emote.Parse("<:server:961685224041902140>") : Emote.Parse("<:user:961687127249260634>");
        var optionDescription = userView ? "View server overview" : "View user overview";
        var originalSearchValue = !string.IsNullOrWhiteSpace(originalSearch) ? originalSearch : "0";
        var optionId =
            $"{interaction}~{userSettings.DiscordUserId}~{context.ContextUser.DiscordUserId}~{userGenre.GenreName}~{originalSearchValue}";

        if (userView && context.DiscordGuild == null)
        {
            optionId = null;
            optionEmote = null;
            optionDescription = null;
        }

        if (pages.Count == 1)
        {
            response.ResponseType = ResponseType.Embed;
            response.SinglePageToEmbedResponseWithButton(pages.First(), optionId, optionEmote, optionDescription,
                genres.selectMenu);
        }
        else
        {
            if (genres.selectMenu != null)
            {
                response.StaticPaginator =
                    StringService.BuildStaticPaginatorWithSelectMenu(pages, genres.selectMenu, optionId, optionEmote);
            }
            else
            {
                response.StaticPaginator = StringService.BuildStaticPaginator(pages, optionId, optionEmote);
            }

            response.ResponseType = ResponseType.Paginator;
        }

        return response;
    }

    private static List<PageBuilder> CreateGenrePageBuilder(List<List<TopArtist>> topArtists,
        EmbedAuthorBuilder author,
        TopGenre topGenre,
        string view,
        NumberFormat numberFormat,
        string userTitle = null,
        IReadOnlyCollection<TopArtist> allUserTopArtists = null)
    {
        var pages = new List<PageBuilder>();
        if (!topArtists.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No results. Try the other paginator with the button on the bottom right below.")
                .WithAuthor(author));
        }

        var counter = 1;
        var pageCounter = 1;
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

            var footer = new StringBuilder();

            footer.AppendLine($"Genre source: Spotify - {view}");

            if (anyMatches)
            {
                footer.AppendLine($"Artists {StringExtensions.Sanitize(userTitle)} knows are underlined");
            }

            footer.AppendLine(
                $"Page {pageCounter}/{topArtists.Count} - {topGenre.Artists.Count} total artists - {topGenre.Artists.Sum(s => s.UserPlaycount)} total plays");

            pages.Add(new PageBuilder()
                .WithDescription(genrePageString.ToString())
                .WithAuthor(author)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        return pages;
    }

    public async Task<ResponseModel> WhoKnowsGenreAsync(
        ContextModel context,
        string genreValues,
        string originalSearch = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var genres = await GetGenreOrRespond(genreValues, context, response, context.ContextUser, "WhoKnows genre",
            null, "whoknows", "Select genre to view WhoKnows");

        if (genres.genres == null)
        {
            return response;
        }

        if (originalSearch != null)
        {
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, response, context.ContextUser,
                "WhoKnows genre", genres.genres.First(), "whoknows", "Select genre to view WhoKnows");
            genres.selectMenu = originalSearchResponse.selectMenu;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var guildTopUserArtists =
            await this._genreService.GetTopUserArtistsForGuildAsync(guild.GuildId, genres.genres.First());
        var usersWithGenre = await this._genreService.GetUsersWithGenreForUserArtists(guildTopUserArtists, guildUsers);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId);
        var currentUser =
            await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);

        var (filterStats, filteredUsersWithGenre) = WhoKnowsService.FilterWhoKnowsObjects(usersWithGenre, guildUsers, guild, context.ContextUser.UserId);

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(filteredUsersWithGenre, context.ContextUser.UserId,
                PrivacyLevel.Server, context.NumberFormat);
        if (filteredUsersWithGenre.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this genre.";
        }

        response.Embed.WithDescription(serverUsers);

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

        if (genres.selectMenu != null)
        {
            response.Components = new ComponentBuilder().WithSelectMenu(genres.selectMenu);
        }

        response.Embed.WithTitle($"{genres.genres.First().Transform(To.TitleCase)} in {context.DiscordGuild.Name}");
        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowsGenreAsync(
        ContextModel context,
        string genreValues,
        string originalSearch = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.Friends?.Any() != true)
        {
            response.Embed.WithDescription("We couldn't find any friends. To add friends:\n" +
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
            var originalSearchResponse = await GetGenreOrRespond(originalSearch, context, response, context.ContextUser,
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

        var guildTopUserArtists =
            await this._genreService.GetTopUserArtistsForUserFriendsAsync(context.ContextUser.UserId,
                genres.genres.First());
        var usersWithGenre =
            await this._genreService.GetUsersWithGenreForUserArtists(guildTopUserArtists, guildUsers,
                context.ContextUser.Friends);

        if (context.DiscordGuild != null)
        {
            var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId);
            var currentUser =
                await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, context.ContextUser);
            await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, currentUser.UserId, guild);
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithGenre.ToList(), context.ContextUser.UserId,
            PrivacyLevel.Server, context.NumberFormat);
        if (usersWithGenre.Count == 0)
        {
            serverUsers = "None of your friends have listened to this genre.";
        }

        response.Embed.WithDescription(serverUsers);

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

        if (genres.selectMenu != null)
        {
            response.Components = new ComponentBuilder().WithSelectMenu(genres.selectMenu);
        }

        response.Embed.WithTitle($"{genres.genres.First().Transform(To.TitleCase)} with friends");
        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }
}
