using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.AppleMusic;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Gateway;
using DiscordGuild = NetCord.Gateway.Guild;
using NetCord.Rest;
using SkiaSharp;
using Guild = FMBot.Persistence.Domain.Models.Guild;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class ArtistBuilders
{
    private readonly ArtistsService _artistsService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GuildService _guildService;
    private readonly UserService _userService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly WhoKnowsPlayService _whoKnowsPlayService;
    private readonly PlayService _playService;
    private readonly UpdateService _updateService;
    private readonly IndexService _indexService;
    private readonly CrownService _crownService;
    private readonly WhoKnowsService _whoKnowsService;
    private readonly SmallIndexRepository _smallIndexRepository;
    private readonly SupporterService _supporterService;
    private readonly PuppeteerService _puppeteerService;
    private readonly CountryService _countryService;
    private readonly GenreService _genreService;
    private readonly DiscogsService _discogsService;
    private readonly CensorService _censorService;
    private readonly FeaturedService _featuredService;
    private readonly MusicDataFactory _musicDataFactory;
    private readonly ShardedGatewayClient _client;
    private readonly IMemoryCache _cache;


    public ArtistBuilders(ArtistsService artistsService,
        IDataSourceFactory dataSourceFactory,
        GuildService guildService,
        UserService userService,
        WhoKnowsArtistService whoKnowsArtistService,
        PlayService playService,
        UpdateService updateService,
        IndexService indexService,
        WhoKnowsPlayService whoKnowsPlayService,
        CrownService crownService,
        WhoKnowsService whoKnowsService,
        SmallIndexRepository smallIndexRepository,
        SupporterService supporterService,
        PuppeteerService puppeteerService,
        CountryService countryService,
        GenreService genreService,
        DiscogsService discogsService,
        CensorService censorService,
        FeaturedService featuredService,
        MusicDataFactory musicDataFactory,
        ShardedGatewayClient client,
        IMemoryCache cache)
    {
        this._artistsService = artistsService;
        this._dataSourceFactory = dataSourceFactory;
        this._guildService = guildService;
        this._userService = userService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
        this._updateService = updateService;
        this._indexService = indexService;
        this._whoKnowsPlayService = whoKnowsPlayService;
        this._crownService = crownService;
        this._whoKnowsService = whoKnowsService;
        this._smallIndexRepository = smallIndexRepository;
        this._supporterService = supporterService;
        this._puppeteerService = puppeteerService;
        this._countryService = countryService;
        this._genreService = genreService;
        this._discogsService = discogsService;
        this._censorService = censorService;
        this._featuredService = featuredService;
        this._musicDataFactory = musicDataFactory;
        this._client = client;
        this._cache = cache;
    }

    public async Task<ResponseModel> ArtistInfoAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            artistSearch.Response.ResponseType = ResponseType.ComponentsV2;
            artistSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            artistSearch.Response.ComponentsContainer.WithTextDisplay(artistSearch.Response.Embed.Description);
            return artistSearch.Response;
        }

        var fullArtistTask = this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, searchValue, redirectsEnabled);
        var userTitleTask = this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var featuredHistoryTask = this._featuredService.GetArtistFeaturedHistory(artistSearch.Artist.ArtistName);

        Task<DateTime?> firstPlayTask = null;
        if (context.ContextUser.UserType != UserType.User && artistSearch.Artist.UserPlaycount > 0)
        {
            firstPlayTask = this._playService.GetArtistFirstPlayDate(context.ContextUser.UserId,
                artistSearch.Artist.ArtistName);
        }

        Task<(int week, int month)> recentPlaycountsTask = null;
        if (artistSearch.Artist.UserPlaycount.HasValue)
        {
            recentPlaycountsTask = this._playService.GetRecentArtistPlaycounts(context.ContextUser.UserId,
                artistSearch.Artist.ArtistName);
        }

        Guild guild = null;
        IDictionary<int, FullGuildUser> guildUsers = null;
        Task<IList<WhoKnowsObjectWithUser>> indexedUsersTask = null;
        if (context.DiscordGuild != null)
        {
            var guildTask = this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
            var guildUsersTask = this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            guild = await guildTask;
            guildUsers = await guildUsersTask;

            if (guild?.LastIndexed != null)
            {
                indexedUsersTask = this._whoKnowsArtistService.GetIndexedUsersForArtist(context.DiscordGuild,
                    guildUsers, guild.GuildId, artistSearch.Artist.ArtistName);
            }
        }

        var fullArtist = await fullArtistTask;
        var userTitle = await userTitleTask;
        var featuredHistory = await featuredHistoryTask;

        var showThumbnail = false;
        if (fullArtist.SpotifyImageUrl != null)
        {
            var safeForChannelTask = this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel, fullArtist.Name);
            var accentColorTask = this._artistsService.GetArtistAccentColorAsync(
                fullArtist.SpotifyImageUrl, fullArtist.Id, fullArtist.Name);

            if (await safeForChannelTask == CensorService.CensorResult.Safe)
            {
                showThumbnail = true;
            }

            response.ComponentsContainer.WithAccentColor(await accentColorTask);
        }

        var headerSection = new StringBuilder();
        headerSection.AppendLine(artistSearch.Artist.ArtistUrl != null
            ? $"## [{artistSearch.Artist.ArtistName}]({artistSearch.Artist.ArtistUrl})"
            : $"## {artistSearch.Artist.ArtistName}");

        var countryFlag = fullArtist.CountryCode != null
            ? this._countryService.Countries.FirstOrDefault(f => f.Code == fullArtist.CountryCode)?.Emoji
            : null;

        if (!string.IsNullOrWhiteSpace(fullArtist.Disambiguation))
        {
            if (fullArtist.Location != null)
            {
                headerSection.AppendLine(countryFlag != null
                    ? $"**{fullArtist.Disambiguation}** from **{fullArtist.Location}** {countryFlag}"
                    : $"**{fullArtist.Disambiguation}** from **{fullArtist.Location}**");
            }
            else
            {
                headerSection.AppendLine($"**{fullArtist.Disambiguation}**");
            }
        }

        if (fullArtist.Location != null && string.IsNullOrWhiteSpace(fullArtist.Disambiguation))
        {
            headerSection.AppendLine(countryFlag != null
                ? $"Artist from **{fullArtist.Location}** {countryFlag}"
                : $"Artist from **{fullArtist.Location}**");
        }

        if (fullArtist.Type != null)
        {
            headerSection.Append($"{fullArtist.Type}");
            if (fullArtist.Gender != null)
            {
                headerSection.Append($" - {fullArtist.Gender}");
            }
            headerSection.AppendLine();
        }

        if (fullArtist.StartDate.HasValue && !fullArtist.EndDate.HasValue)
        {
            var specifiedDateTime = DateTime.SpecifyKind(fullArtist.StartDate.Value, DateTimeKind.Utc);
            var dateValue = ((DateTimeOffset)specifiedDateTime).AddHours(12).ToUnixTimeSeconds();

            if (fullArtist.Type?.ToLower() == "person")
            {
                headerSection.AppendLine(
                    $"Born: <t:{dateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
            }
            else
            {
                headerSection.AppendLine($"Started: <t:{dateValue}:D>");
            }
        }

        if (fullArtist.StartDate.HasValue && fullArtist.EndDate.HasValue)
        {
            var specifiedStartDateTime = DateTime.SpecifyKind(fullArtist.StartDate.Value, DateTimeKind.Utc);
            var startDateValue = ((DateTimeOffset)specifiedStartDateTime).AddHours(12).ToUnixTimeSeconds();
            var specifiedEndDateTime = DateTime.SpecifyKind(fullArtist.EndDate.Value, DateTimeKind.Utc);
            var endDateValue = ((DateTimeOffset)specifiedEndDateTime).AddHours(12).ToUnixTimeSeconds();

            if (fullArtist.Type?.ToLower() == "person")
            {
                headerSection.AppendLine(
                    $"Born: <t:{startDateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
                headerSection.AppendLine($"Died: <t:{endDateValue}:D>");
            }
            else
            {
                headerSection.AppendLine(
                    $"Started: <t:{startDateValue}:D> {ArtistsService.IsArtistBirthday(fullArtist.StartDate)}");
                headerSection.AppendLine($"Stopped: <t:{endDateValue}:D>");
            }
        }

        var hasMusicBrainzInfo = !string.IsNullOrWhiteSpace(fullArtist.Disambiguation) ||
            fullArtist.Location != null || fullArtist.Type != null || fullArtist.StartDate.HasValue;

        StringBuilder userStats = null;
        if (artistSearch.Artist.UserPlaycount.HasValue)
        {
            var correctPlaycountTask = this._updateService.CorrectUserArtistPlaycount(context.ContextUser.UserId,
                artistSearch.Artist.ArtistName, artistSearch.Artist.UserPlaycount.Value);

            userStats = new StringBuilder();
            var playsLine =
                $"**{artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)} by **{userTitle}**";

            if (recentPlaycountsTask != null)
            {
                var recentPlaycounts = await recentPlaycountsTask;
                if (recentPlaycounts.month > 0)
                {
                    playsLine += $" — **{recentPlaycounts.month.Format(context.NumberFormat)}** last month";
                }
            }

            userStats.AppendLine(playsLine);

            if (context.ContextUser.TotalPlaycount.HasValue && artistSearch.Artist.UserPlaycount is >= 30)
            {
                userStats.AppendLine(
                    $"**{((decimal)artistSearch.Artist.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value).FormatPercentage(context.NumberFormat)}** of all your plays");
            }

            if (firstPlayTask != null)
            {
                var firstPlay = await firstPlayTask;
                if (firstPlay != null)
                {
                    var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();
                    userStats.AppendLine($"Discovered <t:{firstListenValue}:D>");
                }
            }
            else
            {
                var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
                if (randomHintNumber == 1 &&
                    this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType,
                        context.DiscordGuild?.Id))
                {
                    this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                    userStats.AppendLine(
                        $"*[Supporters]({Constants.GetSupporterDiscordLink}) can see artist discovery dates.*");
                }
            }

            await correctPlaycountTask;
        }

        if (!hasMusicBrainzInfo && showThumbnail && userStats != null)
        {
            headerSection.AppendLine();
            headerSection.Append(userStats.ToString().TrimEnd());
        }

        if (showThumbnail)
        {
            response.ComponentsContainer.WithSection([
                new TextDisplayProperties(headerSection.ToString().TrimEnd())
            ], fullArtist.SpotifyImageUrl);
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(headerSection.ToString().TrimEnd()));
        }

        if (userStats != null && (hasMusicBrainzInfo || !showThumbnail))
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(userStats.ToString().TrimEnd()));
        }


        var statsSection = new StringBuilder();

        if (context.DiscordGuild != null)
        {
            if (indexedUsersTask != null)
            {
                var usersWithArtist = await indexedUsersTask;
                var (_, filteredUsersWithArtist) =
                    WhoKnowsService.FilterWhoKnowsObjects(usersWithArtist, guildUsers, guild,
                        context.ContextUser.UserId);

                if (filteredUsersWithArtist.Count != 0)
                {
                    var serverListeners = filteredUsersWithArtist.Count;
                    var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);

                    statsSection.AppendLine(
                        $"**{serverPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(serverPlaycount)} in this server by **{serverListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(serverListeners)}");
                }
            }

            var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(context.ContextUser.UserId,
                guildUsers, guild, artistSearch.Artist.ArtistName);

            if (guildAlsoPlaying != null)
            {
                statsSection.AppendLine(guildAlsoPlaying);
            }
        }

        statsSection.AppendLine(
            $"**{artistSearch.Artist.TotalPlaycount.Format(context.NumberFormat)}** Last.fm {StringExtensions.GetPlaysString(artistSearch.Artist.TotalPlaycount)} by **{artistSearch.Artist.TotalListeners.Format(context.NumberFormat)}** {StringExtensions.GetListenersString(artistSearch.Artist.TotalListeners)}");

        if (featuredHistory.Any())
        {
            statsSection.AppendLine($"Featured **{featuredHistory.Count}** {StringExtensions.GetTimesString(featuredHistory.Count)}");
        }

        if (artistSearch.IsRandom)
        {
            statsSection.AppendLine(
                $"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
        }

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(statsSection.ToString().TrimEnd()));

        if (artistSearch.Artist.Description != null)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(artistSearch.Artist.Description));
        }

        if (fullArtist.ArtistGenres != null && fullArtist.ArtistGenres.Any())
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                $"-# {GenreService.GenresToString(fullArtist.ArtistGenres.ToList())}"));
        }

        var viewingUserId = userSettings.DiscordUserId;
        var navRow = new ActionRowProperties()
            .WithButton("Overview",
                $"{InteractionConstants.Artist.Overview}:{fullArtist.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"))
            .WithButton("Tracks",
                $"{InteractionConstants.Artist.Tracks}:{fullArtist.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83c\udfb6"))
            .WithButton("Albums",
                $"{InteractionConstants.Artist.Albums}:{fullArtist.Id}:{viewingUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcbd"));
        response.ComponentsContainer.WithActionRow(navRow);

        var socialRow = new ActionRowProperties();
        var hasSocialLinks = false;

        if (context.ContextUser.RymEnabled == true && fullArtist.ArtistLinks != null &&
            fullArtist.ArtistLinks.Any(a => a.Type == LinkType.RateYourMusic))
        {
            var rym = fullArtist.ArtistLinks.First(f => f.Type == LinkType.RateYourMusic);
            socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.RateYourMusic), url: rym.Url);
            hasSocialLinks = true;
        }
        else if (fullArtist.SpotifyId != null)
        {
            socialRow.WithButton(
                emote: EmojiProperties.Custom(DiscordConstants.Spotify),
                url: $"https://open.spotify.com/artist/{fullArtist.SpotifyId}");
            hasSocialLinks = true;

            if (fullArtist.AppleMusicUrl != null)
            {
                socialRow.WithButton(
                    emote: EmojiProperties.Custom(DiscordConstants.AppleMusic), url: fullArtist.AppleMusicUrl);
            }
        }

        if (fullArtist.ArtistLinks != null && fullArtist.ArtistLinks.Any())
        {
            var facebook = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Facebook);
            if (facebook != null && fullArtist.ArtistLinks.All(a => a.Type != LinkType.Instagram))
            {
                socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.Facebook), url: facebook.Url);
                hasSocialLinks = true;
            }

            var instagram = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Instagram);
            if (instagram != null)
            {
                socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.Instagram), url: instagram.Url);
                hasSocialLinks = true;
            }

            var twitter = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Twitter);
            if (twitter != null)
            {
                socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.Twitter), url: twitter.Url);
                hasSocialLinks = true;
            }

            var tiktok = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.TikTok);
            if (tiktok != null)
            {
                socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.TikTok), url: tiktok.Url);
                hasSocialLinks = true;
            }

            var bandcamp = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Bandcamp);
            if (bandcamp != null)
            {
                socialRow.WithButton(emote: EmojiProperties.Custom(DiscordConstants.Bandcamp), url: bandcamp.Url);
                hasSocialLinks = true;
            }
        }

        if (hasSocialLinks)
        {
            response.ComponentsContainer.WithActionRow(socialRow);
        }

        return response;
    }

    public async Task<ResponseModel> ArtistOverviewAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId,
            otherUserUsername: userSettings.UserNameLastFm, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            artistSearch.Response.ResponseType = ResponseType.ComponentsV2;
            artistSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            artistSearch.Response.ComponentsContainer.WithTextDisplay(artistSearch.Response.Embed.Description);
            return artistSearch.Response;
        }

        var fullArtistTask = this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, searchValue, redirectsEnabled);

        var user = context.ContextUser;
        var determiner = "Your";
        string userTitle;

        if (userSettings.DifferentUser)
        {
            var titleTask = this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
            var discogsTask = this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

            userTitle = $"{userSettings.DisplayName}, requested by {await titleTask}";
            user = await discogsTask;
            determiner = "Their";
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        Task<DateTime?> firstPlayTask = null;
        if (user.UserType != UserType.User && artistSearch.Artist.UserPlaycount > 0)
        {
            firstPlayTask = this._playService.GetArtistFirstPlayDate(user.UserId, artistSearch.Artist.ArtistName);
        }

        Task<(int week, int month)> recentPlaycountsTask = null;
        if (artistSearch.Artist.UserPlaycount > 0)
        {
            recentPlaycountsTask = this._playService.GetRecentArtistPlaycounts(userSettings.UserId,
                artistSearch.Artist.ArtistName);
        }

        var fullArtist = await fullArtistTask;

        var showThumbnail = false;
        if (fullArtist.SpotifyImageUrl != null)
        {
            var safeForChannelTask = this._censorService.IsSafeForChannel(context.DiscordGuild,
                context.DiscordChannel, fullArtist.Name);
            var accentColorTask = this._artistsService.GetArtistAccentColorAsync(
                fullArtist.SpotifyImageUrl, fullArtist.Id, fullArtist.Name);

            if (await safeForChannelTask == CensorService.CensorResult.Safe)
            {
                showThumbnail = true;
            }

            response.ComponentsContainer.WithAccentColor(await accentColorTask);
        }

        var headerSection = new StringBuilder();
        headerSection.AppendLine(artistSearch.Artist.ArtistUrl != null
            ? $"## [{artistSearch.Artist.ArtistName}]({artistSearch.Artist.ArtistUrl})"
            : $"## {artistSearch.Artist.ArtistName}");
        headerSection.AppendLine($"Artist overview for **{userTitle}**");

        if (artistSearch.Artist.UserPlaycount > 0)
        {
            var playsLine =
                $"**{artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)}";

            if (recentPlaycountsTask != null)
            {
                var recentPlaycounts = await recentPlaycountsTask;
                if (recentPlaycounts.month > 0)
                {
                    playsLine += $" — **{recentPlaycounts.month.Format(context.NumberFormat)}** last month";
                }
            }

            headerSection.AppendLine(playsLine);

            if (firstPlayTask != null)
            {
                var firstPlay = await firstPlayTask;
                if (firstPlay != null)
                {
                    var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();
                    headerSection.AppendLine($"Discovered <t:{firstListenValue}:D>");
                }
            }
            else
            {
                var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
                if (randomHintNumber == 1 &&
                    this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType,
                        context.DiscordGuild?.Id))
                {
                    this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                    headerSection.AppendLine(
                        $"*[Supporters]({Constants.GetSupporterOverviewLink}) can see artist discovery dates.*");
                }
            }
        }
        else
        {
            headerSection.AppendLine("No plays on this artist yet");
        }

        if (showThumbnail)
        {
            response.ComponentsContainer.WithSection([
                new TextDisplayProperties(headerSection.ToString().TrimEnd())
            ], fullArtist.SpotifyImageUrl);
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(headerSection.ToString().TrimEnd()));
        }

        var artistTracksButton = false;
        var artistAlbumsButton = false;
        if (artistSearch.Artist.UserPlaycount > 0)
        {
            var topTracks =
                await this._artistsService.GetTopTracksForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);

            if (topTracks.Any())
            {
                var topTracksDescription = new StringBuilder();
                artistTracksButton = true;
                topTracksDescription.AppendLine($"**{determiner} top tracks**");

                var counter = 1;
                foreach (var track in topTracks.Take(8))
                {
                    topTracksDescription.AppendLine(
                        $"`{counter}`  **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.Name, 40))}** - " +
                        $"*{track.Playcount.Format(context.NumberFormat)}x*");
                    counter++;
                }

                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(topTracksDescription.ToString().TrimEnd()));
            }

            var topAlbums =
                await this._artistsService.GetUserAlbumsForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
            if (topAlbums.Any())
            {
                var topAlbumsDescription = new StringBuilder();
                artistAlbumsButton = true;
                topAlbumsDescription.AppendLine($"**{determiner} top albums**");

                var counter = 1;
                foreach (var album in topAlbums.Take(8))
                {
                    topAlbumsDescription.AppendLine(
                        $"`{counter}`  **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.Name, 40))}** - " +
                        $"*{album.Playcount.Format(context.NumberFormat)}x*");
                    counter++;
                }

                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(topAlbumsDescription.ToString().TrimEnd()));
            }
        }

        if (user.UserDiscogs != null && user.DiscogsReleases.Any())
        {
            var artistCollection = user.DiscogsReleases
                .Where(w => w.Release.Artist.StartsWith(artistSearch.Artist.ArtistName,
                                StringComparison.OrdinalIgnoreCase) ||
                            artistSearch.Artist.ArtistName.StartsWith(w.Release.Artist,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (artistCollection.Any())
            {
                var discogsText = new StringBuilder();
                foreach (var album in artistCollection.Take(4))
                {
                    discogsText.Append(StringService.UserDiscogsWithAlbumName(album));
                }

                if (artistCollection.Count > 4)
                {
                    discogsText.Append(
                        $"*Plus {artistCollection.Count - 4} more {StringExtensions.GetItemsString(artistCollection.Count - 4)} in your collection*");
                }

                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(discogsText.ToString().TrimEnd()));
            }
        }

        if (fullArtist.ArtistGenres != null && fullArtist.ArtistGenres.Any())
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                $"-# {GenreService.GenresToString(fullArtist.ArtistGenres.ToList())}"));
        }

        var actionRow = new ActionRowProperties()
            .WithButton("Artist",
                $"{InteractionConstants.Artist.Info}:{fullArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}",
                style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.Info))
            .WithButton("All tracks",
                $"{InteractionConstants.Artist.Tracks}:{fullArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary, disabled: !artistTracksButton, emote: EmojiProperties.Standard("\ud83c\udfb6"))
            .WithButton("All albums",
                $"{InteractionConstants.Artist.Albums}:{fullArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}:",
                style: ButtonStyle.Secondary, disabled: !artistAlbumsButton, emote: EmojiProperties.Standard("\ud83d\udcbd"));
        response.ComponentsContainer.WithActionRow(actionRow);

        return response;
    }

    public async Task<ResponseModel> ArtistTracksAsync(ContextModel context,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM,
            context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm, true, userSettings.UserId,
            redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            artistSearch.Response.ResponseType = ResponseType.ComponentsV2;
            artistSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            artistSearch.Response.ComponentsContainer.WithTextDisplay(artistSearch.Response.Embed.Description);
            return artistSearch.Response;
        }

        var dbArtist =
            await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, redirectsEnabled: redirectsEnabled);

        if (artistSearch.Artist.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistSearch.Artist.ArtistName,
                artistSearch.Artist.UserPlaycount.Value);
        }

        List<UserTrack> topTracks;
        switch (timeSettings.TimePeriod)
        {
            case TimePeriod.Weekly:
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 7,
                    artistSearch.Artist.ArtistName);
                break;
            case TimePeriod.Monthly:
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 31,
                    artistSearch.Artist.ArtistName);
                break;
            default:
                topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId,
                    artistSearch.Artist.ArtistName);
                break;
        }

        if (topTracks.Count == 0 &&
            timeSettings.TimePeriod == TimePeriod.AllTime &&
            artistSearch.Artist.UserPlaycount >= 15 &&
            !userSettings.DifferentUser)
        {
            var user = await this._userService.GetUserForIdAsync(userSettings.UserId);
            await this._indexService.ModularUpdate(user, UpdateType.Tracks);
            topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId,
                artistSearch.Artist.ArtistName);
        }

        var maybeMissingResults = !SupporterService.IsSupporter(userSettings.UserType) &&
                                  !userSettings.DifferentUser &&
                                  timeSettings.TimePeriod == TimePeriod.AllTime &&
                                  (await this._artistsService.GetUserTrackCount(userSettings.UserId)) >= 6000 &&
                                  topTracks.Sum(s => s.Playcount) < artistSearch.Artist.UserPlaycount;

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (topTracks.Count == 0)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()} has no registered tracks for the artist **{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}** in .fmbot.");

            var noResultsRow = new ActionRowProperties()
                .WithButton("Overview",
                    $"{InteractionConstants.Artist.Overview}:{dbArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}",
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"));
            response.ComponentsContainer.WithActionRow(noResultsRow);

            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var title = userSettings.DifferentUser
            ? $"### {userSettings.DisplayName}'s top tracks for '{artistSearch.Artist.ArtistName}'"
            : $"### Your top tracks for '{artistSearch.Artist.ArtistName}'";

        var footer = new StringBuilder();

        if (artistSearch.IsRandom)
        {
            footer.AppendLine(
                $"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
        }

        footer.Append($"{topTracks.Count.Format(context.NumberFormat)} different tracks");

        if (userSettings.DifferentUser)
        {
            footer.AppendLine();
            footer.Append(
                $"{userSettings.UserNameLastFm} has {artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)} total artist plays");
            footer.AppendLine();
            footer.Append($"Requested by {userTitle}");
        }
        else
        {
            footer.AppendLine();
            footer.Append(
                $"{userTitle} has {artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)} total artist plays");
        }

        if (maybeMissingResults)
        {
            footer.AppendLine();
            footer.Append("Some tracks outside of top 6000 might not be visible");
        }

        var footerText = footer.ToString().TrimEnd();

        var pageDescriptions = new List<string>();
        var topTrackPages = topTracks.ChunkBy(10);
        var counter = 1;

        foreach (var topTrackPage in topTrackPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in topTrackPage)
            {
                pageString.AppendLine(
                    $"{counter}. **{StringExtensions.Sanitize(track.Name)}** - *{track.Playcount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(track.Playcount)}*");
                counter++;
            }

            pageDescriptions.Add(pageString.ToString().TrimEnd());
        }

        var overviewId =
            $"{InteractionConstants.Artist.Overview}:{dbArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}";
        var footerLines = footerText.Replace("\n", "\n-# ");

        if (pageDescriptions.Count == 1)
        {
            response.ResponseType = ResponseType.ComponentsV2;

            response.ComponentsContainer.WithTextDisplay(title);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay(pageDescriptions[0]);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay($"-# {footerLines}");

            var actionRow = new ActionRowProperties()
                .WithButton("Overview", overviewId,
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"));
            response.ComponentsContainer.WithActionRow(actionRow);
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;

            var paginator = new ComponentPaginatorBuilder()
                .WithPageFactory(GeneratePage)
                .WithPageCount(Math.Max(1, pageDescriptions.Count))
                .WithActionOnTimeout(ActionOnStop.DisableInput);

            response.ComponentPaginator = paginator;

            IPage GeneratePage(IComponentPaginator p)
            {
                var container = new ComponentContainerProperties();

                container.WithTextDisplay(title);
                container.WithSeparator();

                var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
                if (currentPage != null)
                {
                    container.WithTextDisplay(currentPage);
                }

                container.WithSeparator();
                container.WithTextDisplay($"-# Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} — {footerLines}");

                if (pageDescriptions.Count > 1)
                {
                    var paginationRow = new ActionRowProperties()
                        .AddFirstButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesFirst))
                        .AddPreviousButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious))
                        .AddNextButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesNext))
                        .AddLastButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesLast))
                        .WithButton(null, overviewId, style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"));
                    container.WithActionRow(paginationRow);
                }

                return new PageBuilder()
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithMessageFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([container])
                    .Build();
            }
        }

        return response;
    }

    public async Task<ResponseModel> ArtistAlbumsAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm,
            true, userSettings.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            artistSearch.Response.ResponseType = ResponseType.ComponentsV2;
            artistSearch.Response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            artistSearch.Response.ComponentsContainer.WithTextDisplay(artistSearch.Response.Embed.Description);
            return artistSearch.Response;
        }

        var dbArtist =
            await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, redirectsEnabled: redirectsEnabled);

        if (artistSearch.Artist.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistSearch.Artist.ArtistName,
                artistSearch.Artist.UserPlaycount.Value);
        }

        var topAlbums =
            await this._artistsService.GetUserAlbumsForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (topAlbums.Count == 0 &&
            artistSearch.Artist.UserPlaycount >= 15 &&
            !userSettings.DifferentUser)
        {
            var user = await this._userService.GetUserForIdAsync(userSettings.UserId);
            await this._indexService.ModularUpdate(user, UpdateType.Albums);
            topAlbums = await this._artistsService.GetUserAlbumsForArtist(userSettings.UserId,
                artistSearch.Artist.ArtistName);
        }

        if (topAlbums.Count == 0)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(
                $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()} has no scrobbles for this artist or their scrobbles have no album associated with them.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var maybeMissingResults = !SupporterService.IsSupporter(userSettings.UserType) &&
                                  !userSettings.DifferentUser &&
                                  (await this._artistsService.GetUserAlbumCount(userSettings.UserId)) >= 5000 &&
                                  topAlbums.Sum(s => s.Playcount) < artistSearch.Artist.UserPlaycount;

        var title = userSettings.DifferentUser
            ? $"### {userSettings.DisplayName}'s top albums for '{artistSearch.Artist.ArtistName}'"
            : $"### Your top albums for '{artistSearch.Artist.ArtistName}'";

        var footer = new StringBuilder();

        if (maybeMissingResults)
        {
            footer.AppendLine("Some albums outside of top 5000 might not be visible");
        }

        if (userSettings.DifferentUser)
        {
            footer.Append(
                $"{userSettings.UserNameLastFm} has {artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)} total artist plays");
            footer.AppendLine();
            footer.Append($"Requested by {userTitle}");
        }
        else
        {
            footer.Append(
                $"{userTitle} has {artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)} total artist plays");
        }

        var footerText = footer.ToString().TrimEnd();

        var pageDescriptions = new List<string>();
        var albumPages = topAlbums.ChunkBy(10);
        var counter = 1;

        foreach (var albumPage in albumPages)
        {
            var pageString = new StringBuilder();
            foreach (var artistAlbum in albumPage)
            {
                pageString.AppendLine(
                    $"{counter}. **{artistAlbum.Name}** - *{artistAlbum.Playcount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artistAlbum.Playcount)}*");
                counter++;
            }

            pageDescriptions.Add(pageString.ToString().TrimEnd());
        }

        var overviewId =
            $"{InteractionConstants.Artist.Overview}:{dbArtist.Id}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}";
        var footerLines = footerText.Replace("\n", "\n-# ");

        if (pageDescriptions.Count == 1)
        {
            response.ResponseType = ResponseType.ComponentsV2;

            response.ComponentsContainer.WithTextDisplay(title);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay(pageDescriptions[0]);
            response.ComponentsContainer.WithSeparator();
            response.ComponentsContainer.WithTextDisplay($"-# {footerLines}");

            var actionRow = new ActionRowProperties()
                .WithButton("Overview", overviewId,
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"));
            response.ComponentsContainer.WithActionRow(actionRow);
        }
        else
        {
            response.ResponseType = ResponseType.Paginator;

            var paginator = new ComponentPaginatorBuilder()
                .WithPageFactory(GeneratePage)
                .WithPageCount(Math.Max(1, pageDescriptions.Count))
                .WithActionOnTimeout(ActionOnStop.DisableInput);

            response.ComponentPaginator = paginator;

            IPage GeneratePage(IComponentPaginator p)
            {
                var container = new ComponentContainerProperties();

                container.WithTextDisplay(title);
                container.WithSeparator();

                var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
                if (currentPage != null)
                {
                    container.WithTextDisplay(currentPage);
                }

                container.WithSeparator();
                container.WithTextDisplay($"-# Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} — {footerLines}");

                if (pageDescriptions.Count > 1)
                {
                    var paginationRow = new ActionRowProperties()
                        .AddFirstButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesFirst))
                        .AddPreviousButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious))
                        .AddNextButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesNext))
                        .AddLastButton(p, style: ButtonStyle.Secondary, emote: EmojiProperties.Custom(DiscordConstants.PagesLast))
                        .WithButton(null, overviewId, style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("\ud83d\udcca"));
                    container.WithActionRow(paginationRow);
                }

                return new PageBuilder()
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithMessageFlags(MessageFlags.IsComponentsV2)
                    .WithComponents([container])
                    .Build();
            }
        }

        return response;
    }

    public async Task<ResponseModel> GuildArtistsAsync(
        ContextModel context,
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
            topGuildArtists =
                await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuild(guild.GuildId,
                    guildListSettings.OrderType);
        }
        else
        {
            topGuildArtists = await this._playService.GetGuildTopArtistsPlays(guild.GuildId,
                guildListSettings.StartDateTime, guildListSettings.OrderType, guildListSettings.EndDateTime);
            previousTopGuildArtists = (await this._playService.GetGuildTopArtistsPlays(guild.GuildId,
                guildListSettings.BillboardStartDateTime, guildListSettings.OrderType, guildListSettings.BillboardEndDateTime)).ToList();
        }

        var title = $"Top {guildListSettings.TimeDescription.ToLower()} artists in {context.DiscordGuild.Name}";

        var footerLabel = guildListSettings.OrderType == OrderType.Listeners
            ? "Listener count"
            : "Play count";

        string footerHint = new Random().Next(0, 5) switch
        {
            1 => $"View specific artist listeners with '{context.Prefix}whoknows'",
            2 => "Available time periods: alltime, monthly, weekly, current and last month",
            3 => "Available sorting options: plays and listeners",
            _ => null
        };

        var artistPages = topGuildArtists.Chunk(12).ToList();

        var counter = 1;
        var pageDescriptions = new List<string>();
        foreach (var page in artistPages)
        {
            var pageString = new StringBuilder();
            foreach (var track in page)
            {
                var name = guildListSettings.OrderType == OrderType.Listeners
                    ? $"`{track.ListenerCount.Format(context.NumberFormat)}` · **{track.ArtistName}** · *{track.TotalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(track.TotalPlaycount)}*"
                    : $"`{track.TotalPlaycount.Format(context.NumberFormat)}` · **{track.ArtistName}** · *{track.ListenerCount.Format(context.NumberFormat)} {StringExtensions.GetListenersString(track.ListenerCount)}*";

                if (previousTopGuildArtists != null && previousTopGuildArtists.Any())
                {
                    var previousTopArtist =
                        previousTopGuildArtists.FirstOrDefault(f => f.ArtistName == track.ArtistName);
                    int? previousPosition = previousTopArtist == null
                        ? null
                        : previousTopGuildArtists.IndexOf(previousTopArtist);

                    pageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition, false)
                        .Text);
                }
                else
                {
                    pageString.AppendLine(name);
                }

                counter++;
            }

            pageDescriptions.Add(pageString.ToString());
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;
        response.ResponseType = ResponseType.Paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
            }

            container.WithSeparator();

            var pageFooter = $"-# {footerLabel} - Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count}";
            if (footerHint != null)
            {
                pageFooter += $"\n-# {footerHint}";
            }

            container.WithTextDisplay(pageFooter);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    public async Task<ResponseModel> TopArtistsAsync(
        ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings,
        ResponseMode mode)
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
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/artists?{timeSettings.UrlParameter}");

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artists for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
            timeSettings, topListSettings.ListAmount, 1, true);

        if (!artists.Success || artists.Content == null)
        {
            response.Embed.ErrorResponse(artists.Error, artists.Message, "top artists", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top artists in the [selected time period]({userUrl}).");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (timeSettings.TimePeriod == TimePeriod.AllTime && !userSettings.DifferentUser)
        {
            _ = Task.Run(() =>
                this._smallIndexRepository.UpdateUserArtists(context.ContextUser, artists.Content.TopArtists));
        }

        if (mode == ResponseMode.Image)
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            artists.Content.TopArtists = await this._artistsService.FillArtistImages(artists.Content.TopArtists);

            var firstArtistImage =
                artists.Content.TopArtists.FirstOrDefault(f => f.ArtistImageUrl != null)?.ArtistImageUrl;

            using var image = await this._puppeteerService.GetTopList(userTitle, "Top Artists", "artists",
                timeSettings.Description,
                artists.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), firstArtistImage,
                artists.TopList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-artists-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var previousTopArtists = new List<TopArtist>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._dataSourceFactory
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var artistPages = artists.Content.TopArtists.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var artistPage in artistPages)
        {
            var artistPageString = new StringBuilder();
            foreach (var artist in artistPage)
            {
                var name =
                    $"**[{artist.ArtistName}]({artist.ArtistUrl})** - *{artist.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artist.UserPlaycount)}*";

                if (topListSettings.Billboard && previousTopArtists.Any())
                {
                    var previousTopArtist = previousTopArtists.FirstOrDefault(f => f.ArtistName == artist.ArtistName);
                    int? previousPosition =
                        previousTopArtist == null ? null : previousTopArtists.IndexOf(previousTopArtist);

                    artistPageString.AppendLine(
                        StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                }
                else
                {
                    artistPageString.Append($"{counter}. ");
                    artistPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();

            ImportService.AddImportDescription(footer, artists.PlaySources);

            footer.Append($"Page {pageCounter}/{artistPages.Count}");

            if (artists.Content.TotalAmount.HasValue)
            {
                footer.Append(
                    $" - {artists.Content.TotalAmount.Format(context.NumberFormat)} different artists");
            }

            if (topListSettings.Billboard)
            {
                footer.AppendLine();
                footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine();
                footer.Append("View as billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(artistPageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public static ResponseModel DiscoverySupporterRequired(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.UserType == UserType.User)
        {
            response.Embed.WithDescription(
                $"To see what music you've recently discovered, we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters.");

            response.Components = new ActionRowProperties()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "discoveries"));
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        if (userSettings.UserType == UserType.User)
        {
            response.Embed.WithDescription(
                $"Sorry, the discovery command uses somebody's lifetime listening history. You can only use this command on other supporters.");

            response.Components = new ActionRowProperties()
                .WithButton(".fmbot supporter", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "discoveries"));
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        return null;
    }

    public async Task<ResponseModel> ArtistDiscoveriesAsync(
        ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings,
        ResponseMode mode)
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

        var userUrl = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/artists?{timeSettings.UrlParameter}");

        response.EmbedAuthor.WithName($"Discovered artists in {timeSettings.AltDescription.ToLower()} for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        var knownArtists = allPlays
            .Where(w => w.TimePlayed < timeSettings.StartDateTime)
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.Key)
            .ToHashSet();

        var allArtists = allPlays
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(d => d.Key, d => d.Count(), StringComparer.OrdinalIgnoreCase);

        var topNewArtists = allPlays
            .Where(w => w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime)
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new TopArtist
            {
                ArtistName = s.Key,
                UserPlaycount = allArtists[s.Key],
                FirstPlay = s.OrderBy(o => o.TimePlayed).First().TimePlayed,
                ArtistUrl = LastfmUrlExtensions.GetArtistUrl(s.Key)
            })
            .Where(w => !knownArtists.Any(a => a.Equals(w.ArtistName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(o => o.UserPlaycount)
            .ToList();

        if (mode == ResponseMode.Image && topNewArtists.Any())
        {
            var topList = topNewArtists.Select(s => new TopListObject
            {
                Name = s.ArtistName,
                SubName = $"{s.FirstPlay.Value:MMM d yyyy}",
                Playcount = s.UserPlaycount
            }).ToList();

            var totalPlays = allPlays.Count(w =>
                w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime);
            var backgroundImage = (await this._artistsService.GetArtistFromDatabase(topNewArtists.First().ArtistName))
                ?.SpotifyImageUrl;

            using var image = await this._puppeteerService.GetTopList(userTitle, "Newly discovered artists", "new artists",
                timeSettings.Description,
                topNewArtists.Count, totalPlays, backgroundImage, topList, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-tracks-{userSettings.DiscordUserId}.png";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var artistPages = topNewArtists.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;

        foreach (var artistPage in artistPages)
        {
            var artistPageString = new StringBuilder();
            for (var index = 0; index < artistPage.Count; index++)
            {
                var newArtist = artistPage.ToList()[index];

                artistPageString.Append($"{counter}. ");
                artistPageString.AppendLine(
                    $"**[{StringExtensions.TruncateLongString(newArtist.ArtistName, 28)}]({LastfmUrlExtensions.GetArtistUrl(newArtist.ArtistName)})** " +
                    $"— *{newArtist.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(newArtist.UserPlaycount)}* " +
                    $"— on **<t:{newArtist.FirstPlay.Value.ToUnixEpochDate()}:D>**");

                counter++;
            }

            var footer = new StringBuilder();

            footer.Append($"Page {pageCounter}/{artistPages.Count}");

            footer.Append($" - {topNewArtists.Count.Format(context.NumberFormat)} newly discovered artists");

            pages.Add(new PageBuilder()
                .WithDescription(artistPageString.ToString())
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        if (artistPages.Count == 0)
        {
            pages.Add(new PageBuilder()
                .WithDescription($"No discovered artists in {timeSettings.Description}.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> ArtistPlaysAsync(ContextModel context,
        UserSettingsModel userSettings,
        string artistName,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Text,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistName,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has " +
            $"**{artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)}** {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)} for " +
            $"**{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}**";

        if (userSettings.DifferentUser)
        {
            await this._updateService.UpdateUser(new UpdateUserQueueItem(userSettings.UserId));
        }

        var recentArtistPlaycounts =
            await this._playService.GetRecentArtistPlaycounts(userSettings.UserId, artistSearch.Artist.ArtistName);
        if (recentArtistPlaycounts.month != 0)
        {
            reply +=
                $"\n-# *{recentArtistPlaycounts.week.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentArtistPlaycounts.week)} last week — " +
                $"{recentArtistPlaycounts.month.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(recentArtistPlaycounts.month)} last month*";
        }

        response.Text = reply;

        return response;
    }

    public async Task<ResponseModel> ArtistPaceAsync(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        string amount,
        string artistName, bool redirectsEnabled)
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

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistName,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        goalAmount = SettingService.GetGoalAmount(amount, artistSearch.Artist.UserPlaycount.GetValueOrDefault(0));

        var regularPlayCount = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
            timeSettings.TimeFrom, userSettings.SessionKeyLastFm);

        if (regularPlayCount is null or 0)
        {
            response.Text =
                $"<@{context.DiscordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var artistPlayCount =
            await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId,
                artistSearch.Artist.ArtistName,
                timeSettings.PlayDays.GetValueOrDefault(30));

        if (artistPlayCount is 0)
        {
            response.Text =
                $"<@{context.DiscordUser.Id}> No plays found on **{artistSearch.Artist.ArtistName}** in the last {timeSettings.PlayDays} days.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var age = DateTimeOffset.FromUnixTimeSeconds(timeSettings.TimeFrom.GetValueOrDefault());
        var totalDays = (DateTime.UtcNow - age).TotalDays;

        var playsLeft = goalAmount - artistSearch.Artist.UserPlaycount.GetValueOrDefault(0);

        var avgPerDay = artistPlayCount / totalDays;
        var daysToAdd = playsLeft / avgPerDay;

        var limitDate = new DateTime(9999, 12, 31);

        var timeLeft = limitDate - DateTime.Today;
        var daysUntilLimit = timeLeft.Days;

        var reply = new StringBuilder();

        var determiner = "your";
        if (userSettings.DifferentUser)
        {
            reply.Append(
                $"<@{context.DiscordUser.Id}> My estimate is that the user '{StringExtensions.Sanitize(userSettings.UserNameLastFm)}'");
            determiner = "their";
        }
        else
        {
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that you");
        }

        string goalDateString;
        if (daysUntilLimit <= daysToAdd)
        {
            goalDateString = $"on a date beyond the year 9999! 🚀";
        }
        else
        {
            var goalDate = DateTime.UtcNow.AddDays(daysToAdd);
            goalDateString = $"on **<t:{goalDate.ToUnixEpochDate()}:D>**.";
        }

        reply.AppendLine(
            $" will reach **{goalAmount.Format(context.NumberFormat)}** plays on **{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}** {goalDateString}");

        reply.AppendLine(
            $"-# *Based on {determiner} average of {Math.Round(avgPerDay, 2).Format(context.NumberFormat)} plays per day in the last {Math.Round(totalDays, 0).Format(context.NumberFormat)} days — " +
            $"{artistPlayCount.Format(context.NumberFormat)} plays in this time period — {artistSearch.Artist.UserPlaycount.Format(context.NumberFormat)} alltime*");

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> WhoKnowsArtistAsync(ContextModel context,
        WhoKnowsResponseMode mode,
        string artistValues,
        bool displayRoleSelector = false,
        List<ulong> roles = null,
        bool redirectsEnabled = true,
        bool showCrownButton = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedArtists: true,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist,
            artistSearch.Artist.ArtistName, redirectsEnabled);

        var safeForChannel =
            await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var contextGuild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithArtist = await this._whoKnowsArtistService.GetIndexedUsersForArtist(context.DiscordGuild,
            guildUsers, contextGuild.GuildId, artistSearch.Artist.ArtistName);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId);
        await this._indexService.GetOrAddUserToGuild(guildUsers, contextGuild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, context.ContextUser.UserId,
            contextGuild);

        usersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, context.ContextUser,
            artistSearch.Artist.ArtistName, context.DiscordGuild, artistSearch.Artist.UserPlaycount);

        var (filterStats, filteredUsersWithArtist) =
            WhoKnowsService.FilterWhoKnowsObjects(usersWithArtist, guildUsers, contextGuild, context.ContextUser.UserId,
                roles);

        CrownModel crownModel = null;
        if (contextGuild.CrownsDisabled != true && filteredUsersWithArtist.Count >= 1 && !displayRoleSelector &&
            redirectsEnabled)
        {
            crownModel =
                await this._crownService.GetAndUpdateCrownForArtist(filteredUsersWithArtist, guildUsers,
                    contextGuild, artistSearch.Artist.ArtistName);
            if (crownModel?.Stolen == true)
            {
                showCrownButton = true;
            }
        }

        if (showCrownButton)
        {
            var stolen = crownModel?.Stolen == true;
            response.Components = new ActionRowProperties()
                .WithButton("Crown history", $"{InteractionConstants.Artist.Crown}:{cachedArtist.Id}:{stolen}",
                    style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("👑"));
        }

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"in <b>{context.DiscordGuild.Name}</b>",
                imgUrl, artistSearch.Artist.ArtistName,
                filteredUsersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                crownModel?.Crown,
                crownModel?.CrownHtmlResult);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"whoknows-{artistSearch.Artist.ArtistName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            if (safeForChannel == CensorService.CensorResult.Nsfw)
            {
                response.Spoiler = true;
                response.Embed.WithTitle("⚠️ NSFW - Click to reveal");
                response.ResponseType = ResponseType.ImageWithEmbed;
            }
            else
            {
                response.Embed = null;
            }

            return response;
        }

        var title = $"{artistSearch.Artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();
        if (artistSearch.IsRandom)
        {
            footer.AppendLine(
                $"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
        }

        if (cachedArtist?.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        var rnd = new Random();
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.DiscordGuild);
        if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
        {
            footer.AppendLine($"Missing members? Update with {context.Prefix}refreshmembers");
        }

        if (filterStats.FullDescription != null)
        {
            footer.AppendLine(filterStats.FullDescription);
        }

        if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
        {
            var serverListeners = filteredUsersWithArtist.DistinctBy(d => d.UserId).Count(c => c.Playcount > 0);
            var serverPlaycount = filteredUsersWithArtist.DistinctBy(d => d.UserId).Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithArtist.DistinctBy(d => d.UserId).Average(a => a.Playcount);

            footer.Append("Artist - ");
            footer.Append(
                $"{serverListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append(
                $"{serverPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.AppendLine($"{((int)avgServerPlaycount).Format(context.NumberFormat)} avg");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(context.ContextUser.UserId,
            guildUsers, contextGuild, artistSearch.Artist.ArtistName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithArtist,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString(), crownModel);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, context.ContextUser.UserId,
            PrivacyLevel.Server, context.NumberFormat, crownModel);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (artistSearch.Artist.ArtistUrl != null &&
            Uri.IsWellFormedUriString(artistSearch.Artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.Artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnail(imgUrl);

            var accentColor = await this._artistsService.GetArtistAccentColorAsync(
                imgUrl, cachedArtist.Id, cachedArtist.Name);
            response.Embed.WithColor(accentColor);
        }

        if (displayRoleSelector)
        {
            if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
            {
                var allowedRoles =
                    new RoleMenuProperties($"{InteractionConstants.WhoKnowsRolePicker}:{cachedArtist.Id}")
                        .WithPlaceholder("Apply role filter..")
                        .WithMinValues(0)
                        .WithMaxValues(25);

                response.RoleMenu = allowedRoles;
            }
            else
            {
                //response.Components = new ActionRowProperties().WithButton(Constants.GetPremiumServer, disabled: true, customId: "1");
            }
        }

        return response;
    }

    public StringMenuProperties GetFilterSelectMenu(string customId, Guild guild,
        IDictionary<int, FullGuildUser> guildUsers)
    {
        var builder = new StringMenuProperties(customId);

        builder
            .WithPlaceholder("Set filter options")
            .WithMinValues(0);

        if (guildUsers.Any(a => a.Value.BlockedFromWhoKnows))
        {
            builder.AddOptions(new StringMenuSelectOptionProperties("Blocked users",
                "Filter out users you've manually blocked").WithValue("blocked-users").WithDefault(true));
        }

        return builder;
    }

    public async Task<ResponseModel> GlobalWhoKnowsArtistAsync(
        ContextModel context,
        WhoKnowsSettings settings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser,
            settings.NewSearchValue, context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            useCachedArtists: true, userId: context.ContextUser.UserId, redirectsEnabled: settings.RedirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist,
            artistSearch.Artist.ArtistName, settings.RedirectsEnabled);

        var safeForChannel =
            await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var usersWithArtist =
            await this._whoKnowsArtistService.GetGlobalUsersForArtists(context.DiscordGuild,
                artistSearch.Artist.ArtistName);

        var filteredUsersWithArtist =
            await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist, settings.QualityFilterDisabled);

        filteredUsersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(filteredUsersWithArtist,
            context.ContextUser, artistSearch.Artist.ArtistName, context.DiscordGuild,
            artistSearch.Artist.UserPlaycount);

        var privacyLevel = PrivacyLevel.Global;

        if (context.DiscordGuild != null)
        {
            var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

            filteredUsersWithArtist =
                WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithArtist, guildUsers);

            if (settings.AdminView)
            {
                privacyLevel = PrivacyLevel.Server;
            }
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"in <b>.fmbot 🌐</b>", imgUrl,
                artistSearch.Artist.ArtistName,
                filteredUsersWithArtist, context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"global-whoknows-{artistSearch.Artist.ArtistName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            if (safeForChannel == CensorService.CensorResult.Nsfw)
            {
                response.Spoiler = true;
                response.Embed.WithTitle("⚠️ NSFW - Click to reveal");
                response.ResponseType = ResponseType.ImageWithEmbed;
            }

            return response;
        }

        var title =
            $"{artistSearch.Artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} globally";

        var footer = new StringBuilder();
        if (artistSearch.IsRandom)
        {
            footer.AppendLine(
                $"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
        }

        if (cachedArtist?.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        footer = WhoKnowsService.GetGlobalWhoKnowsFooter(footer, settings, context);

        if (filteredUsersWithArtist.Any() && filteredUsersWithArtist.Count > 1)
        {
            var globalListeners = filteredUsersWithArtist.Count;
            var globalPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
            var avgPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

            footer.Append($"Global artist - ");
            footer.Append(
                $"{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append(
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{((int)avgPlaycount).Format(context.NumberFormat)} avg");
        }

        if (settings.ResponseMode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(filteredUsersWithArtist,
                context.ContextUser.UserId, privacyLevel, context.NumberFormat,
                title, footer.ToString(), hidePrivateUsers: settings.HidePrivateUsers);

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, context.ContextUser.UserId,
            privacyLevel, context.NumberFormat, hidePrivateUsers: settings.HidePrivateUsers);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody that uses .fmbot has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (Uri.IsWellFormedUriString(artistSearch.Artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.Artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnail(imgUrl);

            var accentColor = await this._artistsService.GetArtistAccentColorAsync(
                imgUrl, cachedArtist.Id, cachedArtist.Name);
            response.Embed.WithColor(accentColor);
        }

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowArtistAsync(ContextModel context,
        WhoKnowsResponseMode mode,
        string artistValues, bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        if (context.ContextUser.Friends?.Any() != true)
        {
            response.Embed.WithDescription("We couldn't find any friends. To add friends:\n" +
                                           $"`{context.Prefix}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`\n\n" +
                                           $"Or right-click a user, go to apps and click 'Add as friend'");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedArtists: true,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist =
            await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName,
                redirectsEnabled);

        var safeForChannel =
            await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        var usersWithArtist = await this._whoKnowsArtistService.GetFriendUsersForArtists(context.DiscordGuild,
            guildUsers, guild?.GuildId ?? 0, context.ContextUser.UserId, artistSearch.Artist.ArtistName);

        usersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, context.ContextUser,
            artistSearch.Artist.ArtistName, context.DiscordGuild, artistSearch.Artist.UserPlaycount);
        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (mode == WhoKnowsResponseMode.Image)
        {
            using var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"from <b>{userTitle}</b>'s friends",
                imgUrl, artistSearch.Artist.ArtistName,
                usersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"friends-whoknow-{artistSearch.Artist.ArtistName}.png";
            response.ResponseType = ResponseType.ImageOnly;

            if (safeForChannel == CensorService.CensorResult.Nsfw)
            {
                response.Spoiler = true;
                response.Embed.WithTitle("⚠️ NSFW - Click to reveal");
                response.ResponseType = ResponseType.ImageWithEmbed;
            }

            return response;
        }

        var title = $"{cachedArtist.Name} with friends";

        var footer = new StringBuilder();

        if (cachedArtist.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer.AppendLine(
                $"{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible");
        }

        if (usersWithArtist.Any() && usersWithArtist.Count > 1)
        {
            var globalListeners = usersWithArtist.Count;
            var globalPlaycount = usersWithArtist.Sum(a => a.Playcount);
            var avgPlaycount = usersWithArtist.Average(a => a.Playcount);

            footer.Append(
                $"{globalListeners.Format(context.NumberFormat)} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append(
                $"{globalPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{((int)avgPlaycount).Format(context.NumberFormat)} avg");
        }

        footer.AppendLine($"Friends WhoKnow artist for {userTitle}");

        if (mode == WhoKnowsResponseMode.Pagination)
        {
            var paginator = WhoKnowsService.CreateWhoKnowsPaginator(usersWithArtist,
                context.ContextUser.UserId, PrivacyLevel.Server, context.NumberFormat,
                title, footer.ToString());

            response.ResponseType = ResponseType.Paginator;
            response.ComponentPaginator = paginator;
            return response;
        }

        var serverUsers =
            WhoKnowsService.WhoKnowsListToString(usersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server,
                context.NumberFormat);
        if (usersWithArtist.Count == 0)
        {
            serverUsers = "None of your friends has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        response.Embed.WithTitle(title);

        if (Uri.IsWellFormedUriString(cachedArtist.LastFmUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(cachedArtist.LastFmUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnail(imgUrl);

            var accentColor = await this._artistsService.GetArtistAccentColorAsync(
                imgUrl, cachedArtist.Id, cachedArtist.Name);
            response.Embed.WithColor(accentColor);
        }

        return response;
    }

    public async Task<ResponseModel> TasteAsync(
        ContextModel context,
        EmbedSize embedSize,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var ownLastFmUsername = context.ContextUser.UserNameLastFM;
        string lastfmToCompare = null;

        if (userSettings.DifferentUser)
        {
            lastfmToCompare = userSettings.UserNameLastFm;
        }

        if (lastfmToCompare == null)
        {
            if (userSettings.DifferentUser)
            {
                response.ComponentsContainer.AddComponent(
                    new TextDisplayProperties("That user doesn't use .fmbot."));
                response.CommandResponse = CommandResponse.NotFound;
            }
            else
            {
                response.ComponentsContainer.AddComponent(
                    new TextDisplayProperties(
                        $"Please mention someone, enter a Last.fm username or reply to someone to compare your taste with.\n" +
                        $"-# Example: `{context.Prefix}taste @user` or `{context.Prefix}taste lastfmname`"));
                response.CommandResponse = CommandResponse.WrongInput;
            }

            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return response;
        }

        if (lastfmToCompare.ToLower() == ownLastFmUsername)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayProperties(
                    $"You can't compare taste with yourself. Use `{context.Prefix}topartists` to view your own top artists."));
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var ownArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(ownLastFmUsername, timeSettings, 1000);
        var otherArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(lastfmToCompare, timeSettings, 1000);

        var ownArtists = await ownArtistsTask;
        var otherArtists = await otherArtistsTask;

        if (!ownArtists.Success || ownArtists.Content == null)
        {
            response.Embed.ErrorResponse(ownArtists.Error, ownArtists.Message, "taste", context.DiscordUser,
                "artist list");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (!otherArtists.Success || otherArtists.Content == null)
        {
            response.Embed.ErrorResponse(otherArtists.Error, otherArtists.Message, "taste", context.DiscordUser,
                "artist list");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (ownArtists.Content.TopArtists == null || ownArtists.Content.TopArtists.Count == 0 ||
            otherArtists.Content.TopArtists == null || otherArtists.Content.TopArtists.Count == 0)
        {
            response.Text = "Sorry, you or the other user don't have any artist plays in the selected time period.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var amount = embedSize switch
        {
            EmbedSize.Default => 14,
            EmbedSize.Small => 7,
            EmbedSize.Large => 28,
            _ => 14
        };

        var url = LastfmUrlExtensions.GetUserUrl(lastfmToCompare, $"/library/artists?{timeSettings.UrlParameter}");

        var ownName = context.DiscordUser.GetDisplayName();
        var otherName = userSettings.DisplayName;

        if (context.DiscordGuild?.Users.TryGetValue(context.ContextUser.DiscordUserId, out var discordGuildUser) == true)
        {
            ownName = discordGuildUser.GetDisplayName();
        }

        var ownTopArtists =
            ownArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList();
        var otherTopArtists =
            otherArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList();

        var ownWithDiscogs = await this._userService.GetUserWithDiscogs(context.ContextUser.DiscordUserId);
        var otherWithDiscogs = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

        var rawData = new TasteRawData
        {
            OwnUsername = ownLastFmUsername,
            OtherUsername = lastfmToCompare,
            OwnName = ownName,
            OtherName = otherName,
            Url = url,
            TimeDescription = timeSettings.Description,
            OwnTopArtists = ownTopArtists,
            OtherTopArtists = otherTopArtists,
        };

        await EnrichTasteRawDataAsync(rawData, ownArtists.Content.TopArtists, otherArtists.Content.TopArtists,
            ownWithDiscogs, otherWithDiscogs);

        var cacheModel = new TasteCacheModel
        {
            AccentColor = await UserService.GetAccentColor(context.ContextUser, context.DiscordGuild),
            Amount = amount,
            RawData = rawData,
            Pages = BuildTastePages(this._artistsService, rawData, amount)
        };

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            _ = Task.Run(() =>
                this._smallIndexRepository.UpdateUserArtists(context.ContextUser, ownArtists.Content.TopArtists));
        }

        var cacheKey = Guid.NewGuid().ToString("N")[..8];
        this._cache.Set($"taste-{cacheKey}", cacheModel, TimeSpan.FromMinutes(10));

        BuildTastePage(response, cacheModel, 0, cacheKey,
            context.ContextUser.DiscordUserId, userSettings.DiscordUserId,
            timeSettings.Description, amount);
        return response;
    }

    private async Task EnrichTasteRawDataAsync(
        TasteRawData rawData,
        List<TopArtist> ownTopArtistsFull,
        List<TopArtist> otherTopArtistsFull,
        Persistence.Domain.Models.User ownUserWithDiscogs,
        Persistence.Domain.Models.User otherUserWithDiscogs)
    {
        var ownGenresTask = this._genreService.GetTopGenresForTopArtists(ownTopArtistsFull);
        var otherGenresTask = this._genreService.GetTopGenresForTopArtists(otherTopArtistsFull);
        var ownCountriesTask = this._countryService.GetTopCountriesForTopArtists(ownTopArtistsFull, true);
        var otherCountriesTask = this._countryService.GetTopCountriesForTopArtists(otherTopArtistsFull, true);

        await Task.WhenAll(ownGenresTask, otherGenresTask, ownCountriesTask, otherCountriesTask);

        var ownTopGenres = await ownGenresTask;
        var otherTopGenres = await otherGenresTask;
        if (ownTopGenres.Any() && otherTopGenres.Any())
        {
            rawData.OwnTopGenres = ownTopGenres.Select(s => new TasteItem(s.GenreName, s.UserPlaycount.Value)).ToList();
            rawData.OtherTopGenres = otherTopGenres.Select(s => new TasteItem(s.GenreName, s.UserPlaycount.Value)).ToList();
        }

        var ownTopCountries = await ownCountriesTask;
        var otherTopCountries = await otherCountriesTask;
        if (ownTopCountries.Any() && otherTopCountries.Any())
        {
            rawData.OwnTopCountries = ownTopCountries.Select(s => new TasteItem(s.CountryName, s.UserPlaycount.Value)).ToList();
            rawData.OtherTopCountries = otherTopCountries.Select(s => new TasteItem(s.CountryName, s.UserPlaycount.Value)).ToList();
        }

        if (ownUserWithDiscogs?.UserDiscogs != null && otherUserWithDiscogs?.UserDiscogs != null)
        {
            var ownReleases = await this._discogsService.GetUserCollection(ownUserWithDiscogs.UserId);
            var otherReleases = await this._discogsService.GetUserCollection(otherUserWithDiscogs.UserId);

            rawData.OwnDiscogsArtists = ownReleases.GroupBy(s => s.Release.Artist)
                .Select(g => new TasteItem(g.Key, g.Count())).ToList();
            rawData.OtherDiscogsArtists = otherReleases.GroupBy(s => s.Release.Artist)
                .Select(g => new TasteItem(g.Key, g.Count())).ToList();
            rawData.DiscogsOwnUsername = ownUserWithDiscogs.UserDiscogs.Username;
            rawData.DiscogsOtherUsername = otherUserWithDiscogs.UserDiscogs.Username;
            rawData.DiscogsUrl = $"{Constants.DiscogsUserUrl}{rawData.DiscogsOtherUsername}/collection";
        }
    }

    private static List<TastePageData> BuildTastePages(ArtistsService artistsService, TasteRawData raw, int amount)
    {
        var pages = new List<TastePageData>();
        var sanitizedOwnName = StringExtensions.Sanitize(raw.OwnName);
        var sanitizedOtherName = StringExtensions.Sanitize(raw.OtherName);

        var artistTaste = artistsService.GetTableTaste(raw.OwnTopArtists, raw.OtherTopArtists, amount,
            raw.TimeDescription, raw.OwnUsername, raw.OtherUsername, "Artist");
        pages.Add(new TastePageData
        {
            Label = "Artists",
            Title = $"Top artist comparison — {sanitizedOwnName} vs {sanitizedOtherName}",
            Content = artistTaste.result,
            Url = raw.Url
        });

        if (raw.OwnTopGenres != null && raw.OtherTopGenres != null)
        {
            var genreTaste = artistsService.GetTableTaste(raw.OwnTopGenres, raw.OtherTopGenres, amount,
                raw.TimeDescription, raw.OwnUsername, raw.OtherUsername, "Genre");
            pages.Add(new TastePageData
            {
                Label = "Genres",
                Title = $"Top genre comparison — {sanitizedOwnName} vs {sanitizedOtherName}",
                Content = genreTaste.result,
                Url = raw.Url
            });
        }

        if (raw.OwnTopCountries != null && raw.OtherTopCountries != null)
        {
            var countryTaste = artistsService.GetTableTaste(raw.OwnTopCountries, raw.OtherTopCountries, amount,
                raw.TimeDescription, raw.OwnUsername, raw.OtherUsername, "Country");
            pages.Add(new TastePageData
            {
                Label = "Countries",
                Title = $"Top country comparison — {sanitizedOwnName} vs {sanitizedOtherName}",
                Content = countryTaste.result,
                Url = raw.Url
            });
        }

        if (raw.OwnDiscogsArtists != null && raw.OtherDiscogsArtists != null)
        {
            var discogsTaste = artistsService.GetTableTaste(raw.OwnDiscogsArtists, raw.OtherDiscogsArtists, amount,
                raw.TimeDescription, raw.DiscogsOwnUsername, raw.DiscogsOtherUsername, "Artist");
            pages.Add(new TastePageData
            {
                Label = "Discogs",
                Title = $"Top Discogs comparison — {sanitizedOwnName} vs {sanitizedOtherName}",
                Content = discogsTaste.result,
                Url = raw.DiscogsUrl
            });
        }

        return pages;
    }

    public static void BuildTastePage(ResponseModel response, TasteCacheModel cacheModel, int pageIndex,
        string cacheKey, ulong ownDiscordId, ulong otherDiscordId, string timePeriodKey, int amount)
    {
        var pages = cacheModel.Pages;
        if (pageIndex >= pages.Count)
        {
            pageIndex = 0;
        }

        var page = pages[pageIndex];
        var container = response.ComponentsContainer;

        if (cacheModel.AccentColor != DiscordConstants.LastFmColorRed)
        {
            container.WithAccentColor(cacheModel.AccentColor);
        }

        container.WithTextDisplay(page.Title.ContainsEmoji()
            ? $"### {page.Title}"
            : $"### [{page.Title}]({page.Url})");
        container.WithTextDisplay(page.Content);

        var tabRow = new ActionRowProperties();
        for (var i = 0; i < pages.Count; i++)
        {
            var isActive = i == pageIndex;
            var tabPage = pages[i];
            tabRow.WithButton(
                tabPage.Label,
                customId: $"{InteractionConstants.Taste.Tab}:{cacheKey}:{i}:{ownDiscordId}:{otherDiscordId}:{timePeriodKey}:{amount}",
                style: isActive ? ButtonStyle.Primary : ButtonStyle.Secondary,
                disabled: isActive);
        }

        var toggledAmount = amount == 28 ? 14 : 28;
        tabRow.WithButton(
            null,
            customId: $"{InteractionConstants.Taste.Tab}:{cacheKey}:{pageIndex}:{ownDiscordId}:{otherDiscordId}:{timePeriodKey}:{toggledAmount}",
            style: ButtonStyle.Secondary,
            emote: amount == 28 ? EmojiProperties.Custom(1483232860755460117) : EmojiProperties.Custom(1483232894318149692));

        container.WithActionRow(tabRow);
    }

    public void SwitchTasteAmount(TasteCacheModel cacheModel, int newAmount)
    {
        if (cacheModel.Amount == newAmount)
        {
            return;
        }

        cacheModel.Pages = BuildTastePages(this._artistsService, cacheModel.RawData, newAmount);
        cacheModel.Amount = newAmount;
    }

    public async Task<ResponseModel> RebuildTasteAsync(
        ulong ownDiscordId,
        ulong otherDiscordId,
        string timePeriodKey,
        int amount,
        int pageIndex,
        DiscordGuild guild)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var ownUser = await this._userService.GetUserWithDiscogs(ownDiscordId);
        var otherUser = await this._userService.GetUserWithDiscogs(otherDiscordId);

        if (ownUser == null || otherUser == null ||
            string.IsNullOrEmpty(ownUser.UserNameLastFM) || string.IsNullOrEmpty(otherUser.UserNameLastFM))
        {
            return response;
        }

        var timeSettings = SettingService.GetTimePeriod(timePeriodKey);

        var ownArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(ownUser.UserNameLastFM, timeSettings, 1000);
        var otherArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(otherUser.UserNameLastFM, timeSettings, 1000);

        var ownArtists = await ownArtistsTask;
        var otherArtists = await otherArtistsTask;

        if (!ownArtists.Success || ownArtists.Content?.TopArtists == null ||
            !otherArtists.Success || otherArtists.Content?.TopArtists == null)
        {
            return response;
        }

        string ownName;
        string otherName;

        if (guild?.Users.TryGetValue(ownDiscordId, out var discordGuildUser) == true)
        {
            ownName = discordGuildUser.GetDisplayName();
        }
        else
        {
            var discordUser = await this._client.Rest.GetUserAsync(ownDiscordId);
            ownName = discordUser.GetDisplayName();
        }

        if (guild?.Users.TryGetValue(otherDiscordId, out var otherDiscordGuildUser) == true)
        {
            otherName = otherDiscordGuildUser.GetDisplayName();
        }
        else
        {
            var discordUser = await this._client.Rest.GetUserAsync(otherDiscordId);
            otherName = discordUser.GetDisplayName();
        }

        var url = LastfmUrlExtensions.GetUserUrl(otherUser.UserNameLastFM, $"/library/artists?{timeSettings.UrlParameter}");

        var rawData = new TasteRawData
        {
            OwnUsername = ownUser.UserNameLastFM,
            OtherUsername = otherUser.UserNameLastFM,
            OwnName = ownName,
            OtherName = otherName,
            Url = url,
            TimeDescription = timeSettings.Description,
            OwnTopArtists = ownArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList(),
            OtherTopArtists = otherArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList(),
        };

        await EnrichTasteRawDataAsync(rawData, ownArtists.Content.TopArtists, otherArtists.Content.TopArtists,
            ownUser, otherUser);

        var cacheModel = new TasteCacheModel
        {
            AccentColor = await UserService.GetAccentColor(ownUser, guild),
            Amount = amount,
            RawData = rawData,
            Pages = BuildTastePages(this._artistsService, rawData, amount)
        };

        var cacheKey = Guid.NewGuid().ToString("N")[..8];
        this._cache.Set($"taste-{cacheKey}", cacheModel, TimeSpan.FromMinutes(10));

        BuildTastePage(response, cacheModel, pageIndex, cacheKey, ownDiscordId, otherDiscordId,
            timePeriodKey, amount);
        return response;
    }

    public async Task<ResponseModel> AffinityAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        Guild guild,
        IDictionary<int, FullGuildUser> guildUsers,
        bool largeGuild)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var bypassCache = false;
        if (!guildUsers.ContainsKey(userSettings.UserId))
        {
            var guildUser = await context.DiscordGuild.GetUserAsync(userSettings.DiscordUserId);
            if (guildUser != null)
            {
                await this._indexService.AddOrUpdateGuildUser(guildUser, false);
                bypassCache = true;
            }
        }

        var guildTopAllTimeTask =
            this._whoKnowsArtistService.GetAllTimeTopArtistForGuild(guild.GuildId, largeGuild, bypassCache);
        var guildTopQuarterlyTask =
            this._whoKnowsArtistService.GetQuarterlyTopArtistForGuild(guild.GuildId, largeGuild, bypassCache);

        var guildTopAllTime = await guildTopAllTimeTask;
        var guildTopQuarterly = await guildTopQuarterlyTask;

        var ownAllTime = guildTopAllTime.Where(w => w.UserId == userSettings.UserId).ToList();
        var ownQuarterly = guildTopQuarterly.Where(w => w.UserId == userSettings.UserId).ToList();

        var concurrentNeighbors =
            await this._whoKnowsArtistService.GetAffinity(guildTopAllTime, ownAllTime, guildTopQuarterly, ownQuarterly);

        var filter = GuildService.FilterGuildUsers(guildUsers, guild);
        var filteredUserIds = filter.FilteredGuildUsers
            .Keys
            .Distinct()
            .ToHashSet();

        concurrentNeighbors.TryGetValue(userSettings.UserId, out var self);

        if (self == null)
        {
            response.Embed.WithDescription(
                "Sorry, you are not added to this server yet or you are not in the results. Run `/refreshmembers`, wait a bit and try again.");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var neighbors = concurrentNeighbors
            .Where(w => filteredUserIds.Contains(w.Key))
            .ToDictionary(d => d.Key, d => d.Value);

        var neighborPages = neighbors
            .Where(w => w.Key != userSettings.UserId)
            .OrderByDescending(o => o.Value.TotalPoints)
            .Take(120)
            .Chunk(12)
            .ToList();

        var pageCounter = 1;
        var pages = new List<PageBuilder>();
        foreach (var page in neighborPages)
        {
            var pageString = new StringBuilder();
            foreach (var neighbor in page)
            {
                guildUsers.TryGetValue(neighbor.Key, out var guildUser);
                pageString.AppendLine(
                    $"**{CalculateAffinityPercentage(neighbor.Value.TotalPoints, self.TotalPoints)}** — " +
                    $"**[{StringExtensions.Sanitize(guildUser?.UserName)}]({LastfmUrlExtensions.GetUserUrl(guildUser?.UserNameLastFM)})** — " +
                    $"`{CalculateAffinityPercentage(neighbor.Value.ArtistPoints, self.ArtistPoints)}` artists, " +
                    $"`{CalculateAffinityPercentage(neighbor.Value.GenrePoints, self.GenrePoints)}` genres, " +
                    $"`{CalculateAffinityPercentage(neighbor.Value.CountryPoints, self.CountryPoints, 1)}` countries");
            }

            var pageFooter = new StringBuilder();
            pageFooter.Append(
                $"Page {pageCounter}/{neighborPages.Count} - {filteredUserIds.Count} .fmbot members in this server");

            pages.Add(new PageBuilder()
                .WithTitle($"Server neighbors for {userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}")
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithDescription(pageString.ToString())
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        if (!neighborPages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("Could not find users with a similar music taste."));
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);

        return response;
    }

    private static string CalculateAffinityPercentage(double neighborPoints, double ownPoints, int multiplier = 2)
    {
        var numberInfo = new NumberFormatInfo
        {
            PercentPositivePattern = 1
        };

        var result = neighborPoints / ownPoints * multiplier;

        if (result > 1)
        {
            result = 1;
        }

        return result.ToString("P0", numberInfo);
    }

    public async Task<ResponseModel> GetIceberg(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageOnly
        };

        var topArtists =
            await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm, timeSettings, 500);

        if (!topArtists.Success)
        {
            response.Embed.ErrorResponse(topArtists.Error, topArtists.Message, "top tracks", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (topArtists.Content?.TopArtists == null || !topArtists.Content.TopArtists.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have any top artists in the selected time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var artists = await this._artistsService.GetArtistsPopularity(topArtists.Content.TopArtists);

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "bot", "iceberg.png");
        var saveFile = !File.Exists(path);

        await using Stream gifStream = saveFile
            ? await this._dataSourceFactory.GetAlbumImageAsStreamAsync("https://fm.bot/img/bot/iceberg.png")
            : File.OpenRead(path);

        using var bitmap = SKBitmap.Decode(gifStream);

        if (saveFile)
        {
            await ChartService.SaveImageToCache(bitmap, path);
        }

        this._puppeteerService.CreatePopularityIcebergImage(bitmap,
            StringExtensions.TruncateLongString(userSettings.DisplayName, 16),
            timeSettings.Description, artists);

        var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.FileName = "iceberg.png";

        return response;
    }
}
