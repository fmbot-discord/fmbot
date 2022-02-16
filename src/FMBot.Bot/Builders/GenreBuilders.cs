using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
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

    public GenreBuilders(UserService userService, GuildService guildService, GenreService genreService, WhoKnowsArtistService whoKnowsArtistService, PlayService playService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._genreService = genreService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
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
}
