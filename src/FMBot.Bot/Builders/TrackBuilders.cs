using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using SkiaSharp;

namespace FMBot.Bot.Builders;

public class TrackBuilders
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly TrackService _trackService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly PlayService _playService;
    private readonly SpotifyService _spotifyService;
    private readonly TimeService _timeService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PuppeteerService _puppeteerService;

    public TrackBuilders(UserService userService, GuildService guildService, TrackService trackService, WhoKnowsTrackService whoKnowsTrackService, PlayService playService, SpotifyService spotifyService, TimeService timeService, LastFmRepository lastFmRepository, PuppeteerService puppeteerService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._trackService = trackService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._playService = playService;
        this._spotifyService = spotifyService;
        this._timeService = timeService;
        this._lastFmRepository = lastFmRepository;
        this._puppeteerService = puppeteerService;
    }

    public async Task<ResponseModel> GuildTracksAsync(
        ContextModel context,
        Guild guild,
        GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        ICollection<GuildTrack> topGuildTracks;
        IList<GuildTrack> previousTopGuildTracks = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildTracks = await this._whoKnowsTrackService.GetTopAllTimeTracksForGuild(guild.GuildId, guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId, guildListSettings.AmountOfDaysWithBillboard);

            topGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
            previousTopGuildTracks = PlayService.GetGuildTopTracks(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.NewSearchValue);
        }

        if (!topGuildTracks.Any())
        {
            response.Embed.WithDescription(guildListSettings.NewSearchValue != null
                ? $"Sorry, there are no registered top tracks for artist `{guildListSettings.NewSearchValue}` on this server in the time period you selected."
                : $"Sorry, there are no registered top tracks on this server in the time period you selected.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var title = string.IsNullOrWhiteSpace(guildListSettings.NewSearchValue) ?
            $"Top {guildListSettings.TimeDescription.ToLower()} tracks in {context.DiscordGuild.Name}" :
            $"Top {guildListSettings.TimeDescription.ToLower()} '{guildListSettings.NewSearchValue}' tracks in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific track listeners with '{context.Prefix}whoknowstrack'");
                break;
            case 2:
                footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                break;
            case 3:
                footer.AppendLine($"Available sorting options: plays and listeners");
                break;
        }

        var trackPages = topGuildTracks.Chunk(12).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in trackPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in page)
            {
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{track.ListenerCount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.TotalPlaycount} {StringExtensions.GetPlaysString(track.TotalPlaycount)})"
                    : $"`{track.TotalPlaycount}` · **{track.ArtistName}** - **{track.TrackName}** ({track.ListenerCount} {StringExtensions.GetListenersString(track.ListenerCount)})";

                if (previousTopGuildTracks != null && previousTopGuildTracks.Any())
                {
                    var previousTopTrack = previousTopGuildTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.TrackName == track.TrackName);
                    int? previousPosition = previousTopTrack == null ? null : previousTopGuildTracks.IndexOf(previousTopTrack);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{trackPages.Count}");
            pageFooter.Append(footer);

            pages.Add(new PageBuilder()
                .WithTitle(title)
                .WithDescription(pageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> TopTracksAsync(
        ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var pages = new List<PageBuilder>();

        string userTitle;
        if (!userSettings.DifferentUser)
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
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

        var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 200);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }
        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var previousTopTracks = new List<TopTrack>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousTopTracksCall = await this._lastFmRepository
                .GetTopTracksForCustomTimePeriodAsyncAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousTopTracksCall.Success)
            {
                previousTopTracks.AddRange(previousTopTracksCall.Content.TopTracks);
            }
        }

        response.Embed.WithAuthor(response.EmbedAuthor);

        var trackPages = topTracks.Content.TopTracks.ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var trackPage in trackPages)
        {
            var trackPageString = new StringBuilder();
            foreach (var track in trackPage)
            {
                var name = $"**{track.ArtistName}** - **[{track.TrackName}]({track.TrackUrl})** ({track.UserPlaycount} {StringExtensions.GetPlaysString(track.UserPlaycount)})";

                if (topListSettings.Billboard && previousTopTracks.Any())
                {
                    var previousTopTrack = previousTopTracks.FirstOrDefault(f => f.ArtistName == track.ArtistName && f.AlbumName == track.AlbumName);
                    int? previousPosition = previousTopTrack == null ? null : previousTopTracks.IndexOf(previousTopTrack);

                    trackPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                }
                else
                {
                    trackPageString.Append($"{counter}. ");
                    trackPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{trackPages.Count}");
            if (topTracks.Content.TotalAmount.HasValue)
            {
                footer.Append($" - {topTracks.Content.TotalAmount.Value} total tracks in this time period");
            }
            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append("View this list as a billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(trackPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> GetReceipt(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (userSettings.DifferentUser)
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";
        response.EmbedAuthor.WithName($"Top {timeSettings.Description} tracks for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var topTracks = await this._lastFmRepository.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings, 20);

        if (!topTracks.Success)
        {
            response.Embed.ErrorResponse(topTracks.Error, topTracks.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }
        if (topTracks.Content?.TopTracks == null || !topTracks.Content.TopTracks.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top tracks in the [selected time period]({userUrl}).");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        long? timeFrom = null;
        if (timeSettings.TimePeriod != TimePeriod.AllTime && timeSettings.PlayDays != null)
        {
            var dateAgo = DateTime.UtcNow.AddDays(-timeSettings.PlayDays.Value);
            timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();
        }

        var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom, userSettings.SessionKeyLastFm);

        var image = await this._puppeteerService.GetReceipt(userSettings, topTracks.Content, timeSettings, count);

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.FileName = "receipt";

        return response;
    }
}

