using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class GeniusBuilders(
    GeniusService geniusService,
    UserService userService,
    IDataSourceFactory dataSourceFactory)
{
    public async Task<ResponseModel> GeniusAsync(ContextModel context, string searchValue)
    {
        var currentTrackName = "";
        var currentTrackArtist = "";

        string querystring;
        if (!string.IsNullOrWhiteSpace(searchValue))
        {
            querystring = searchValue;
        }
        else
        {
            string sessionKey = null;
            if (!string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
            {
                sessionKey = context.ContextUser.SessionKeyLastFm;
            }

            var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1,
                useCache: true, sessionKey: sessionKey);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles,
                    context.ContextUser.UserNameLastFM, context.Localizer);
            }

            var currentTrack = recentScrobbles.Content.RecentTracks[0];
            querystring = $"{currentTrack.ArtistName} {currentTrack.TrackName}";

            currentTrackName = currentTrack.TrackName;
            currentTrackArtist = currentTrack.ArtistName;
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            ReferencedMusic = new ReferencedMusic
            {
                Artist = currentTrackArtist,
                Track = currentTrackName
            }
        };

        var geniusResults = await geniusService.SearchGeniusAsync(querystring, currentTrackName, currentTrackArtist);

        if (geniusResults == null || !geniusResults.Any())
        {
            response.Embed.WithDescription("No Genius results have been found for this track.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        if (!context.SlashCommand && string.IsNullOrWhiteSpace(searchValue) && Random.Shared.Next(0, 8) == 1 &&
            !await userService.HintShownBefore(context.ContextUser.UserId, "genius"))
        {
            response.EmbedFooter.WithText(
                $"Tip: Search for other songs by simply adding the searchvalue behind '{context.Prefix}genius'.");
            response.HintShown = true;
            response.Embed.WithFooter(response.EmbedFooter);
        }

        var firstResult = geniusResults.First().Result;
        if (firstResult.TitleWithFeatured.Trim().StartsWith(currentTrackName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            firstResult.PrimaryArtist.Name.Trim().Equals(currentTrackArtist.Trim(), StringComparison.OrdinalIgnoreCase) ||
            geniusResults.Count == 1)
        {
            response.Embed.WithTitle(firstResult.TitleWithFeatured);
            response.Embed.WithUrl(firstResult.Url);
            response.Embed.WithThumbnail(firstResult.SongArtImageThumbnailUrl);
            response.Embed.WithDescription($"By **[{firstResult.PrimaryArtist.Name}]({firstResult.PrimaryArtist.Url})**");

            response.Components = new ActionRowProperties().WithButton("View on Genius", url: firstResult.Url);

            return response;
        }

        response.Embed.WithTitle($"Genius results for {querystring}");
        response.Embed.WithThumbnail(firstResult.SongArtImageThumbnailUrl);

        var embedDescription = new StringBuilder();

        var amount = geniusResults.Count > 5 ? 5 : geniusResults.Count;
        for (var i = 0; i < amount; i++)
        {
            var geniusResult = geniusResults[i].Result;

            embedDescription.AppendLine($"{i + 1}. [{geniusResult.TitleWithFeatured}]({geniusResult.Url})");
            embedDescription.AppendLine($"By **[{geniusResult.PrimaryArtist.Name}]({geniusResult.PrimaryArtist.Url})**");
            embedDescription.AppendLine();
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }
}
