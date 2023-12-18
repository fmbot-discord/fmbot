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
using Fergun.Interactive;
using FMBot.Domain.Enums;
using System.Collections.Generic;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.ThirdParty;

namespace FMBot.Bot.Builders;

public class CrownBuilders
{
    private readonly CrownService _crownService;
    private readonly UserService _userService;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SpotifyService _spotifyService;

    public CrownBuilders(CrownService crownService, ArtistsService artistsService, IDataSourceFactory dataSourceFactory, UserService userService, GuildService guildService, SpotifyService spotifyService)
    {
        this._crownService = crownService;
        this._artistsService = artistsService;
        this._dataSourceFactory = dataSourceFactory;
        this._userService = userService;
        this._guildService = guildService;
        this._spotifyService = spotifyService;
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

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName);

        var artistCrowns = await this._crownService.GetCrownsForArtist(guild.GuildId, artistSearch.Artist.ArtistName);

        var userIds = artistCrowns.Select(s => s.UserId).ToHashSet();
        var users = await this._userService.GetMultipleUsers(userIds);

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        response.Components = new ComponentBuilder()
            .WithButton("WhoKnows", $"{InteractionConstants.Artist.WhoKnows}-{cachedArtist.Id}", style: ButtonStyle.Secondary, emote: new Emoji("📋"));

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

        response.Embed.WithTitle($"Crown for {currentCrown.ArtistName}");

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

    public async Task<ResponseModel> CrownOverviewAsync(
        ContextModel context,
        Guild guild,
        UserSettingsModel userSettings,
        CrownViewType crownViewType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        if (guild.CrownsDisabled == true)
        {
            response.Text = "Crown functionality has been disabled in this server.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var userCrowns = await this._crownService.GetCrownsForUser(guild, userSettings.UserId, crownViewType);

        var crownType = crownViewType == CrownViewType.Stolen ? "Stolen crowns" : "Crowns";
        var title = userSettings.DifferentUser
            ? $"{crownType} for {userSettings.UserNameLastFm}, requested by {userTitle}"
            : $"{crownType} for {userTitle}";

        if (!userCrowns.Any())
        {
            if (crownViewType == CrownViewType.Stolen)
            {
                response.Embed.WithDescription($"You or the user you're searching don't have any crowns that got stolen yet.");
            }
            else
            {
                response.Embed.WithDescription($"You or the user you're searching for don't have any crowns yet. \n\n" +
                                               $"Use `{context.Prefix}whoknows` to start getting crowns!");
            }
            
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var pages = new List<PageBuilder>();

        var crownPages = userCrowns.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var crownPage in crownPages)
        {
            var crownPageString = new StringBuilder();
            foreach (var userCrown in crownPage)
            {
                crownPageString.Append($"{counter}. **{userCrown.ArtistName}** — *{userCrown.CurrentPlaycount} plays*");

                if (crownViewType != CrownViewType.Stolen)
                {
                    crownPageString.Append($" — Claimed <t:{((DateTimeOffset)userCrown.Created).ToUnixTimeSeconds()}:R>");
                }
                else
                {
                    crownPageString.Append($" — Stolen <t:{((DateTimeOffset)userCrown.Modified).ToUnixTimeSeconds()}:R>");
                }

                crownPageString.AppendLine();

                counter++;
            }

            var footer = new StringBuilder();

            footer.AppendLine($"Page {pageCounter}/{crownPages.Count} - {userCrowns.Count} total crowns");

            switch (crownViewType)
            {
                case CrownViewType.Playcount:
                    footer.AppendLine("Actively held crowns ordered by playcount");
                    break;
                case CrownViewType.Recent:
                    footer.AppendLine("Active crowns that were recently claimed");
                    break;
                case CrownViewType.Stolen:
                    footer.AppendLine("Expired crowns that have recently been stolen");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            footer.AppendLine("Available options: playcount, recent and stolen");

            pages.Add(new PageBuilder()
                .WithDescription(crownPageString.ToString())
                .WithTitle(title)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }
}
