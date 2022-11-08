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
using FMBot.Domain;
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.Builders;

public class CrownBuilders
{
    private readonly CrownService _crownService;
    private readonly ArtistsService _artistsService;
    private readonly LastFmRepository _lastFmRepository;

    public CrownBuilders(CrownService crownService, ArtistsService artistsService, LastFmRepository lastFmRepository)
    {
        this._crownService = crownService;
        this._artistsService = artistsService;
        this._lastFmRepository = lastFmRepository;
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
                await this._lastFmRepository.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, sessionKey:context.ContextUser.SessionKeyLastFm, useCache: true);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                var errorEmbed =
                    GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, context.ContextUser.UserNameLastFM);
                response.Embed = errorEmbed;
                response.CommandResponse = CommandResponse.LastFmError;
                return response;
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

        var name = GuildService.GetUserFromGuild(guild, currentCrown.UserId);

        var artistUrl =
            $"{Constants.LastFMUserUrl}{currentCrown.User.UserNameLastFM}/library/music/{HttpUtility.UrlEncode(artistSearch.Artist.ArtistName)}";
        response.Embed.AddField("Current crown holder",
            $"**[{name?.UserName ?? currentCrown.User.UserNameLastFM}]({artistUrl})** - " +
            $"Since **<t:{((DateTimeOffset)currentCrown.Created).ToUnixTimeSeconds()}:D>** - " +
            $"`{currentCrown.StartPlaycount}` to `{currentCrown.CurrentPlaycount}` plays");

        var lastCrownCreateDate = currentCrown.Created;
        if (artistCrowns.Count > 1)
        {
            var crownHistory = new StringBuilder();

            foreach (var artistCrown in artistCrowns.Take(10).Where(w => !w.Active))
            {
                var crownUsername = GuildService.GetUserFromGuild(guild, artistCrown.UserId);

                crownHistory.AppendLine($"**{crownUsername?.UserName ?? artistCrown.User.UserNameLastFM}** - " +
                                        $"**<t:{((DateTimeOffset)artistCrown.Created).ToUnixTimeSeconds()}:D>** to **<t:{((DateTimeOffset)lastCrownCreateDate).ToUnixTimeSeconds()}:D>** - " +
                                        $"`{artistCrown.StartPlaycount}` to `{artistCrown.CurrentPlaycount}` plays");
                lastCrownCreateDate = artistCrown.Created;

            }

            response.Embed.AddField("Crown history", crownHistory.ToString());

            if (artistCrowns.Count(w => !w.Active) > 10)
            {
                crownHistory.AppendLine($"*{artistCrowns.Count(w => !w.Active) - 10} more steals hidden..*");

                var firstCrown = artistCrowns.OrderBy(o => o.Created).First();
                var crownUsername = GuildService.GetUserFromGuild(guild, firstCrown.UserId);
                response.Embed.AddField("First crownholder",
                     $"**{crownUsername?.UserName ?? firstCrown.User.UserNameLastFM}** - " +
                     $"**<t:{((DateTimeOffset)firstCrown.Created).ToUnixTimeSeconds()}:D>** to **<t:{((DateTimeOffset)lastCrownCreateDate).ToUnixTimeSeconds()}:D>** - " +
                     $"`{firstCrown.StartPlaycount}` to `{firstCrown.CurrentPlaycount}` plays");
            }
        }

        response.Embed.WithTitle($"Crown info for {currentCrown.ArtistName}");

        var embedDescription = new StringBuilder();

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }
}
