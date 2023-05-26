using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Humanizer;
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
    private readonly LastFmRepository _lastFmRepository;
    private readonly SpotifyService _spotifyService;

    public GenreBuilders(UserService userService, GuildService guildService, GenreService genreService, WhoKnowsArtistService whoKnowsArtistService, PlayService playService, ArtistsService artistsService, LastFmRepository lastFmRepository, SpotifyService spotifyService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._genreService = genreService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
        this._artistsService = artistsService;
        this._lastFmRepository = lastFmRepository;
        this._spotifyService = spotifyService;
    }

    public async Task<ResponseModel> GetGuildGenres(ContextModel context, Guild guild, GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        ICollection<GuildArtist> topGuildArtists;
        IList<GuildGenre> previousTopGuildGenres = null;

        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildArtists = await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuildWithListeners(guild.GuildId, guildListSettings.OrderType);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId,
                guildListSettings.AmountOfDaysWithBillboard);

            topGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.StartDateTime, guildListSettings.OrderType, 8000, true);
            var previousTopGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, 8000, true);

            previousTopGuildGenres = await this._genreService.GetTopGenresForGuildArtists(previousTopGuildArtists, guildListSettings.OrderType);
        }

        var topGuildGenres = await this._genreService.GetTopGenresForGuildArtists(topGuildArtists, guildListSettings.OrderType);

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
                    ? $"`{genre.ListenerCount}` · **{genre.GenreName.Transform(To.TitleCase)}** ({genre.TotalPlaycount} {Extensions.StringExtensions.GetPlaysString(genre.TotalPlaycount)})"
                    : $"`{genre.TotalPlaycount}` · **{genre.GenreName.Transform(To.TitleCase)}** ({genre.ListenerCount} {StringExtensions.GetListenersString(genre.ListenerCount)})";

                if (previousTopGuildGenres != null && previousTopGuildGenres.Any())
                {
                    var previousTopGenre = previousTopGuildGenres.FirstOrDefault(f => f.GenreName == genre.GenreName);
                    int? previousPosition = previousTopGenre == null ? null : previousTopGuildGenres.IndexOf(previousTopGenre);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
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

    public async Task<ResponseModel> GetTopGenres(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        TopListSettings topListSettings)
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
        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000);

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
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                                        $"Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._lastFmRepository
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var genres = await this._genreService.GetTopGenresForTopArtists(artists.Content.TopArtists);
        var previousTopGenres = await this._genreService.GetTopGenresForTopArtists(previousTopArtists);

        var genrePages = genres.ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var genrePage in genrePages)
        {
            var genrePageString = new StringBuilder();
            foreach (var genre in genrePage)
            {
                var name = $"**{genre.GenreName.Transform(To.TitleCase)}** ({genre.UserPlaycount} {StringExtensions.GetPlaysString(genre.UserPlaycount)})";

                if (topListSettings.Billboard && previousTopGenres.Any())
                {
                    var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == genre.GenreName);
                    int? previousPosition = previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

                    genrePageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                }
                else
                {
                    genrePageString.Append($"{counter}\\. ");
                    genrePageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();
            footer.AppendLine("Genre source: Spotify");
            footer.AppendLine($"Page {pageCounter}/{genrePages.Count} - {genres.Count} total genres");

            if (topListSettings.Billboard)
            {
                footer.AppendLine(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard)
            {
                footer.AppendLine("View this list as a billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(genrePageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> GenreAsync(
        ContextModel context,
        string genreOptions,
        Guild guild)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var genres = new List<string>();
        if (string.IsNullOrWhiteSpace(genreOptions))
        {
            var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1, true, context.ContextUser.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                var errorEmbed =
                    GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, context.ContextUser.UserNameLastFM);
                response.Embed = errorEmbed;
                response.CommandResponse = CommandResponse.LastFmError;
                return response;
            }

            var artistName = recentTracks.Content.RecentTracks.First().ArtistName;

            var foundGenres = await this._genreService.GetGenresForArtist(artistName);

            if (foundGenres == null)
            {
                var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistName, context.ContextUser.UserNameLastFM);
                if (artistCall.Success)
                {
                    var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistCall.Content);

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

            if (genres.Any())
            {
                var artist = await this._artistsService.GetArtistFromDatabase(artistName);

                if (artist == null)
                {
                    response.Embed.WithDescription(
                        "Sorry, the genre or artist you're searching for does not exist or do not have any stored genres.");

                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                response.Embed.WithTitle($"Genre info for '{artistName}'");

                var genreDescription = new StringBuilder();
                foreach (var artistGenre in artist.ArtistGenres)
                {
                    genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");
                }

                if (artist?.SpotifyImageUrl != null)
                {
                    response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                }

                response.Embed.WithDescription(genreDescription.ToString());

                response.Embed.WithFooter($"Genre source: Spotify\n" +
                                       $"Add a genre to this command to see top artists");

                response.ResponseType = ResponseType.Embed;
                return response;
            }
        }
        else
        {
            var foundGenre = await this._genreService.GetValidGenre(genreOptions);

            if (foundGenre == null)
            {
                var artist = await this._artistsService.GetArtistFromDatabase(genreOptions);

                if (artist != null)
                {
                    response.Embed.WithTitle($"Genre info for '{artist.Name}'");

                    var genreDescription = new StringBuilder();
                    foreach (var artistGenre in artist.ArtistGenres)
                    {
                        genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");
                    }

                    if (artist?.SpotifyImageUrl != null)
                    {
                        response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                    }

                    response.Embed.WithDescription(genreDescription.ToString());

                    response.Embed.WithFooter($"Genre source: Spotify\n" +
                                           $"Add a genre to this command to see top artists");

                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                response.Embed.WithDescription(
                    "Sorry, the genre or artist you're searching for does not exist or do not have any stored genres.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            genres = new List<string> { foundGenre };
        }

        if (!genres.Any())
        {
            response.Embed.WithDescription(
                "Sorry, we don't have any registered genres for the artist you're currently listening to.\n\n" +
                $"Please try again later or manually enter a genre (example: `{context.Prefix}genre hip hop`)");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(context.ContextUser.UserId, true);
        if (topArtists.Count < 100)
        {
            response.Embed.WithDescription($"Sorry, you don't have enough top artists yet to use this command (must have at least 100 - you have {topArtists.Count}).\n\n" +
                                        "Please try again later.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var topGuildArtists = await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuild(guild.GuildId, OrderType.Playcount, limit: null);

        var userArtistsWithGenres = await this._genreService.GetArtistsForGenres(genres, topArtists);
        var guildArtistsWithGenres = await this._genreService.GetArtistsForGenres(genres, topGuildArtists.Select(s => new TopArtist
        {
            ArtistName = s.ArtistName,
            UserPlaycount = s.TotalPlaycount
        }).ToList());

        if (!userArtistsWithGenres.Any())
        {
            response.Embed.WithDescription("Sorry, we couldn't find any top artists for your selected genres.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        var userGenre = userArtistsWithGenres.First();
        var guildGenre = guildArtistsWithGenres.First();

        if (!userGenre.Artists.Any() || !guildGenre.Artists.Any())
        {
            response.Embed.WithDescription(
                "Sorry, we don't have any registered artists for the genre you're searching for.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        var userGenreArtistPages = userGenre.Artists.ChunkBy(10);
        var guildGenreArtistPages = guildGenre.Artists.ChunkBy(10);

        var results = new Dictionary<string, List<PageBuilder>>
        {
            { "user", GetPageBuilders(userGenreArtistPages, response.EmbedAuthor, userGenre) },
            { "server", GetPageBuilders(guildGenreArtistPages, response.EmbedAuthor, guildGenre, userGenre.Artists) },
        };

        var options = results
            .ToDictionary(x => x.Key, x =>
                new LazyPaginatorBuilder()
                    .WithPageFactory(index => GeneratePage(x.Value, x.Key, index, userGenre.GenreName, x.Key == "server" ? context.DiscordGuild.Name : userTitle))
                    .WithMaxPageIndex(x.Value.Count - 1)
                    .WithActionOnCancellation(ActionOnStop.DisableInput)
                    .WithActionOnTimeout(ActionOnStop.DisableInput)
                    .WithFooter(PaginatorFooter.None)
                    .WithOptions(x.Key == "server" ? DiscordConstants.PaginationGuildEmotes : DiscordConstants.PaginationUserEmotes)
                    .Build() as Paginator);

        var first = options.First().Key;
        var initialPage = GeneratePage(results[first], first, 0, userGenre.GenreName, first == "server" ? context.DiscordGuild.Name : userTitle);

        var pagedSelection = new PagedSelectionBuilder<string>()
            .WithOptions(options)
            .WithSelectionPage(initialPage)
            .WithActionOnTimeout(ActionOnStop.DeleteInput)
            .WithActionOnCancellation(ActionOnStop.DisableInput)
            .Build();

        response.PagedSelection = pagedSelection;
        response.ResponseType = ResponseType.PagedSelection;
        return response;
    }

    private List<PageBuilder> GetPageBuilders(List<List<TopArtist>> topArtists, EmbedAuthorBuilder author, TopGenre topGenre, List<TopArtist> allUserTopArtists = null)
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
        foreach (var genreArtistPage in topArtists)
        {
            var genrePageString = new StringBuilder();
            foreach (var genreArtist in genreArtistPage)
            {
                var counterString = $"{counter}.";
                if (allUserTopArtists != null)
                {
                    var userTopArtist = allUserTopArtists.FirstOrDefault(f => f.ArtistName.ToLower() == genreArtist.ArtistName.ToLower());
                    if (userTopArtist != null)
                    {
                        counterString = $"**{counter}.**";
                    }
                }

                genrePageString.AppendLine($"{counterString} **{genreArtist.ArtistName}** ({genreArtist.UserPlaycount} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)})");
                counter++;
            }

            var footer = $"Genre source: Spotify\n" +
                         $"Page {pageCounter}/{topArtists.Count} - {topGenre.Artists.Count} total artists - {topGenre.Artists.Sum(s => s.UserPlaycount)} total plays";

            pages.Add(new PageBuilder()
                .WithDescription(genrePageString.ToString())
                .WithAuthor(author)
                .WithFooter(footer));
            pageCounter++;
        }

        return pages;
    }

    private static PageBuilder GeneratePage(IReadOnlyList<PageBuilder> pages, string scraper, int index, string genre, string userTitle)
    {
        return new PageBuilder()
            .WithAuthor(pages[index].Author.WithName($"Top '{genre.Transform(To.TitleCase)}' artists for {userTitle}"))
            .WithDescription(pages[index].Description)
            .WithImageUrl(pages[index].Url)
            .WithFooter($"{scraper}\n" + pages[index].Footer.Text);
    }
}
