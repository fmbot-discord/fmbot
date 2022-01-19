using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.EntityFrameworkCore.Metadata;
using Swan;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class ArtistBuilders
{
    private readonly ArtistsService _artistsService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;
    private readonly SpotifyService _spotifyService;
    private readonly UserService _userService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly PlayService _playService;
    private readonly IUpdateService _updateService;
    private readonly IIndexService _indexService;
    private readonly CrownService _crownService;
    private readonly WhoKnowsService _whoKnowsService;

    public ArtistBuilders(ArtistsService artistsService,
        LastFmRepository lastFmRepository,
        GuildService guildService,
        SpotifyService spotifyService,
        UserService userService,
        WhoKnowsArtistService whoKnowsArtistService,
        PlayService playService,
        IUpdateService updateService,
        IIndexService indexService,
        WhoKnowsPlayService whoKnowsPlayService,
        CrownService crownService,
        WhoKnowsService whoKnowsService)
    {
        this._artistsService = artistsService;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
        this._spotifyService = spotifyService;
        this._userService = userService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
        this._updateService = updateService;
        this._indexService = indexService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._crownService = crownService;
        this._whoKnowsService = whoKnowsService;
    }

    public async Task<ResponseModel> ArtistAsync(
        string prfx,
        IGuild discordGuild,
        IUser discordUser,
        User contextUser,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.GetArtist(response, discordUser, searchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        var spotifyArtistTask = this._spotifyService.GetOrStoreArtistAsync(artistSearch.artist, searchValue);

        var fullArtist = await spotifyArtistTask;

        var footer = new StringBuilder();
        if (fullArtist.SpotifyImageUrl != null)
        {
            response.Embed.WithThumbnailUrl(fullArtist.SpotifyImageUrl);
            footer.AppendLine("Image source: Spotify");
        }

        if (contextUser.TotalPlaycount.HasValue && artistSearch.artist.UserPlaycount is >= 10)
        {
            footer.AppendLine($"{(decimal)artistSearch.artist.UserPlaycount.Value / contextUser.TotalPlaycount.Value:P} of all your scrobbles are on this artist");
        }

        var userTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);

        response.EmbedAuthor.WithName($"Artist info about {artistSearch.artist.ArtistName} for {userTitle}");
        response.EmbedAuthor.WithUrl(artistSearch.artist.ArtistUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        if (!string.IsNullOrWhiteSpace(fullArtist.Type))
        {
            var artistInfo = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(fullArtist.Disambiguation))
            {
                if (fullArtist.Location != null)
                {
                    artistInfo.Append($"**{fullArtist.Disambiguation}**");
                    artistInfo.Append($" from **{fullArtist.Location}**");
                    artistInfo.AppendLine();
                }
                else
                {
                    artistInfo.AppendLine($"**{fullArtist.Disambiguation}**");
                }
            }
            if (fullArtist.Location != null && string.IsNullOrWhiteSpace(fullArtist.Disambiguation))
            {
                artistInfo.AppendLine($"{fullArtist.Location}");
            }
            if (fullArtist.Type != null)
            {
                artistInfo.Append($"{fullArtist.Type}");
                if (fullArtist.Gender != null)
                {
                    artistInfo.Append($" - ");
                    artistInfo.Append($"{fullArtist.Gender}");
                }

                artistInfo.AppendLine();
            }
            if (fullArtist.StartDate.HasValue && !fullArtist.EndDate.HasValue)
            {
                var specifiedDateTime = DateTime.SpecifyKind(fullArtist.StartDate.Value, DateTimeKind.Utc);
                var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                if (fullArtist.Type?.ToLower() == "person")
                {
                    artistInfo.AppendLine($"Born: <t:{dateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
                }
                else
                {
                    artistInfo.AppendLine($"Started: <t:{dateValue}:D>");
                }
            }
            if (fullArtist.StartDate.HasValue && fullArtist.EndDate.HasValue)
            {
                var specifiedStartDateTime = DateTime.SpecifyKind(fullArtist.StartDate.Value, DateTimeKind.Utc);
                var startDateValue = ((DateTimeOffset)specifiedStartDateTime).ToUnixTimeSeconds();

                var specifiedEndDateTime = DateTime.SpecifyKind(fullArtist.EndDate.Value, DateTimeKind.Utc);
                var endDateValue = ((DateTimeOffset)specifiedEndDateTime).ToUnixTimeSeconds();

                if (fullArtist.Type?.ToLower() == "person")
                {
                    artistInfo.AppendLine($"Born: <t:{startDateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
                    artistInfo.AppendLine($"Died: <t:{endDateValue}:D>");
                }
                else
                {
                    artistInfo.AppendLine($"Started: <t:{startDateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
                    artistInfo.AppendLine($"Stopped: <t:{endDateValue}:D>");
                }
            }

            if (artistInfo.Length > 0)
            {
                response.Embed.WithDescription(artistInfo.ToString());
            }
        }

        if (discordGuild != null)
        {
            var serverStats = "";
            var guild = await this._guildService.GetGuildForWhoKnows(discordGuild.Id);

            if (guild?.LastIndexed != null)
            {
                var usersWithArtist = await this._whoKnowsArtistService.GetIndexedUsersForArtist(discordGuild, guild.GuildId, artistSearch.artist.ArtistName);
                var filteredUsersWithArtist = WhoKnowsService.FilterGuildUsersAsync(usersWithArtist, guild);

                if (filteredUsersWithArtist.Count != 0)
                {
                    var serverListeners = filteredUsersWithArtist.Count;
                    var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);
                    var serverPlaycountLastWeek = await this._whoKnowsArtistService.GetWeekArtistPlaycountForGuildAsync(guild.GuildId, artistSearch.artist.ArtistName);

                    serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                    serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                    serverStats += $"\n`{(int)avgServerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                    serverStats += $"\n`{serverPlaycountLastWeek}` {StringExtensions.GetPlaysString(serverPlaycountLastWeek)} last week";

                    if (usersWithArtist.Count > filteredUsersWithArtist.Count)
                    {
                        var filteredAmount = usersWithArtist.Count - filteredUsersWithArtist.Count;
                        serverStats += $"\n`{filteredAmount}` users filtered";
                    }
                }
            }
            else
            {
                serverStats += $"Run `{prfx}index` to get server stats";
            }

            if (!string.IsNullOrWhiteSpace(serverStats))
            {
                response.Embed.AddField("Server stats", serverStats, true);
            }
        }

        var globalStats = "";
        globalStats += $"`{artistSearch.artist.TotalListeners}` {StringExtensions.GetListenersString(artistSearch.artist.TotalListeners)}";
        globalStats += $"\n`{artistSearch.artist.TotalPlaycount}` global {StringExtensions.GetPlaysString(artistSearch.artist.TotalPlaycount)}";
        if (artistSearch.artist.UserPlaycount.HasValue)
        {
            globalStats += $"\n`{artistSearch.artist.UserPlaycount}` {StringExtensions.GetPlaysString(artistSearch.artist.UserPlaycount)} by you";
            globalStats += $"\n`{await this._playService.GetArtistPlaycountForTimePeriodAsync(contextUser.UserId, artistSearch.artist.ArtistName)}` by you last week";
            await this._updateService.CorrectUserArtistPlaycount(contextUser.UserId, artistSearch.artist.ArtistName,
                artistSearch.artist.UserPlaycount.Value);
        }

        response.Embed.AddField("Last.fm stats", globalStats, true);

        if (artistSearch.artist.Description != null)
        {
            response.Embed.AddField("Summary", artistSearch.artist.Description);
        }

        //if (artist.Tags != null && artist.Tags.Any() && (fullArtist.ArtistGenres == null || !fullArtist.ArtistGenres.Any()))
        //{
        //    var tags = LastFmRepository.TagsToLinkedString(artist.Tags);

        //    response.Embed.AddField("Tags", tags);
        //}

        if (fullArtist.ArtistGenres != null && fullArtist.ArtistGenres.Any())
        {
            footer.AppendLine(GenreService.GenresToString(fullArtist.ArtistGenres.ToList()));
        }

        response.Embed.WithFooter(footer.ToString());
        return response;
    }

    public async Task<ResponseModel> ArtistTracksAsync(
        IGuild discordGuild,
        IUser discordUser,
        User contextUser,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings,
        string searchValue)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.GetArtist(response, discordUser, searchValue, contextUser.UserNameLastFM,
            contextUser.SessionKeyLastFm, userSettings.UserNameLastFm, true, userSettings.UserId);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        if (artistSearch.artist.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistSearch.artist.ArtistName,
                artistSearch.artist.UserPlaycount.Value);
        }

        var timeDescription = timeSettings.Description.ToLower();
        List<UserTrack> topTracks;
        switch (timeSettings.TimePeriod)
        {
            case TimePeriod.Weekly:
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 7, artistSearch.artist.ArtistName);
                break;
            case TimePeriod.Monthly:
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 31, artistSearch.artist.ArtistName);
                break;
            default:
                timeDescription = "alltime";
                topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId, artistSearch.artist.ArtistName);
                break;
        }

        var pages = new List<PageBuilder>();
        var userTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);

        if (topTracks.Count == 0)
        {
            response.Embed.WithDescription(
                $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()} has no registered tracks for the artist **{artistSearch.artist.ArtistName}** in .fmbot.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var url = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/music/{UrlEncoder.Default.Encode(artistSearch.artist.ArtistName)}";
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            response.EmbedAuthor.WithUrl(url);
        }

        var topTrackPages = topTracks.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var topTrackPage in topTrackPages)
        {
            var albumPageString = new StringBuilder();
            foreach (var track in topTrackPage)
            {
                albumPageString.AppendLine($"{counter}. **{track.Name}** ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)})");
                counter++;
            }

            var footer = new StringBuilder();
            footer.AppendLine($"Page {pageCounter}/{topTrackPages.Count} - {topTracks.Count} different tracks");
            var title = new StringBuilder();

            if (userSettings.DifferentUser && userSettings.UserId != contextUser.UserId)
            {
                footer.AppendLine($"{userSettings.UserNameLastFm} has {artistSearch.artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DiscordUserName} their top tracks for '{artistSearch.artist.ArtistName}'");
            }
            else
            {
                footer.Append($"{userTitle} has {artistSearch.artist.UserPlaycount} total scrobbles on this artist");
                title.Append($"Your top tracks for '{artistSearch.artist.ArtistName}'");

                response.EmbedAuthor.WithIconUrl(discordUser.GetAvatarUrl());
            }

            response.EmbedAuthor.WithName(title.ToString());

            pages.Add(new PageBuilder()
                .WithDescription(albumPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> GuildArtistsAsync(
        string prfx,
        IGuild discordGuild,
        Guild guild,
        GuildRankingSettings guildListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        ICollection<GuildArtist> topGuildArtists;
        IList<GuildArtist> previousTopGuildArtists = null;
        if (guildListSettings.ChartTimePeriod == TimePeriod.AllTime)
        {
            topGuildArtists = await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuild(guild.GuildId, guildListSettings.OrderType);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId,
                guildListSettings.AmountOfDaysWithBillboard);

            topGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.StartDateTime, guildListSettings.OrderType);
            previousTopGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType);
        }

        var title = $"Top {guildListSettings.TimeDescription.ToLower()} artists in {discordGuild.Name}";

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific track listeners with '{prfx}whoknows'");
                break;
            case 2:
                footer.AppendLine($"Available time periods: alltime, monthly, weekly and daily");
                break;
            case 3:
                footer.AppendLine($"Available sorting options: plays and listeners");
                break;
        }

        var artistPages = topGuildArtists.Chunk(12).ToList();

        var counter = 1;
        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in artistPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in page)
            {
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{track.ListenerCount}` · **{track.ArtistName}** ({track.TotalPlaycount} {StringExtensions.GetPlaysString(track.TotalPlaycount)})"
                    : $"`{track.TotalPlaycount}` · **{track.ArtistName}** ({track.ListenerCount} {StringExtensions.GetListenersString(track.ListenerCount)})";

                if (previousTopGuildArtists != null && previousTopGuildArtists.Any())
                {
                    var previousTopArtist = previousTopGuildArtists.FirstOrDefault(f => f.ArtistName == track.ArtistName);
                    int? previousPosition = previousTopArtist == null ? null : previousTopGuildArtists.IndexOf(previousTopArtist);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false).Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append($"Page {pageCounter}/{artistPages.Count}");
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

    public async Task<ResponseModel> ArtistPaceAsync(
        IUser discordUser,
        User contextUser,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        string amount,
        long timeFrom,
        string artistName)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var goalAmount = SettingService.GetGoalAmount(amount, 0);

        if (artistName == null && amount != null)
        {
            artistName = amount
                .Replace(goalAmount.ToString(), "")
                .Replace($"{(int)Math.Floor((double)(goalAmount / 1000))}k", "")
                .TrimEnd()
                .TrimStart();
        }

        var artistSearch = await this._artistsService.GetArtist(response, discordUser, artistName, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, userSettings.UserNameLastFm);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        goalAmount = SettingService.GetGoalAmount(amount, artistSearch.artist.UserPlaycount.GetValueOrDefault(0));

        var regularPlayCount = await this._lastFmRepository.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeFrom, userSettings.SessionKeyLastFm);

        if (regularPlayCount is null or 0)
        {
            response.Text = $"<@{discordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var artistPlayCount =
            await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId, artistSearch.artist.ArtistName,
                timeSettings.PlayDays.GetValueOrDefault(30));

        if (artistPlayCount is 0)
        {
            response.Text = $"<@{discordUser.Id}> No plays found on **{artistSearch.artist.ArtistName}** in the last {timeSettings.PlayDays} days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
        var totalDays = (DateTime.UtcNow - age).TotalDays;

        var playsLeft = goalAmount - artistSearch.artist.UserPlaycount.GetValueOrDefault(0);

        var avgPerDay = artistPlayCount / totalDays;

        var goalDate = DateTime.UtcNow.AddDays(playsLeft / avgPerDay);

        var reply = new StringBuilder();

        var determiner = "your";
        if (userSettings.DifferentUser)
        {
            reply.Append($"<@{discordUser.Id}> My estimate is that the user '{userSettings.UserNameLastFm.FilterOutMentions()}'");
            determiner = "their";
        }
        else
        {
            reply.Append($"<@{discordUser.Id}> My estimate is that you");
        }

        reply.AppendLine($" will reach **{goalAmount}** plays on **{artistSearch.artist.ArtistName}** on **<t:{goalDate.ToUnixEpochDate()}:D>**.");


        reply.AppendLine(
            $"This is based on {determiner} avg of {Math.Round(avgPerDay, 1)} per day in the last {Math.Round(totalDays, 0)} days ({artistPlayCount} total - {artistSearch.artist.UserPlaycount} alltime)");

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> WhoKnowsArtistAsync(
        string prfx,
        IGuild discordGuild,
        IUser discordUser,
        Guild contextGuild,
        User contextUser,
        string artistValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.GetArtist(response, discordUser, artistValues, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedArtists: true, userId: contextUser.UserId);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistSearch.artist, artistSearch.artist.ArtistName);

        var currentUser = await this._indexService.GetOrAddUserToGuild(contextGuild, await discordGuild.GetUserAsync(contextUser.DiscordUserId), contextUser);

        if (!contextGuild.GuildUsers.Select(s => s.UserId).Contains(contextUser.UserId))
        {
            contextGuild.GuildUsers.Add(currentUser);
        }

        await this._indexService.UpdateGuildUser(await discordGuild.GetUserAsync(contextUser.DiscordUserId), currentUser.UserId, contextGuild);

        var usersWithArtist = await this._whoKnowsArtistService.GetIndexedUsersForArtist(discordGuild, contextGuild.GuildId, artistSearch.artist.ArtistName);

        if (artistSearch.artist.UserPlaycount != 0)
        {
            usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, currentUser, artistSearch.artist.ArtistName, artistSearch.artist.UserPlaycount);
        }

        var filteredUsersWithArtist = WhoKnowsService.FilterGuildUsersAsync(usersWithArtist, contextGuild);

        CrownModel crownModel = null;
        if (contextGuild.CrownsDisabled != true && filteredUsersWithArtist.Count >= 1)
        {
            crownModel =
                await this._crownService.GetAndUpdateCrownForArtist(filteredUsersWithArtist, contextGuild, artistSearch.artist.ArtistName);
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, contextUser.UserId, PrivacyLevel.Server, crownModel);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = new StringBuilder();
        if (cachedArtist?.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        var userTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);
        footer.AppendLine($"WhoKnows artist requested by {userTitle}");

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(discordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-100))
        {
            footer.AppendLine($"Missing members? Update with {prfx}index");
        }

        if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
        {
            var serverListeners = filteredUsersWithArtist.Count;
            var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

            footer.Append($"{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append($"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.AppendLine($"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(contextUser.UserId,
            contextGuild, artistSearch.artist.ArtistName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (usersWithArtist.Count > filteredUsersWithArtist.Count && !contextGuild.WhoKnowsWhitelistRoleId.HasValue)
        {
            var filteredAmount = usersWithArtist.Count - filteredUsersWithArtist.Count;
            footer.AppendLine($"{filteredAmount} inactive/blocked users filtered");
        }
        if (contextGuild.WhoKnowsWhitelistRoleId.HasValue)
        {
            footer.AppendLine($"Users with WhoKnows whitelisted role only");
        }

        response.Embed.WithTitle($"{artistSearch.artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} in {discordGuild.Name}");

        if (artistSearch.artist.ArtistUrl != null && Uri.IsWellFormedUriString(artistSearch.artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (cachedArtist.SpotifyImageUrl != null)
        {
            response.Embed.WithThumbnailUrl(cachedArtist.SpotifyImageUrl);
        }

        return response;
    }

    public async Task<ResponseModel> GlobalWhoKnowsArtistAsync(
        string prfx,
        IGuild discordGuild,
        IUser discordUser,
        Guild contextGuild,
        User contextUser,
        WhoKnowsSettings settings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.GetArtist(response, discordUser, settings.NewSearchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm, useCachedArtists: true, userId: contextUser.UserId);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistSearch.artist, artistSearch.artist.ArtistName);

        var usersWithArtist = await this._whoKnowsArtistService.GetGlobalUsersForArtists(discordGuild, artistSearch.artist.ArtistName);

        if (artistSearch.artist.UserPlaycount != 0 && discordGuild != null)
        {
            var discordGuildUser = await discordGuild.GetUserAsync(contextUser.DiscordUserId);
            var guildUser = new GuildUser
            {
                UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : contextUser.UserNameLastFM,
                User = contextUser
            };
            usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, artistSearch.artist.ArtistName, artistSearch.artist.UserPlaycount);
        }

        var filteredUsersWithArtist = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

        var privacyLevel = PrivacyLevel.Global;

        if (contextGuild != null)
        {
            filteredUsersWithArtist =
                WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithArtist, contextGuild.GuildUsers.ToList());

            if (settings.AdminView && contextGuild.SpecialGuild == true)
            {
                privacyLevel = PrivacyLevel.Server;
            }
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, contextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody that uses .fmbot has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = new StringBuilder();
        if (cachedArtist?.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        var userTitle = await this._userService.GetUserTitleAsync(discordGuild, discordUser);
        footer.AppendLine($"Global WhoKnows artist requested by {userTitle}");

        if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
        {
            var globalListeners = filteredUsersWithArtist.Count;
            var globalPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
            var avgPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

            footer.Append($"{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append($"{globalPlaycount} total {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{(int)avgPlaycount} avg {StringExtensions.GetPlaysString((int)avgPlaycount)}");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(contextUser.UserId,
            contextGuild, artistSearch.artist.ArtistName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (settings.AdminView)
        {
            footer.AppendLine("Admin view enabled - not for public channels");
        }
        if (contextUser.PrivacyLevel != PrivacyLevel.Global)
        {
            footer.AppendLine($"You are currently not globally visible - use '{prfx}privacy global' to enable.");
        }

        if (settings.HidePrivateUsers)
        {
            footer.AppendLine("All private users are hidden from results");
        }

        response.Embed.WithTitle($"{artistSearch.artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} globally");

        if (Uri.IsWellFormedUriString(artistSearch.artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (cachedArtist?.SpotifyImageUrl != null)
        {
            response.Embed.WithThumbnailUrl(cachedArtist.SpotifyImageUrl);
        }

        return response;
    }
}
