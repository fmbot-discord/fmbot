using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using Swan;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class PlayBuilder
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly SettingService _settingService;
    private readonly TimeService _timeService;
    private readonly TrackService _trackService;
    private readonly UserService _userService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsAlbumService _whoKnowsAlbumService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private InteractiveService Interactivity { get; }

    public PlayBuilder(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        SettingService settingService,
        UserService userService,
        WhoKnowsPlayService whoKnowsPlayService,
        CensorService censorService,
        WhoKnowsArtistService whoKnowsArtistService,
        WhoKnowsAlbumService whoKnowsAlbumService,
        WhoKnowsTrackService whoKnowsTrackService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings,
        TimeService timeService,
        GenreService genreService,
        TrackService trackService)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._censorService = censorService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._whoKnowsAlbumService = whoKnowsAlbumService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this.Interactivity = interactivity;
        this._timeService = timeService;
        this._genreService = genreService;
        this._trackService = trackService;
    }

    public async Task<ResponseModel> NowPlayingAsync(
        ContextModel context,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
        {
            sessionKey = context.ContextUser.SessionKeyLastFm;
        }

        Response<RecentTrackList> recentTracks;

        if (!userSettings.DifferentUser)
        {
            if (context.ContextUser.LastIndexed == null)
            {
                _ = this._indexService.IndexUser(context.ContextUser);
                recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm,
                    useCache: true, sessionKey: sessionKey);
            }
            else
            {
                recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(context.ContextUser);
            }
        }
        else
        {
            recentTracks =
                await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            var errorEmbed =
                GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.Embed = errorEmbed;
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var embedType = context.ContextUser.FmEmbedType;

        Guild guild = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);
            if (guild?.FmEmbedType != null)
            {
                embedType = guild.FmEmbedType.Value;
            }

            if (guild != null)
            {
                await this._indexService.UpdateGuildUser(await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId),
                    context.ContextUser.UserId, guild);
            }
        }

        var totalPlaycount = recentTracks.Content.TotalAmount;

        var currentTrack = recentTracks.Content.RecentTracks[0];
        var previousTrack = recentTracks.Content.RecentTracks.Count > 1 ? recentTracks.Content.RecentTracks[1] : null;
        if (userSettings.DifferentUser)
        {
            totalPlaycount = recentTracks.Content.TotalAmount;
        }

        if (!userSettings.DifferentUser)
        {
            this._whoKnowsPlayService.AddRecentPlayToCache(context.ContextUser.UserId, currentTrack);
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        var fmText = "";
        var footerText = "";

        if (!userSettings.DifferentUser &&
            !currentTrack.NowPlaying &&
            currentTrack.TimePlayed.HasValue &&
            currentTrack.TimePlayed < DateTime.UtcNow.AddHours(-1) &&
            currentTrack.TimePlayed > DateTime.UtcNow.AddDays(-5))
        {
            footerText +=
                $"Using Spotify and fm lagging behind? Check '{context.Prefix}outofsync'\n";
        }

        if (currentTrack.Loved)
        {
            footerText +=
                $"❤️ Loved track | ";
        }

        if (embedType is FmEmbedType.TextMini or FmEmbedType.TextFull or FmEmbedType.EmbedTiny)
        {
            if (!userSettings.DifferentUser)
            {
                footerText +=
                    $"{requesterUserTitle} has ";
            }
            else
            {
                footerText +=
                    $"{userSettings.UserNameLastFm} (requested by {requesterUserTitle}) has ";
            }
        }
        else
        {
            footerText +=
                $"{userSettings.UserNameLastFm} has ";
        }


        if (!userSettings.DifferentUser)
        {
            switch (context.ContextUser.FmCountType)
            {
                case FmCountType.Track:
                    var trackPlaycount =
                        await this._whoKnowsTrackService.GetTrackPlayCountForUser(currentTrack.ArtistName,
                            currentTrack.TrackName, context.ContextUser.UserId);
                    if (trackPlaycount.HasValue)
                    {
                        footerText += $"{trackPlaycount} scrobbles on this track | ";
                    }

                    break;
                case FmCountType.Album:
                    if (!string.IsNullOrEmpty(currentTrack.AlbumName))
                    {
                        var albumPlaycount =
                            await this._whoKnowsAlbumService.GetAlbumPlayCountForUser(currentTrack.ArtistName,
                                currentTrack.AlbumName, context.ContextUser.UserId);
                        if (albumPlaycount.HasValue)
                        {
                            footerText += $"{albumPlaycount} scrobbles on this album | ";
                        }
                    }

                    break;
                case FmCountType.Artist:
                    var artistPlaycount =
                        await this._whoKnowsArtistService.GetArtistPlayCountForUser(currentTrack.ArtistName,
                            context.ContextUser.UserId);
                    if (artistPlaycount.HasValue)
                    {
                        footerText += $"{artistPlaycount} scrobbles on this artist | ";
                    }

                    break;
                case null:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        footerText += $"{totalPlaycount} total scrobbles";

        switch (embedType)
        {
            case FmEmbedType.TextMini:
            case FmEmbedType.TextFull:
                if (embedType == FmEmbedType.TextMini)
                {
                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();
                }
                else if (previousTrack != null)
                {
                    fmText += $"**Current track**:\n";

                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();

                    fmText += $"\n" +
                              $"**Previous track**:\n";

                    fmText += StringService.TrackToString(previousTrack).FilterOutMentions();
                }

                fmText +=
                    $"`{footerText.FilterOutMentions()}`";

                response.ResponseType = ResponseType.Text;
                response.Text = fmText;
                break;
            default:
                if (embedType == FmEmbedType.EmbedMini || embedType == FmEmbedType.EmbedTiny)
                {
                    fmText += StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled);
                    response.Embed.WithDescription(fmText);
                }
                else if (previousTrack != null)
                {
                    response.Embed.AddField("Current:",
                        StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled));
                    response.Embed.AddField("Previous:",
                        StringService.TrackToLinkedString(previousTrack, context.ContextUser.RymEnabled));
                }

                string headerText;
                if (currentTrack.NowPlaying)
                {
                    headerText = "Now playing - ";
                }
                else
                {
                    headerText = embedType == FmEmbedType.EmbedMini
                        ? "Last track for "
                        : "Last tracks for ";
                }

                headerText += embedTitle;

                if (!currentTrack.NowPlaying && currentTrack.TimePlayed.HasValue)
                {
                    footerText += " | Last scrobble:";
                    response.Embed.WithTimestamp(currentTrack.TimePlayed.Value);
                }

                response.EmbedAuthor.WithName(headerText);
                response.EmbedAuthor.WithUrl(recentTracks.Content.UserUrl);

                if (guild != null && !userSettings.DifferentUser)
                {
                    var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
                        guild, currentTrack.ArtistName, currentTrack.TrackName);

                    if (guildAlsoPlaying != null)
                    {
                        footerText += "\n";
                        footerText += guildAlsoPlaying;
                    }
                }

                if (!string.IsNullOrWhiteSpace(footerText))
                {
                    response.EmbedFooter.WithText(footerText);
                    response.Embed.WithFooter(response.EmbedFooter);
                }

                if (embedType != FmEmbedType.EmbedTiny)
                {
                    response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
                    response.Embed.WithAuthor(response.EmbedAuthor);
                    response.Embed.WithUrl(recentTracks.Content.UserUrl);
                }

                if (currentTrack.AlbumCoverUrl != null && embedType != FmEmbedType.EmbedTiny)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                        currentTrack.AlbumName, currentTrack.ArtistName, currentTrack.AlbumCoverUrl);
                    if (safeForChannel.Result)
                    {
                        response.Embed.WithThumbnailUrl(currentTrack.AlbumCoverUrl);
                    }
                }

                break;
        }

        return response;
    }

    public async Task<ResponseModel> RecentAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int amount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        string sessionKey = null;
        if (!userSettings.DifferentUser && !string.IsNullOrEmpty(context.ContextUser.SessionKeyLastFm))
        {
            sessionKey = context.ContextUser.SessionKeyLastFm;
        }

        var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(userSettings.UserNameLastFm, amount, useCache: true, sessionKey: sessionKey);

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            var errorEmbed =
                GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, userSettings.UserNameLastFm);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        response.EmbedAuthor.WithName($"Latest tracks for {embedTitle}");

        response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        response.EmbedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var fmRecentText = "";
        var resultAmount = recentTracks.Content.RecentTracks.Count;
        if (recentTracks.Content.RecentTracks.Any(a => a.NowPlaying))
        {
            resultAmount -= 1;
        }
        for (var i = 0; i < resultAmount; i++)
        {
            var track = recentTracks.Content.RecentTracks[i];

            if (i == 0)
            {
                if (track.AlbumCoverUrl != null)
                {
                    var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                        track.AlbumName, track.ArtistName, track.AlbumCoverUrl);
                    if (safeForChannel.Result)
                    {
                        response.Embed.WithThumbnailUrl(track.AlbumCoverUrl);
                    }
                }
            }

            var trackString = StringService.TrackToLinkedString(track, context.ContextUser.RymEnabled);

            if (track.NowPlaying)
            {
                fmRecentText += $"🎶 - {trackString}\n";
            }
            else
            {
                fmRecentText += $"`{i + 1}` - {trackString}\n";
            }
        }

        response.Embed.WithDescription(fmRecentText);

        string footerText;
        var firstTrack = recentTracks.Content.RecentTracks[0];
        if (firstTrack.NowPlaying)
        {
            footerText =
                $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles | Now Playing";
        }
        else
        {
            footerText =
                $"{userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount} scrobbles";

            if (!firstTrack.NowPlaying && firstTrack.TimePlayed.HasValue)
            {
                footerText += " | Last scrobble:";
                response.Embed.WithTimestamp(firstTrack.TimePlayed.Value);
            }
        }

        response.EmbedFooter.WithText(footerText);

        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> OverviewAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int amount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._updateService.UpdateUser(context.ContextUser);

        var week = await this._playService.GetDailyOverview(userSettings.UserId, amount);

        if (week == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = "Sorry, we don't have plays for this user in the selected amount of days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        foreach (var day in week.Days.OrderByDescending(o => o.Date))
        {
            var genreString = new StringBuilder();
            if (day.TopGenres != null && day.TopGenres.Any())
            {
                for (var i = 0; i < day.TopGenres.Count; i++)
                {
                    if (i != 0)
                    {
                        genreString.Append(" - ");
                    }

                    var genre = day.TopGenres[i];
                    genreString.Append($"{genre}");
                }
            }

            response.Embed.AddField(
                $"{day.Playcount} plays - {StringExtensions.GetListeningTimeString(day.ListeningTime)} - <t:{day.Date.ToUnixEpochDate()}:D>",
                $"{genreString}\n" +
                $"{day.TopArtist}\n" +
                $"{day.TopAlbum}\n" +
                $"{day.TopTrack}"
            );
        }

        var description = $"Top genres, artist, album and track for last {amount} days";

        if (week.Days.Count < amount)
        {
            description += $"\n{amount - week.Days.Count} days not shown because of no plays.";
        }

        response.Embed.WithDescription(description);

        response.EmbedAuthor.WithName($"Daily overview for {userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}");

        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library?date_preset=LAST_7_DAYS");
        response.Embed.WithAuthor(response.EmbedAuthor);

        response.EmbedFooter.WithText($"{week.Uniques} unique tracks - {week.Playcount} total plays - avg {Math.Round(week.AvgPerDay, 1)} per day");
        response.Embed.WithFooter(response.EmbedFooter);

        return response;
    }

    public async Task<ResponseModel> PaceAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        long goalAmount,
        long userTotalPlaycount,
        long timeFrom)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var count = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom, userSettings.SessionKeyLastFm);

        if (count is null or 0)
        {
            response.Text = $"<@{context.DiscordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
        var totalDays = (DateTime.UtcNow - age).TotalDays;

        var playsLeft = goalAmount - userTotalPlaycount;

        var avgPerDay = count / totalDays;

        var goalDate = (DateTime.UtcNow.AddDays(playsLeft / avgPerDay.Value));

        var reply = new StringBuilder();

        var determiner = "your";
        if (userSettings.DifferentUser)
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that the user '{userSettings.UserNameLastFm.FilterOutMentions()}'");
            determiner = "their";
        }
        else
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that you");
        }

        reply.AppendLine($" will reach **{goalAmount}** scrobbles on **<t:{goalDate.ToUnixEpochDate()}:D>**.");

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            reply.AppendLine(
                $"This is based on {determiner} alltime avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day. ({count} in {Math.Round(totalDays, 0)} days)");
        }
        else
        {
            reply.AppendLine(
                $"This is based on {determiner} avg of {Math.Round(avgPerDay.GetValueOrDefault(0), 1)} per day in the last {Math.Round(totalDays, 0)} days ({count} total)");
        }

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> MileStoneAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        long mileStoneAmount,
        long userTotalPlaycount)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var mileStonePlay = await this._lastFmRepository.GetMilestoneScrobbleAsync(userSettings.UserNameLastFm, userSettings.SessionKeyLastFm, userTotalPlaycount, mileStoneAmount);

        if (!mileStonePlay.Success || mileStonePlay.Content == null)
        {
            response.Embed.ErrorResponse(mileStonePlay.Error, mileStonePlay.Message, "milestone", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var reply = new StringBuilder();

        reply.AppendLine(StringService.TrackToLinkedString(mileStonePlay.Content));

        var userTitle = $"{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}";

        response.Embed.WithTitle($"{mileStoneAmount}{StringExtensions.GetAmountEnd(mileStoneAmount)} scrobble from {userTitle}");

        if (mileStonePlay.Content.AlbumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel,
                mileStonePlay.Content.AlbumName, mileStonePlay.Content.ArtistName, mileStonePlay.Content.AlbumCoverUrl);
            if (safeForChannel.Result)
            {
                response.Embed.WithThumbnailUrl(mileStonePlay.Content.AlbumCoverUrl);
            }
        }

        if (mileStonePlay.Content.TimePlayed.HasValue)
        {
            var dateString = mileStonePlay.Content.TimePlayed.Value.ToString("yyyy-M-dd");
            response.Embed.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library?from={dateString}&to={dateString}");

            reply.AppendLine($"Date played: **<t:{mileStonePlay.Content.TimePlayed.Value.ToUnixEpochDate()}:D>**");
        }

        response.Embed.WithDescription(reply.ToString());

        return response;
    }
}
