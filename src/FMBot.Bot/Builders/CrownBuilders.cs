using FMBot.Bot.Models;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;
using System.Text;
using System.Web;
using System;
using System.Linq;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;

namespace FMBot.Bot.Builders;

public class CrownBuilders
{
    private readonly CrownService _crownService;
    private readonly UserService _userService;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;

    public CrownBuilders(CrownService crownService, ArtistsService artistsService, IDataSourceFactory dataSourceFactory, UserService userService, GuildService guildService)
    {
        this._crownService = crownService;
        this._artistsService = artistsService;
        this._dataSourceFactory = dataSourceFactory;
        this._userService = userService;
        this._guildService = guildService;
    }

    public async Task<ResponseModel> CrownAsync(
        ContextModel context,
        Guild guild,
        string artistValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (guild.CrownsDisabled == true)
        {
            response.Text = "Crown functionality has been disabled in this server.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        if (string.IsNullOrWhiteSpace(artistValues))
        {
            var recentTracks =
                await this._dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, sessionKey: context.ContextUser.SessionKeyLastFm, useCache: true);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, context.ContextUser.UserNameLastFM);
            }

            var currentTrack = recentTracks.Content.RecentTracks[0];
            artistValues = currentTrack.ArtistName;
        }

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistValues, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var artistCrowns = await this._crownService.GetCrownsForArtist(guild.GuildId, artistSearch.Artist.ArtistName);

        var userIds = artistCrowns.Select(s => s.UserId).ToHashSet();
        var users = await this._userService.GetMultipleUsers(userIds);

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        if (!artistCrowns.Any(a => a.Active))
        {
            response.Embed.WithDescription($"No known crowns for the artist `{artistSearch.Artist.ArtistName}`. \n" +
                                        $"Be the first to claim the crown with `{context.Prefix}whoknows`!");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var currentCrown = artistCrowns
            .Where(w => w.Active)
                .OrderByDescending(o => o.CurrentPlaycount)
                .First();


        var userArtistUrl =
            $"{LastfmUrlExtensions.GetUserUrl(users[currentCrown.UserId].UserNameLastFM)}/library/music/{HttpUtility.UrlEncode(artistSearch.Artist.ArtistName)}";

        guildUsers.TryGetValue(currentCrown.UserId, out var currentGuildUser);
        response.Embed.AddField("Current crown holder", CrownToString(currentGuildUser, users[currentCrown.UserId], currentCrown, currentCrown.Created, userArtistUrl));

        var lastCrownCreateDate = currentCrown.Created;
        if (artistCrowns.Count > 1)
        {
            var crownHistory = new StringBuilder();

            foreach (var artistCrown in artistCrowns.Take(10).Where(w => !w.Active))
            {
                guildUsers.TryGetValue(artistCrown.UserId, out var guildUser);

                crownHistory.AppendLine(CrownToString(guildUser, users[artistCrown.UserId], artistCrown, lastCrownCreateDate));

                lastCrownCreateDate = artistCrown.Created;
            }

            response.Embed.AddField("Crown history", crownHistory.ToString());

            if (artistCrowns.Count(w => !w.Active) > 10)
            {
                crownHistory.AppendLine($"*{artistCrowns.Count(w => !w.Active) - 10} more steals hidden..*");

                var firstCrown = artistCrowns.OrderBy(o => o.Created).First();

                guildUsers.TryGetValue(firstCrown.UserId, out var firstGuildUser);

                response.Embed.AddField("First crownholder", CrownToString(firstGuildUser, users[firstCrown.UserId], firstCrown, lastCrownCreateDate));
            }
        }

        response.Embed.WithTitle($"Crown info for {currentCrown.ArtistName}");

        var embedDescription = new StringBuilder();

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    private static string CrownToString(FullGuildUser guildUser, User user, UserCrown crown, DateTime lastCrownCreateDate, string url = null)
    {
        var description = new StringBuilder();

        description.Append($"**<t:{((DateTimeOffset)crown.Created).ToUnixTimeSeconds()}:D>** to **<t:{((DateTimeOffset)lastCrownCreateDate).ToUnixTimeSeconds()}:D>** — ");

        if (url != null)
        {
            description.Append($"**[{guildUser?.UserName ?? user.UserNameLastFM}]({url})** — ");
        }
        else
        {
            description.Append($"**{guildUser?.UserName ?? user.UserNameLastFM}** — ");
        }

        description.Append($"*{crown.StartPlaycount} to {crown.CurrentPlaycount} plays*");

        return description.ToString();
    }
}
