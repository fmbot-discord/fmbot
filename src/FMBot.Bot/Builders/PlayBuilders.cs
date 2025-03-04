using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
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
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class PlayBuilder
{
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;
    private readonly UpdateService _updateService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly PlayService _playService;
    private readonly GenreService _genreService;
    private readonly TimeService _timeService;
    private readonly TrackService _trackService;
    private readonly UserService _userService;
    private readonly CountryService _countryService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly AlbumService _albumService;
    private readonly PuppeteerService _puppeteerService;
    private readonly ArtistsService _artistsService;

    private InteractiveService Interactivity { get; }

    public PlayBuilder(
        GuildService guildService,
        IndexService indexService,
        UpdateService updateService,
        IDataSourceFactory dataSourceFactory,
        PlayService playService,
        UserService userService,
        WhoKnowsPlayService whoKnowsPlayService,
        CensorService censorService,
        InteractiveService interactivity,
        TimeService timeService,
        GenreService genreService,
        TrackService trackService,
        CountryService countryService,
        AlbumService albumService,
        PuppeteerService puppeteerService,
        ArtistsService artistsService)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._dataSourceFactory = dataSourceFactory;
        this._playService = playService;
        this._updateService = updateService;
        this._userService = userService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._censorService = censorService;
        this.Interactivity = interactivity;
        this._timeService = timeService;
        this._genreService = genreService;
        this._trackService = trackService;
        this._countryService = countryService;
        this._albumService = albumService;
        this._puppeteerService = puppeteerService;
        this._artistsService = artistsService;
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
                recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFm,
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
                await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFm, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, userSettings.UserNameLastFm);
        }

        var embedType = context.ContextUser.FmEmbedType;

        Guild guild = null;
        IDictionary<int, FullGuildUser> guildUsers = null;
        if (context.DiscordGuild != null)
        {
            guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);
            if (guild?.FmEmbedType != null)
            {
                embedType = guild.FmEmbedType.Value;
            }

            var channel = await this._guildService.GetChannel(context.DiscordChannel.Id);
            if (channel?.FmEmbedType != null)
            {
                embedType = channel.FmEmbedType.Value;
            }

            if (guild != null)
            {
                guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);
                var discordGuildUser =
                    await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId, CacheMode.CacheOnly);

                await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, context.ContextUser.UserId,
                    guild);
            }
        }

        var totalPlaycount = recentTracks.Content.TotalAmount;

        var currentTrack = recentTracks.Content.RecentTracks[0];
        var previousTrack = recentTracks.Content.RecentTracks.Count > 1 ? recentTracks.Content.RecentTracks[1] : null;
        if (userSettings.DifferentUser)
        {
            totalPlaycount = recentTracks.Content.TotalAmount;
        }

        PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, currentTrack.ArtistName);
        PublicProperties.UsedCommandsTracks.TryAdd(context.InteractionId, currentTrack.TrackName);
        if (!string.IsNullOrWhiteSpace(currentTrack.AlbumName))
        {
            PublicProperties.UsedCommandsAlbums.TryAdd(context.InteractionId, currentTrack.AlbumName);
        }

        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        // var embed = await this._userService.GetTemplateFmAsync(context.ContextUser.UserId, userSettings, currentTrack,
        //     previousTrack, totalPlaycount, guild, guildUsers);
        // response.Embed = embed.EmbedBuilder;
        // return response;

        var fmText = "";
        var footerText = await this._userService.GetFooterAsync(
            context.ContextUser.FmFooterOptions, userSettings, currentTrack, previousTrack, totalPlaycount, context,
            guild, guildUsers,
            embedType == FmEmbedType.TextFull || embedType == FmEmbedType.TextMini);

        if (!userSettings.DifferentUser &&
            !currentTrack.NowPlaying &&
            currentTrack.TimePlayed.HasValue &&
            currentTrack.TimePlayed < DateTime.UtcNow.AddHours(-1) &&
            currentTrack.TimePlayed > DateTime.UtcNow.AddDays(-5))
        {
            footerText.Append($"Using Spotify and lagging behind? Check '{context.Prefix}outofsync' - ");
        }

        switch (embedType)
        {
            case FmEmbedType.TextOneLine:
                response.Text =
                    $"**{embedTitle}** is listening to **{currentTrack.TrackName}** by **{currentTrack.ArtistName}**"
                        .FilterOutMentions();

                response.ResponseType = ResponseType.Text;
                break;
            case FmEmbedType.TextMini:
            case FmEmbedType.TextFull:
                if (embedType == FmEmbedType.TextMini)
                {
                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();
                }
                else if (previousTrack != null)
                {
                    if (currentTrack.NowPlaying)
                    {
                        fmText += $"*{embedTitle}'s now playing:*\n";
                    }
                    else
                    {
                        fmText += $"*{embedTitle}'s last played track:*\n";
                    }

                    fmText += StringService.TrackToString(currentTrack).FilterOutMentions();

                    fmText += $"\n" +
                              $"*Previous:*\n";

                    fmText += StringService.TrackToString(previousTrack).FilterOutMentions();
                }

                var formattedFooter = footerText.Length == 0 ? "" : $"{footerText}";
                fmText += formattedFooter;

                response.ResponseType = ResponseType.Text;
                response.Text = fmText;
                break;
            default:
                if (embedType == FmEmbedType.EmbedMini || embedType == FmEmbedType.EmbedTiny)
                {
                    fmText += StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled,
                        embedType == FmEmbedType.EmbedMini);
                    response.Embed.WithDescription(fmText);
                }
                else if (previousTrack != null)
                {
                    var embedFull = new StringBuilder();
                    embedFull.AppendLine(currentTrack.NowPlaying ? $"-# *Current:*" : $"-# *Last:*");
                    embedFull.AppendLine(
                        StringService.TrackToLinkedString(currentTrack, context.ContextUser.RymEnabled, false));
                    embedFull.AppendLine("-# *Previous:*");
                    embedFull.Append(StringService.TrackToLinkedString(previousTrack, context.ContextUser.RymEnabled,
                        false));
                    response.Embed.WithDescription(embedFull.ToString());
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

                response.EmbedAuthor.WithName(headerText);
                response.EmbedAuthor.WithUrl(recentTracks.Content.UserUrl);

                if (!currentTrack.NowPlaying && currentTrack.TimePlayed.HasValue)
                {
                    footerText.AppendLine("Last scrobble:");
                    response.Embed.WithTimestamp(currentTrack.TimePlayed.Value);
                }


                if (guild != null && !userSettings.DifferentUser)
                {
                    var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingTrack(context.ContextUser.UserId,
                        guildUsers, guild, currentTrack.ArtistName, currentTrack.TrackName);

                    if (guildAlsoPlaying != null)
                    {
                        footerText.AppendLine(guildAlsoPlaying);
                    }
                }

                if (footerText.Length > 0)
                {
                    response.EmbedFooter.WithText(footerText.ToString());
                    response.Embed.WithFooter(response.EmbedFooter);
                }

                if (embedType != FmEmbedType.EmbedTiny)
                {
                    response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
                    response.Embed.WithAuthor(response.EmbedAuthor);
                    response.Embed.WithUrl(recentTracks.Content.UserUrl);
                }

                if (currentTrack.AlbumName != null)
                {
                    var dbAlbum =
                        await this._albumService.GetAlbumFromDatabase(currentTrack.ArtistName, currentTrack.AlbumName);

                    var albumCoverUrl = dbAlbum?.SpotifyImageUrl ?? currentTrack.AlbumCoverUrl;

                    if (albumCoverUrl != null && embedType != FmEmbedType.EmbedTiny)
                    {
                        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                            context.DiscordChannel,
                            currentTrack.AlbumName, currentTrack.ArtistName, albumCoverUrl);
                        if (safeForChannel == CensorService.CensorResult.Safe)
                        {
                            response.Embed.WithThumbnailUrl(albumCoverUrl);
                        }
                    }
                }

                break;
        }

        if (context.ContextUser.EmoteReactions != null && context.ContextUser.EmoteReactions.Any() &&
            SupporterService.IsSupporter(context.ContextUser.UserType))
        {
            response.EmoteReactions = context.ContextUser.EmoteReactions;
        }
        else if (context.DiscordGuild != null)
        {
            response.EmoteReactions = await this._guildService.GetGuildReactions(context.DiscordGuild.Id);
        }

        return response;
    }

    public async Task<ResponseModel> RecentAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        string artistToFilter = null)
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
                recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFm,
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
                await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFm, 120, useCache: true);
        }

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, userSettings.UserNameLastFm);
        }

        var limit = SupporterService.IsSupporter(userSettings.UserType) ? int.MaxValue : 240;
        recentTracks.Content =
            await this._playService.AddUserPlaysToRecentTracks(userSettings.UserId, recentTracks.Content, limit);

        if (!SupporterService.IsSupporter(userSettings.UserType))
        {
            recentTracks.Content.RecentTracks = recentTracks.Content.RecentTracks.Take(239).ToList();
        }

        if (SupporterService.IsSupporter(userSettings.UserType) && !string.IsNullOrWhiteSpace(artistToFilter))
        {
            recentTracks.Content.RecentTracks = recentTracks.Content.RecentTracks
                .Where(w => artistToFilter.Equals(w.ArtistName, StringComparison.OrdinalIgnoreCase)).ToList();
        }


        var requesterUserTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var embedTitle = !userSettings.DifferentUser
            ? $"{requesterUserTitle}"
            : $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}, requested by {requesterUserTitle}";

        response.EmbedAuthor.WithName($"Latest tracks for {embedTitle}");

        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        response.EmbedAuthor.WithUrl(recentTracks.Content.UserRecentTracksUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var firstTrack = recentTracks.Content.RecentTracks.ElementAtOrDefault(0);
        string thumbnailUrl = null;
        if (firstTrack?.AlbumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                firstTrack.AlbumName, firstTrack.ArtistName, firstTrack.AlbumCoverUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                thumbnailUrl = firstTrack.AlbumCoverUrl;
            }
        }

        var pages = new List<PageBuilder>();

        var trackPages = recentTracks.Content.RecentTracks
            .ToList()
            .ChunkBy(6);
        var pageCounter = 1;

        foreach (var trackPage in trackPages)
        {
            var trackPageString = new StringBuilder();
            foreach (var track in trackPage)
            {
                trackPageString.AppendLine(StringService
                    .TrackToLinkedStringWithTimestamp(track, context.ContextUser.RymEnabled));
            }

            var footer = new StringBuilder();

            ImportService.AddImportDescription(footer, [trackPage.Last().PlaySource ?? PlaySource.LastFm]);

            footer.Append($"Page {pageCounter}/{trackPages.Count.Format(context.NumberFormat)}");
            footer.Append(
                $" - {userSettings.UserNameLastFm} has {recentTracks.Content.TotalAmount.Format(context.NumberFormat)} scrobbles");

            if (!string.IsNullOrWhiteSpace(artistToFilter))
            {
                footer.AppendLine();
                if (!SupporterService.IsSupporter(userSettings.UserType))
                {
                    footer.Append($"Sorry, artist filtering is only available for supporters");
                }
                else
                {
                    footer.Append($"Filtering plays to artist '{artistToFilter}'");
                }
            }

            var page = new PageBuilder()
                .WithDescription(StringExtensions.TruncateLongString(trackPageString.ToString(), 4095))
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString());

            if (pageCounter == 1 && thumbnailUrl != null)
            {
                page.WithThumbnailUrl(thumbnailUrl);
            }

            pages.Add(page);

            pageCounter++;
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No recent tracks found.")
                .WithAuthor(response.EmbedAuthor));
        }

        if (SupporterService.IsSupporter(userSettings.UserType))
        {
            response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        }
        else
        {
            response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
        }

        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> PlaysAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text
        };

        var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
            timeSettings.TimeFrom, userSettings.SessionKeyLastFm, timeSettings.TimeUntil);

        if (count == null)
        {
            response.Text =
                $"Could not find total count for Last.fm user `{StringExtensions.Sanitize(userSettings.UserNameLastFm)}`.";
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var userTitle =
            $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}";

        response.Text = timeSettings.TimePeriod == TimePeriod.AllTime
            ? $"**{userTitle}** has `{count.Format(context.NumberFormat)}` total scrobbles"
            : $"**{userTitle}** has `{count.Format(context.NumberFormat)}` scrobbles in the {timeSettings.AltDescription}";

        return response;
    }

    public async Task<ResponseModel> StreakAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        User userWithStreak)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var recentTracks = await this._updateService.UpdateUserAndGetRecentTracks(userWithStreak);

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, userSettings.UserNameLastFm);
        }

        var lastPlays = await this._playService.GetAllUserPlays(userSettings.UserId);
        var streak = PlayService.GetCurrentStreak(userSettings.UserId,
            recentTracks.Content.RecentTracks.FirstOrDefault(), lastPlays);

        response.EmbedAuthor.WithName(
            $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s streak overview");

        string emoji = null;
        if (PlayService.StreakExists(streak))
        {
            var streakText = PlayService.StreakToText(streak, context.NumberFormat);
            response.Embed.WithDescription(streakText);

            if (!userSettings.DifferentUser)
            {
                if (PlayService.ShouldSaveStreak(streak))
                {
                    var saved = await this._playService.UpdateOrInsertStreak(streak);
                    if (saved != null)
                    {
                        response.Embed.WithFooter(saved);
                    }
                }
                else
                {
                    response.Embed.WithFooter(
                        $"Only streaks with {Constants.StreakSaveThreshold} plays or higher are saved.");
                }
            }

            if (!string.IsNullOrWhiteSpace(streak.ArtistName))
            {
                PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, streak.ArtistName);
            }

            if (!string.IsNullOrWhiteSpace(streak.AlbumName))
            {
                PublicProperties.UsedCommandsAlbums.TryAdd(context.InteractionId, streak.AlbumName);
            }

            if (!string.IsNullOrWhiteSpace(streak.TrackName))
            {
                PublicProperties.UsedCommandsTracks.TryAdd(context.InteractionId, streak.TrackName);
            }

            emoji = PlayService.GetEmojiForStreakCount(streak.ArtistPlaycount.GetValueOrDefault());
        }
        else
        {
            response.Embed.WithDescription("No active streak found.\n" +
                                           "Try scrobbling multiple of the same artist, album or track in a row to get started.");
        }

        if (!userSettings.DifferentUser)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        response.EmbedAuthor.WithUrl($"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library");
        response.Embed.WithAuthor(response.EmbedAuthor);

        if (emoji != null)
        {
            response.EmoteReactions = [emoji.Trim()];
        }


        return response;
    }

    public async Task<ResponseModel> StreakHistoryAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        bool editMode = false,
        string artist = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var streaks = await this._playService.GetStreaks(userSettings.UserId);

        response.EmbedAuthor.WithName(
            !userSettings.DifferentUser
                ? $"Streak history for {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}"
                : $"Streak history for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}");

        response.EmbedAuthor.WithUrl($"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library");

        if (!string.IsNullOrWhiteSpace(artist))
        {
            var artistFilter = artist.Trim().ToLower();
            streaks = streaks.Where(w => w.ArtistPlaycount.HasValue &&
                                         w.ArtistName != null &&
                                         w.ArtistName.Trim().Contains(artistFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!streaks.Any())
        {
            response.Embed.WithDescription("No saved streaks found for this user.");
            if (artist != null)
            {
                response.Embed.WithFooter($"Filtering to artist '{artist}'");
            }

            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var streakPages = streaks.Chunk(4).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in streakPages)
        {
            var pageString = new StringBuilder();
            foreach (var streak in page)
            {
                pageString.Append($"**{counter}. **");

                if ((streak.StreakEnded - streak.StreakStarted).TotalHours <= 20)
                {
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakStarted).ToUnixTimeSeconds()}:f>");
                    pageString.Append($" til ");
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakEnded).ToUnixTimeSeconds()}:t>");
                }
                else
                {
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakStarted).ToUnixTimeSeconds()}:f>");
                    pageString.Append($" til ");
                    pageString.Append($"<t:{((DateTimeOffset)streak.StreakEnded).ToUnixTimeSeconds()}:f>");
                }

                if (editMode && !userSettings.DifferentUser)
                {
                    pageString.Append($" · Deletion ID: `{streak.UserStreakId}`");
                }

                pageString.AppendLine();

                var streakText = PlayService.StreakToText(streak, context.NumberFormat, false);
                pageString.AppendLine(streakText);

                counter++;
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{streakPages.Count}");

            if (!string.IsNullOrWhiteSpace(artist))
            {
                pageFooter.Append($" - Filtering to artist '{artist}'");
            }

            if (editMode)
            {
                pageFooter.AppendLine();
                pageFooter.Append($"Editmode enabled - Use the trash button to delete streaks");
            }

            pages.Add(new PageBuilder()
                .WithDescription(pageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        if (pages.Count == 1)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.Description = pages[0].Description;
            response.EmbedAuthor = response.EmbedAuthor;
            response.EmbedFooter = response.EmbedFooter;

            if (editMode)
            {
                response.Components = new ComponentBuilder()
                    .WithButton(emote: new Emoji("🗑️"), customId: InteractionConstants.DeleteStreak);
            }

            return response;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages,
            editMode ? InteractionConstants.DeleteStreak : null, editMode ? new Emoji("🗑️") : null);

        return response;
    }

    public async Task<ResponseModel> DeleteStreakAsync(
        ContextModel context,
        long streakToDelete)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var streaks = await this._playService.GetStreaks(context.ContextUser.UserId);

        if (!streaks.Any())
        {
            response.Embed.WithDescription("No saved streaks found for you.");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var streak = streaks.FirstOrDefault(f =>
            f.UserStreakId == streakToDelete && f.UserId == context.ContextUser.UserId);

        if (streak == null)
        {
            response.Embed.WithDescription("Could not find streak to delete.");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        await this._playService.DeleteStreak(streak.UserStreakId);

        response.Embed.WithTitle("🗑 Streak deleted");
        response.Embed.WithDescription("Successfully deleted the following streak:\n" +
                                       PlayService.StreakToText(streak, context.NumberFormat, false));
        response.ResponseType = ResponseType.Embed;
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

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone ?? "Eastern Standard Time");

        var limit = SupporterService.IsSupporter(userSettings.UserType) ? 99999 : 30;
        var dailyOverview = await this._playService.GetDailyOverview(userSettings.UserId, timeZone, limit);

        if (dailyOverview == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = "Sorry, we don't have plays for this user in the selected amount of days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var dayPages = dailyOverview.Days.OrderByDescending(o => o.Date).Chunk(amount).ToList();

        response.EmbedAuthor.WithName(
            $"Daily overview for {StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}");
        response.EmbedAuthor.WithUrl(
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library?date_preset=LAST_7_DAYS");

        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var days in dayPages)
        {
            var plays = new List<UserPlay>();

            foreach (var day in days.OrderByDescending(o => o.Date))
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

                var fieldContent = new StringBuilder();
                if (genreString.Length > 0)
                {
                    fieldContent.AppendLine($"-# *{genreString}*");
                }

                fieldContent.AppendLine(day.TopArtist);
                fieldContent.AppendLine(day.TopAlbum);
                fieldContent.Append(day.TopTrack);

                response.Embed.AddField(
                    $"<t:{TimeZoneInfo.ConvertTimeToUtc(day.Date, timeZone).ToUnixEpochDate()}:D> - " +
                    $"{StringExtensions.GetListeningTimeString(day.ListeningTime)} - " +
                    $"{day.Playcount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(day.Playcount)}",
                    fieldContent.ToString()
                );

                plays.AddRange(day.Plays);
            }

            var pageFooter = new StringBuilder();

            pageFooter.Append(amount == 1 ? $"Day" : $"Page");
            pageFooter.Append($" {pageCounter}/{dayPages.Count}");

            if (amount == 7)
            {
                pageFooter.Append($" - 🫡");
            }

            pageFooter.AppendLine();
            pageFooter.AppendLine(
                $"Top genres, artist, album and track per {amount.Format(context.NumberFormat)} days");
            pageFooter.AppendLine(
                $"{PlayService.GetUniqueCount(plays).Format(context.NumberFormat)} unique tracks - {plays.Count.Format(context.NumberFormat)} total plays - avg {Math.Round(PlayService.GetAvgPerDayCount(plays), 1).Format(context.NumberFormat)} per day");

            if (days.Count() < amount)
            {
                pageFooter.AppendLine($"{amount - days.Count()} days not shown because of no plays.");
            }

            pages.Add(new PageBuilder()
                .WithFields(response.Embed.Fields)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
            response.Embed.Fields = new();
        }

        if (SupporterService.IsSupporter(userSettings.UserType))
        {
            response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        }
        else
        {
            response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
        }

        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> PaceAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        long goalAmount,
        long userTotalPlaycount,
        long? registeredUnixTime = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        long? count;

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            timeSettings.TimeFrom = registeredUnixTime;
            count = userTotalPlaycount;
        }
        else
        {
            count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom, userSettings.SessionKeyLastFm);
        }

        if (count is null or 0)
        {
            response.Text =
                $"<@{context.DiscordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(timeSettings.TimeFrom.GetValueOrDefault());
        var totalDays = (DateTime.UtcNow - age).TotalDays;

        var playsLeft = goalAmount - userTotalPlaycount;

        var avgPerDay = count / totalDays;

        var goalDate = (DateTime.UtcNow.AddDays(playsLeft / avgPerDay.Value));

        var reply = new StringBuilder();

        var determiner = "your";
        if (userSettings.DifferentUser)
        {
            reply.Append(
                $"<@{context.DiscordUser.Id}> My estimate is that the user '{userSettings.UserNameLastFm.FilterOutMentions()}'");
            determiner = "their";
        }
        else
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that you");
        }

        reply.AppendLine(
            $" will reach **{goalAmount.Format(context.NumberFormat)}** scrobbles on **<t:{goalDate.ToUnixEpochDate()}:D>**.");

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            reply.AppendLine(
                $"-# *Based on {determiner} alltime average of {Math.Round(avgPerDay.GetValueOrDefault(0), 1).Format(context.NumberFormat)} scrobbles per day — {count.Format(context.NumberFormat)} total in {Math.Round(totalDays, 0)} days*");
        }
        else
        {
            reply.AppendLine(
                $"-# *Based on {determiner} average of {Math.Round(avgPerDay.GetValueOrDefault(0), 1).Format(context.NumberFormat)} scrobbles per day in the last {Math.Round(totalDays, 0)} days — {count.Format(context.NumberFormat)} total*");
        }

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> MileStoneAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int mileStoneAmount,
        long userTotalPlaycount,
        bool isRandom = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var mileStonePlay = await this._dataSourceFactory.GetMilestoneScrobbleAsync(userSettings.UserNameLastFm,
            userSettings.SessionKeyLastFm, userTotalPlaycount, mileStoneAmount);

        if (!mileStonePlay.Success || mileStonePlay.Content == null)
        {
            response.Embed.ErrorResponse(mileStonePlay.Error, mileStonePlay.Message, "milestone", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            return response;
        }

        var reply = new StringBuilder();

        reply.AppendLine(StringService.TrackToLinkedString(mileStonePlay.Content));

        var userTitle = $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}";

        response.Embed.WithTitle(
            $"{mileStoneAmount.Format(context.NumberFormat)}{StringExtensions.GetAmountEnd(mileStoneAmount)} scrobble from {userTitle}");

        if (mileStonePlay.Content.AlbumCoverUrl != null)
        {
            var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel,
                mileStonePlay.Content.AlbumName, mileStonePlay.Content.ArtistName, mileStonePlay.Content.AlbumCoverUrl);
            if (safeForChannel == CensorService.CensorResult.Safe)
            {
                response.Embed.WithThumbnailUrl(mileStonePlay.Content.AlbumCoverUrl);
            }
        }

        if (mileStonePlay.Content.TimePlayed.HasValue)
        {
            var dateString = mileStonePlay.Content.TimePlayed.Value.ToString("yyyy-M-dd");
            response.Embed.WithUrl(
                $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library?from={dateString}&to={dateString}");

            reply.AppendLine($"Date played: **<t:{mileStonePlay.Content.TimePlayed.Value.ToUnixEpochDate()}:D>**");

            PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, mileStonePlay.Content.ArtistName);
            PublicProperties.UsedCommandsTracks.TryAdd(context.InteractionId, mileStonePlay.Content.TrackName);
            if (!string.IsNullOrWhiteSpace(mileStonePlay.Content.AlbumName))
            {
                PublicProperties.UsedCommandsAlbums.TryAdd(context.InteractionId, mileStonePlay.Content.AlbumName);
            }

            response.ReferencedMusic = new ReferencedMusic
            {
                Artist = mileStonePlay.Content.ArtistName,
                Album = mileStonePlay.Content.AlbumName,
                Track = mileStonePlay.Content.TrackName,
            };
        }

        if (isRandom)
        {
            response.Components = new ComponentBuilder().WithButton("Reroll",
                $"{InteractionConstants.RandomMilestone}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: new Emoji("🎲"));
        }

        response.Embed.WithDescription(reply.ToString());

        return response;
    }

    public async Task<ResponseModel> YearAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        int year)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        var pagesAmount = userSettings.UserType == UserType.User ? 2 : 3;

        var yearOverview = await this._playService.GetYear(userSettings.UserId, year);

        if (yearOverview.LastfmErrors)
        {
            response.Embed.WithDescription("Sorry, Last.fm returned an error. Please try again");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (yearOverview.TopArtists?.TopArtists == null || !yearOverview.TopArtists.TopArtists.Any())
        {
            response.Embed.WithDescription(
                "Sorry, you haven't listened to music in this year. If you think this message is wrong, please try again.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userTitle = $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}'s";
        var pages = new List<PageBuilder>();

        var description = new StringBuilder();
        var fields = new List<EmbedFieldBuilder>();

        if (yearOverview.PreviousTopArtists?.TopArtists is { Count: > 0 })
        {
            description.AppendLine($"Your top genres, artists, albums and tracks for {year} compared to {year - 1}.");
        }
        else
        {
            description.AppendLine($"Welcome to Last.fm and .fmbot. Here's your overview for {year}.");
        }

        response.Embed.WithDescription(description.ToString());

        var genres = await this._genreService.GetTopGenresForTopArtists(yearOverview.TopArtists.TopArtists);

        var previousTopGenres = new List<TopGenre>();
        if (yearOverview.PreviousTopArtists?.TopArtists != null)
        {
            previousTopGenres =
                await this._genreService.GetTopGenresForTopArtists(yearOverview.PreviousTopArtists?.TopArtists);
        }

        var genreDescription = new StringBuilder();
        var lines = new List<StringService.BillboardLine>();
        for (var i = 0; i < genres.Count; i++)
        {
            var topGenre = genres[i];

            var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == topGenre.GenreName);

            int? previousPosition = previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

            var line = StringService.GetBillboardLine($"**{topGenre.GenreName}**", i, previousPosition);
            lines.Add(line);

            if (i < 10)
            {
                genreDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Genres").WithValue(genreDescription.ToString())
            .WithIsInline(true));

        var artistDescription = new StringBuilder();
        for (var i = 0; i < yearOverview.TopArtists.TopArtists.Count; i++)
        {
            var topArtist = yearOverview.TopArtists.TopArtists[i];

            var previousTopArtist =
                yearOverview.PreviousTopArtists?.TopArtists?.FirstOrDefault(f =>
                    f.ArtistName == topArtist.ArtistName);

            var previousPosition = previousTopArtist == null
                ? null
                : yearOverview.PreviousTopArtists?.TopArtists?.IndexOf(previousTopArtist);

            var line = StringService.GetBillboardLine($"**{topArtist.ArtistName}**", i, previousPosition);
            lines.Add(line);

            if (i < 10)
            {
                artistDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Artists").WithValue(artistDescription.ToString())
            .WithIsInline(true));

        var rises = lines
            .Where(w => w.OldPosition is >= 20 && w.NewPosition <= 15 && w.PositionsMoved >= 15)
            .OrderBy(o => o.PositionsMoved)
            .ThenBy(o => o.NewPosition)
            .ToList();

        var risesDescription = new StringBuilder();
        if (rises.Any())
        {
            foreach (var rise in rises.Take(7))
            {
                risesDescription.Append($"<:5_or_more_up:912380324841918504>");
                risesDescription.AppendLine($"{rise.Name} (From #{rise.OldPosition} to #{rise.NewPosition})");
            }
        }

        if (risesDescription.Length > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Rises").WithValue(risesDescription.ToString()));
        }

        var drops = lines
            .Where(w => w.OldPosition is <= 15 && w.NewPosition >= 20 && w.PositionsMoved <= -15)
            .OrderBy(o => o.PositionsMoved)
            .ThenBy(o => o.OldPosition)
            .ToList();

        var dropsDescription = new StringBuilder();
        if (drops.Any())
        {
            foreach (var drop in drops.Take(7))
            {
                dropsDescription.Append($"<:5_or_more_down:912380324753838140> ");
                dropsDescription.AppendLine($"{drop.Name} (From #{drop.OldPosition} to #{drop.NewPosition})");
            }
        }

        if (dropsDescription.Length > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Drops").WithValue(dropsDescription.ToString()));
        }

        pages.Add(new PageBuilder()
            .WithFields(fields)
            .WithDescription(description.ToString())
            .WithTitle($"{userTitle} {year} in Review - 1/{pagesAmount}"));

        fields = new List<EmbedFieldBuilder>();

        var albumDescription = new StringBuilder();
        if (yearOverview.TopAlbums.TopAlbums.Any())
        {
            for (var i = 0; i < yearOverview.TopAlbums.TopAlbums.Take(8).Count(); i++)
            {
                var topAlbum = yearOverview.TopAlbums.TopAlbums[i];

                var previousTopAlbum =
                    yearOverview.PreviousTopAlbums?.TopAlbums?.FirstOrDefault(f =>
                        f.ArtistName == topAlbum.ArtistName && f.AlbumName == topAlbum.AlbumName);

                var previousPosition = previousTopAlbum == null
                    ? null
                    : yearOverview.PreviousTopAlbums?.TopAlbums?.IndexOf(previousTopAlbum);

                albumDescription.AppendLine(StringService
                    .GetBillboardLine(
                        $"**{topAlbum.ArtistName}** - **{StringExtensions.TruncateLongString(topAlbum.AlbumName, 40)}**",
                        i, previousPosition).Text);
            }

            fields.Add(new EmbedFieldBuilder().WithName("Albums").WithValue(albumDescription.ToString()));
        }

        var trackDescription = new StringBuilder();
        for (var i = 0; i < yearOverview.TopTracks.TopTracks.Take(8).Count(); i++)
        {
            var topTrack = yearOverview.TopTracks.TopTracks[i];

            var previousTopTrack =
                yearOverview.PreviousTopTracks?.TopTracks?.FirstOrDefault(f =>
                    f.ArtistName == topTrack.ArtistName && f.TrackName == topTrack.TrackName);

            var previousPosition = previousTopTrack == null
                ? null
                : yearOverview.PreviousTopTracks?.TopTracks?.IndexOf(previousTopTrack);

            trackDescription.AppendLine(StringService
                .GetBillboardLine($"**{topTrack.ArtistName}** - **{topTrack.TrackName}**", i, previousPosition).Text);
        }

        fields.Add(new EmbedFieldBuilder().WithName("Tracks").WithValue(trackDescription.ToString()));

        var countries = await this._countryService.GetTopCountriesForTopArtists(yearOverview.TopArtists.TopArtists);

        var previousTopCountries = new List<TopCountry>();
        if (yearOverview.PreviousTopArtists?.TopArtists != null)
        {
            previousTopCountries =
                await this._countryService.GetTopCountriesForTopArtists(yearOverview.PreviousTopArtists?.TopArtists);
        }

        var countryDescription = new StringBuilder();
        for (var i = 0; i < countries.Count; i++)
        {
            var topCountry = countries[i];

            var previousTopCountry = previousTopCountries.FirstOrDefault(f => f.CountryCode == topCountry.CountryCode);

            int? previousPosition =
                previousTopCountry == null ? null : previousTopCountries.IndexOf(previousTopCountry);

            var line = StringService.GetBillboardLine($"**{topCountry.CountryName}**", i, previousPosition);
            lines.Add(line);

            if (i < 8)
            {
                countryDescription.AppendLine(line.Text);
            }
        }

        fields.Add(new EmbedFieldBuilder().WithName("Countries").WithValue(countryDescription.ToString())
            .WithIsInline(true));

        var tracksAudioOverview =
            await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.TopTracks.TopTracks);
        var previousTracksAudioOverview =
            await this._trackService.GetAverageTrackAudioFeaturesForTopTracks(yearOverview.PreviousTopTracks
                ?.TopTracks);

        if (tracksAudioOverview.Total > 0)
        {
            fields.Add(new EmbedFieldBuilder().WithName("Top track analysis")
                .WithValue(TrackService.AudioFeatureAnalysisComparisonString(tracksAudioOverview,
                    previousTracksAudioOverview)));
        }

        var supporterDescription = context.ContextUser.UserType == UserType.User
            ? $"Want an extra page with your artist discoveries and a monthly overview? \n[Get .fmbot supporter here.]({Constants.GetSupporterDiscordLink})"
            : "";

        pages.Add(new PageBuilder()
            .WithDescription(supporterDescription)
            .WithFields(fields)
            .WithTitle($"{userTitle} {year} in Review - 2/{pagesAmount}"));

        if (userSettings.UserType != UserType.User)
        {
            fields = new List<EmbedFieldBuilder>();

            var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);
            allPlays = (await this._timeService.EnrichPlaysWithPlayTime(allPlays)).enrichedPlays;

            var filter = new DateTime(year, 01, 01);
            var endFilter = new DateTime(year, 12, 12, 23, 59, 59);
            var knownArtists = allPlays
                .Where(w => w.TimePlayed < filter)
                .GroupBy(g => g.ArtistName)
                .Select(s => s.Key)
                .ToList();

            var topNewArtists = allPlays
                .Where(w => w.TimePlayed >= filter && w.TimePlayed <= endFilter)
                .GroupBy(g => g.ArtistName)
                .Select(s => new TopArtist
                {
                    ArtistName = s.Key,
                    UserPlaycount = s.Count(),
                    FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed
                })
                .Where(w => !knownArtists.Any(a => a.Equals(w.ArtistName, StringComparison.InvariantCultureIgnoreCase)))
                .OrderByDescending(o => o.UserPlaycount)
                .Take(8)
                .ToList();

            var newArtistDescription = new StringBuilder();
            for (var i = 0; i < topNewArtists.Count(); i++)
            {
                var newArtist = topNewArtists.OrderBy(o => o.FirstPlay).ToList()[i];

                if (newArtistDescription.Length < 800)
                {
                    newArtistDescription.AppendLine(
                        $"**[{StringExtensions.TruncateLongString(newArtist.ArtistName, 28)}]({LastfmUrlExtensions.GetArtistUrl(newArtist.ArtistName)})** " +
                        $"— *{newArtist.UserPlaycount} {StringExtensions.GetPlaysString(newArtist.UserPlaycount)}* " +
                        $"— on **<t:{newArtist.FirstPlay.Value.ToUnixEpochDate()}:D>**");
                }
            }

            fields.Add(new EmbedFieldBuilder().WithName("Artist discoveries")
                .WithValue(newArtistDescription.ToString()));

            var monthDescription = new StringBuilder();
            var monthGroups = allPlays
                .Where(w => w.TimePlayed >= filter && w.TimePlayed <= endFilter)
                .OrderBy(o => o.TimePlayed)
                .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

            var totalPlayTime =
                TimeService.GetPlayTimeForEnrichedPlays(allPlays.Where(w =>
                    w.TimePlayed >= filter && w.TimePlayed <= endFilter));
            monthDescription.AppendLine(
                $"**`All`** " +
                $"- **{allPlays.Count(w => w.TimePlayed >= filter && w.TimePlayed <= endFilter).Format(context.NumberFormat)}** plays " +
                $"- **{StringExtensions.GetLongListeningTimeString(totalPlayTime)}**");

            foreach (var month in monthGroups)
            {
                if (!allPlays.Any(a => a.TimePlayed < DateTime.UtcNow.AddMonths(-month.Key.Month)))
                {
                    break;
                }

                var time = TimeService.GetPlayTimeForEnrichedPlays(month);
                monthDescription.AppendLine(
                    $"**`{CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(month.Key.Month)}`** " +
                    $"- **{month.Count().Format(context.NumberFormat)}** plays " +
                    $"- **{StringExtensions.GetLongListeningTimeString(time)}**");
            }

            if (monthDescription.Length > 0)
            {
                fields.Add(new EmbedFieldBuilder().WithName("Months")
                    .WithValue(monthDescription.ToString()));
            }

            pages.Add(new PageBuilder()
                .WithFields(fields)
                .WithTitle($"{userTitle} {year} in Review - 3/{pagesAmount}")
                .WithDescription("⭐ .fmbot Supporter stats"));
        }

        response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
        return response;
    }

    public static ResponseModel GapsSupporterRequired(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.UserType == UserType.User)
        {
            response.Embed.WithDescription(
                $"To see the biggest gaps between when you listened to certain artists we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault);
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        if (userSettings.UserType == UserType.User)
        {
            response.Embed.WithDescription(
                $"Sorry, artist gaps uses someone their lifetime listening history. You can only use this command on other supporters.");

            response.Components = new ComponentBuilder()
                .WithButton(".fmbot supporter", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault);
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public async Task<ResponseModel> ListeningGapsAsync(
        ContextModel context,
        TopListSettings topListSettings,
        UserSettingsModel userSettings,
        ResponseMode mode,
        GapEntityType entityType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var pages = new List<PageBuilder>();

        string userTitle;
        if (!userSettings.DifferentUser)
        {
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }

            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        if (context.ContextUser.LastUpdated < DateTime.UtcNow.AddHours(-1))
        {
            await this._updateService.UpdateUser(context.ContextUser);
        }

        var entityTypeDisplay = entityType.ToString();
        var userUrl =
            LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm, $"/library/{entityTypeDisplay.ToLower()}s");

        response.EmbedAuthor.WithName($"{entityTypeDisplay} listening gaps for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        if (allPlays == null || allPlays.Count == 0)
        {
            pages.Add(new PageBuilder()
                .WithDescription($"No plays found in your listening history.")
                .WithAuthor(response.EmbedAuthor));

            response.StaticPaginator = StringService.BuildStaticPaginator(pages, selectMenuBuilder: context.SelectMenu);
            response.ResponseType = ResponseType.Paginator;
            return response;
        }

        var entityPlays = entityType switch
        {
            GapEntityType.Artist => allPlays.OrderBy(o => o.TimePlayed)
                .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(d => d.Key, d => d.ToList()),
            GapEntityType.Album => allPlays
                .Where(w => !string.IsNullOrEmpty(w.AlbumName))
                .OrderBy(o => o.TimePlayed)
                .GroupBy(g => $"{g.ArtistName} - {g.AlbumName}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(d => d.Key, d => d.ToList()),
            GapEntityType.Track => allPlays.OrderBy(o => o.TimePlayed)
                .GroupBy(g => $"{g.ArtistName} - {g.TrackName}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(d => d.Key, d => d.ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null)
        };

        const int gapThresholdDays = 90;
        var minPlays = entityType switch
        {
            GapEntityType.Artist => 3,
            GapEntityType.Album => 2,
            GapEntityType.Track => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null)
        };

        var entitiesWithGaps =
            new List<(string DisplayName, string Artist, string ItemName, DateTime ResumeDate, TimeSpan GapDuration, int
                TotalPlays)>();

        foreach (var entity in entityPlays)
        {
            if (entity.Value.Count < minPlays)
            {
                continue;
            }

            var orderedPlays = entity.Value.OrderBy(o => o.TimePlayed).ToList();

            // Compare timestamps between consecutive plays to find gaps
            for (var i = 1; i < orderedPlays.Count; i++)
            {
                var previousPlay = orderedPlays[i - 1].TimePlayed;
                var currentPlay = orderedPlays[i].TimePlayed;
                var gap = currentPlay - previousPlay;

                if (gap.TotalDays >= gapThresholdDays)
                {
                    string artist;
                    string itemName = null;

                    switch (entityType)
                    {
                        case GapEntityType.Artist:
                            artist = orderedPlays[i].ArtistName;
                            break;
                        case GapEntityType.Album:
                            artist = orderedPlays[i].ArtistName;
                            itemName = orderedPlays[i].AlbumName;
                            break;
                        case GapEntityType.Track:
                            artist = orderedPlays[i].ArtistName;
                            itemName = orderedPlays[i].TrackName;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null);
                    }

                    entitiesWithGaps.Add((entity.Key, artist, itemName, currentPlay, gap, entity.Value.Count));
                    break;
                }
            }
        }

        var sortedEntitiesWithGaps = entitiesWithGaps
            .OrderByDescending(o => o.GapDuration.TotalDays)
            .ToList();

        var viewType = new SelectMenuBuilder()
            .WithPlaceholder("Select gap view")
            .WithCustomId(InteractionConstants.GapView)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in Enum.GetValues<GapEntityType>())
        {
            var name = option.GetAttribute<ChoiceDisplayAttribute>().Name;
            var value =
                $"{Enum.GetName(option)}-{Enum.GetName(mode)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}";

            var active = option == entityType;
            viewType.AddOption(new SelectMenuOptionBuilder(name, value, null, isDefault: active));
        }

        if (mode == ResponseMode.Image && sortedEntitiesWithGaps.Count != 0)
        {
            var topList = sortedEntitiesWithGaps.Take(10).Select(s => new TopListObject
            {
                Name = s.DisplayName,
                SubName = $"Gap: {StringExtensions.GetLongListeningTimeString(s.GapDuration)}",
                Playcount = s.TotalPlays
            }).ToList();

            string backgroundImage = null;

            if (entityType == GapEntityType.Artist)
            {
                backgroundImage =
                    (await this._artistsService.GetArtistFromDatabase(sortedEntitiesWithGaps.First().Artist))
                    ?.SpotifyImageUrl;
            }

            if (entityType == GapEntityType.Album)
            {
                backgroundImage =
                    (await this._albumService.GetAlbumFromDatabase(sortedEntitiesWithGaps.First().Artist,
                        sortedEntitiesWithGaps.First().ItemName))
                    ?.SpotifyImageUrl;
            }

            var image = await this._puppeteerService.GetTopList(userTitle, $"{entityTypeDisplay} listening gaps",
                $"returning {entityTypeDisplay.ToLower()}s",
                "Alltime", sortedEntitiesWithGaps.Count, sortedEntitiesWithGaps.Sum(s => s.TotalPlays), backgroundImage,
                topList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();

            response.FileName = $"{entityTypeDisplay.ToLower()}-gaps-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;
            response.Embed = null;
            response.Components = new ComponentBuilder().WithSelectMenu(viewType);

            return response;
        }

        var entityPages = sortedEntitiesWithGaps.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;

        foreach (var entityPage in entityPages)
        {
            var entityPageString = new StringBuilder();
            foreach (var gapEntity in entityPage)
            {
                entityPageString.Append($"{counter}. ");

                switch (entityType)
                {
                    case GapEntityType.Artist:
                        entityPageString.AppendLine(
                            $"**[{StringExtensions.TruncateLongString(gapEntity.Artist, 28)}]({LastfmUrlExtensions.GetArtistUrl(gapEntity.Artist)})** " +
                            // $"— *{gapEntity.TotalPlays.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(gapEntity.TotalPlays)}* " +
                            $"- Resumed **<t:{gapEntity.ResumeDate.ToUnixEpochDate()}:D>** " +
                            $"after **{(int)gapEntity.GapDuration.TotalDays} days**");
                        break;
                    case GapEntityType.Album:
                        entityPageString.AppendLine(
                            $"**[{StringExtensions.TruncateLongString(gapEntity.ItemName, 30)}]({LastfmUrlExtensions.GetAlbumUrl(gapEntity.Artist, gapEntity.ItemName)})** " +
                            $"by **[{StringExtensions.TruncateLongString(gapEntity.Artist, 20)}]({LastfmUrlExtensions.GetArtistUrl(gapEntity.Artist)})** " +
                            // $"— *{gapEntity.TotalPlays.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(gapEntity.TotalPlays)}* " +
                            $"- Resumed **<t:{gapEntity.ResumeDate.ToUnixEpochDate()}:D>** " +
                            $"after **{(int)gapEntity.GapDuration.TotalDays} days**");
                        break;
                    case GapEntityType.Track:
                        entityPageString.AppendLine(
                            $"**[{StringExtensions.TruncateLongString(gapEntity.ItemName, 30)}]({LastfmUrlExtensions.GetTrackUrl(gapEntity.Artist, gapEntity.ItemName)})** " +
                            $"by **[{StringExtensions.TruncateLongString(gapEntity.Artist, 20)}]({LastfmUrlExtensions.GetArtistUrl(gapEntity.Artist)})** " +
                            // $"— *{gapEntity.TotalPlays.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(gapEntity.TotalPlays)}* " +
                            $"- Resumed **<t:{gapEntity.ResumeDate.ToUnixEpochDate()}:D>** " +
                            $"after **{(int)gapEntity.GapDuration.TotalDays} days**");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null);
                }

                counter++;
            }

            var footer = new StringBuilder();

            footer.Append($"Page {pageCounter}/{entityPages.Count}");

            footer.Append(
                $" — {sortedEntitiesWithGaps.Count.Format(context.NumberFormat)} {entityTypeDisplay}s with gaps of {gapThresholdDays}+ days");
            footer.Append(
                $" — {minPlays}+ plays");

            pages.Add(new PageBuilder()
                .WithDescription(entityPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        if (entityPages.Count == 0)
        {
            pages.Add(new PageBuilder()
                .WithDescription($"No {entityTypeDisplay}s with listening gaps of {gapThresholdDays}+ days found.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.StaticPaginator = StringService.BuildStaticPaginatorWithSelectMenu(pages, viewType);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }
}
