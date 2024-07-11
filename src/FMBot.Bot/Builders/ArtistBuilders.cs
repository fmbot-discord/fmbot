using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
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
using SkiaSharp;
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
    private readonly IUpdateService _updateService;
    private readonly IIndexService _indexService;
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

    public ArtistBuilders(ArtistsService artistsService,
        IDataSourceFactory dataSourceFactory,
        GuildService guildService,
        UserService userService,
        WhoKnowsArtistService whoKnowsArtistService,
        PlayService playService,
        IUpdateService updateService,
        IIndexService indexService,
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
        MusicDataFactory musicDataFactory)
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
    }

    public async Task<ResponseModel> ArtistInfoAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var fullArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, searchValue, redirectsEnabled);

        var footer = new StringBuilder();

        var featuredHistory = await this._featuredService.GetArtistFeaturedHistory(artistSearch.Artist.ArtistName);
        if (featuredHistory.Any())
        {
            footer.AppendLine($"Featured {featuredHistory.Count} {StringExtensions.GetTimesString(featuredHistory.Count)}");
        }

        if (fullArtist.SpotifyImageUrl != null)
        {
            response.Embed.WithThumbnailUrl(fullArtist.SpotifyImageUrl);
            footer.AppendLine("Image source: Spotify");
        }

        if (context.ContextUser.TotalPlaycount.HasValue && artistSearch.Artist.UserPlaycount is >= 10)
        {
            footer.AppendLine($"{(decimal)artistSearch.Artist.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value:P} of all your plays are on this artist");
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName($"Artist: {artistSearch.Artist.ArtistName} for {userTitle}");
        response.EmbedAuthor.WithUrl(artistSearch.Artist.ArtistUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        string firstListenInfo = null;
        if (context.ContextUser.UserType != UserType.User && artistSearch.Artist.UserPlaycount > 0)
        {
            var firstPlay =
                await this._playService.GetArtistFirstPlayDate(context.ContextUser.UserId,
                    artistSearch.Artist.ArtistName);
            if (firstPlay != null)
            {
                var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();

                firstListenInfo = $"Discovered on: <t:{firstListenValue}:D>";
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                response.Embed.WithDescription($"*[Supporters]({Constants.GetSupporterDiscordLink}) can see artist discovery dates.*");
            }
        }

        if (!string.IsNullOrWhiteSpace(fullArtist.Type) ||
            !string.IsNullOrWhiteSpace(fullArtist.Location) ||
            !string.IsNullOrWhiteSpace(fullArtist.Disambiguation))
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

            if (firstListenInfo != null)
            {
                artistInfo.AppendLine(firstListenInfo);
                firstListenInfo = null;
            }

            if (artistInfo.Length > 0)
            {
                if (response.Embed.Description is { Length: > 0 })
                {
                    response.Embed.WithDescription(
                        artistInfo + "\n" + response.Embed.Description);

                }
                else
                {
                    response.Embed.WithDescription(artistInfo.ToString());
                }
            }
        }

        if (context.DiscordGuild != null)
        {
            var serverStats = "";
            var contextGuild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);

            if (contextGuild?.LastIndexed != null)
            {
                var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

                var usersWithArtist = await this._whoKnowsArtistService.GetIndexedUsersForArtist(context.DiscordGuild, guildUsers, contextGuild.GuildId, artistSearch.Artist.ArtistName);
                var (filterStats, filteredUsersWithArtist) = WhoKnowsService.FilterWhoKnowsObjects(usersWithArtist, contextGuild);

                if (filteredUsersWithArtist.Count != 0)
                {
                    var serverListeners = filteredUsersWithArtist.Count;
                    var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);
                    var serverPlaycountLastWeek = await this._playService.GetWeekArtistPlaycountForGuildAsync(contextGuild.GuildId, artistSearch.Artist.ArtistName);

                    serverStats += $"`{serverListeners}` {StringExtensions.GetListenersString(serverListeners)}";
                    serverStats += $"\n`{serverPlaycount}` total {StringExtensions.GetPlaysString(serverPlaycount)}";
                    serverStats += $"\n`{(int)avgServerPlaycount}` avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                    serverStats += $"\n`{serverPlaycountLastWeek}` {StringExtensions.GetPlaysString(serverPlaycountLastWeek)} last week";

                    if (filterStats.BasicDescription != null)
                    {
                        serverStats += $"\n{filterStats.BasicDescription}";
                    }
                }

                var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(context.ContextUser.UserId,
                    guildUsers, contextGuild, artistSearch.Artist.ArtistName);

                if (guildAlsoPlaying != null)
                {
                    footer.AppendLine(guildAlsoPlaying);
                }
            }
            else
            {
                serverStats += $"Run `{context.Prefix}index` to get server stats";
            }

            if (!string.IsNullOrWhiteSpace(serverStats))
            {
                response.Embed.AddField("Server stats", serverStats, true);
            }
        }

        if (firstListenInfo != null && string.IsNullOrEmpty(response.Embed.Description))
        {
            response.Embed.WithDescription(firstListenInfo);
        }

        var globalStats = "";
        globalStats += $"`{artistSearch.Artist.TotalListeners}` {StringExtensions.GetListenersString(artistSearch.Artist.TotalListeners)}";
        globalStats += $"\n`{artistSearch.Artist.TotalPlaycount}` global {StringExtensions.GetPlaysString(artistSearch.Artist.TotalPlaycount)}";
        if (artistSearch.Artist.UserPlaycount.HasValue)
        {
            globalStats += $"\n`{artistSearch.Artist.UserPlaycount}` {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)} by you";
            globalStats += $"\n`{await this._playService.GetArtistPlaycountForTimePeriodAsync(context.ContextUser.UserId, artistSearch.Artist.ArtistName)}` by you last week";
            await this._updateService.CorrectUserArtistPlaycount(context.ContextUser.UserId, artistSearch.Artist.ArtistName,
                artistSearch.Artist.UserPlaycount.Value);
        }

        response.Embed.AddField("Last.fm stats", globalStats, true);

        if (artistSearch.Artist.Description != null)
        {
            response.Embed.AddField("Summary", artistSearch.Artist.Description);
        }

        if (fullArtist.ArtistGenres != null && fullArtist.ArtistGenres.Any())
        {
            footer.AppendLine(GenreService.GenresToString(fullArtist.ArtistGenres.ToList()));
        }

        response.Components = new ComponentBuilder()
            .WithButton("Overview", $"{InteractionConstants.Artist.Overview}-{fullArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, emote: new Emoji("\ud83d\udcca"));

        if (context.ContextUser.RymEnabled == true && fullArtist.ArtistLinks != null && fullArtist.ArtistLinks.Any(a => a.Type == LinkType.RateYourMusic))
        {
            var rym = fullArtist.ArtistLinks.First(f => f.Type == LinkType.RateYourMusic);
            response.Components.WithButton(style: ButtonStyle.Link, emote: Emote.Parse(DiscordConstants.RateYourMusic), url: rym.Url);
        }
        else if (fullArtist.SpotifyId != null)
        {
            response.Components.WithButton(style: ButtonStyle.Link,
                emote: Emote.Parse(DiscordConstants.Spotify), url: $"https://open.spotify.com/artist/{fullArtist.SpotifyId}");

            if (fullArtist.AppleMusicUrl != null)
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.AppleMusic), url: fullArtist.AppleMusicUrl);
            }
        }

        if (fullArtist.ArtistLinks != null && fullArtist.ArtistLinks.Any())
        {
            var facebook = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Facebook);
            if (facebook != null && fullArtist.ArtistLinks.All(a => a.Type != LinkType.Instagram))
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.Facebook), url: facebook.Url);
            }
            var instagram = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Instagram);
            if (instagram != null)
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.Instagram), url: instagram.Url);
            }
            var twitter = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Twitter);
            if (twitter != null)
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.Twitter), url: twitter.Url);
            }
            var tiktok = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.TikTok);
            if (tiktok != null)
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.TikTok), url: tiktok.Url);
            }
            var bandcamp = fullArtist.ArtistLinks.FirstOrDefault(f => f.Type == LinkType.Bandcamp);
            if (bandcamp != null)
            {
                response.Components.WithButton(style: ButtonStyle.Link,
                    emote: Emote.Parse(DiscordConstants.Bandcamp), url: bandcamp.Url);
            }
        }

        response.Embed.WithFooter(footer.ToString());
        return response;
    }

    public async Task<ResponseModel> ArtistOverviewAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValue,
        bool redirectsEnabled)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userId: context.ContextUser.UserId,
            otherUserUsername: userSettings.UserNameLastFm, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var fullArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, searchValue, redirectsEnabled);

        var footer = new StringBuilder();

        string userTitle;
        var user = context.ContextUser;
        var determiner = "Your";

        if (userSettings.DifferentUser)
        {
            userTitle =
                $"{userSettings.DisplayName}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            user = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);
            determiner = "Their";
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.EmbedAuthor.WithName($"Artist overview about {artistSearch.Artist.ArtistName} for {userTitle}");
        response.EmbedAuthor.WithUrl(artistSearch.Artist.ArtistUrl);
        response.Embed.WithAuthor(response.EmbedAuthor);

        if (context.ContextUser.TotalPlaycount.HasValue && artistSearch.Artist.UserPlaycount is >= 10)
        {
            footer.AppendLine($"{(decimal)artistSearch.Artist.UserPlaycount.Value / context.ContextUser.TotalPlaycount.Value:P} of all {determiner.ToLower()} scrobbles are on this artist");
        }

        var description = new StringBuilder();
        description.Append("-# *");
        if (user.UserType != UserType.User && artistSearch.Artist.UserPlaycount > 0)
        {
            var firstPlay = await this._playService.GetArtistFirstPlayDate(user.UserId, artistSearch.Artist.ArtistName);
            if (firstPlay != null)
            {
                var firstListenValue = ((DateTimeOffset)firstPlay).ToUnixTimeSeconds();

                description.Append($"Discovered on: <t:{firstListenValue}:D> — ");
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowSupporterPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildSupporterPromoCache(context.DiscordGuild?.Id);
                description.Append($"*[Supporters]({Constants.GetSupporterDiscordLink}) can see artist discovery dates.*");
            }
        }

        if (artistSearch.Artist.UserPlaycount > 0)
        {
            description.Append(
                $"{artistSearch.Artist.UserPlaycount} {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)} on this artist");

            var playsLastWeek =
                await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId, artistSearch.Artist.ArtistName);
            if (playsLastWeek != 0)
            {
                description.Append($" — {playsLastWeek} {StringExtensions.GetPlaysString(playsLastWeek)} last week");
            }
        }
        else
        {
            description.Append("No plays on this artist yet");
        }

        description.Append("*");

        response.Embed.WithDescription(description.ToString());

        if (user.UserDiscogs != null && user.DiscogsReleases.Any())
        {
            var artistCollection = user.DiscogsReleases
                .Where(w => w.Release.Artist.StartsWith(artistSearch.Artist.ArtistName, StringComparison.OrdinalIgnoreCase) ||
                            artistSearch.Artist.ArtistName.StartsWith(w.Release.Artist, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (artistCollection.Any())
            {
                var artistCollectionDescription = new StringBuilder();
                foreach (var album in artistCollection.Take(4))
                {
                    artistCollectionDescription.Append(StringService.UserDiscogsWithAlbumName(album));
                }

                if (artistCollection.Count > 4)
                {
                    artistCollectionDescription.Append($"*Plus {artistCollection.Count - 4} more {StringExtensions.GetItemsString(artistCollection.Count - 4)} in your collection*");
                }
                response.Embed.AddField($"{determiner} Discogs collection", artistCollectionDescription.ToString());
            }
        }

        var artistTracksButton = false;
        var artistAlbumsButton = false;
        if (artistSearch.Artist.UserPlaycount > 0)
        {
            var topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);

            if (topTracks.Any())
            {
                var topTracksDescription = new StringBuilder();
                artistTracksButton = true;

                var counter = 1;
                foreach (var track in topTracks.Take(8))
                {
                    topTracksDescription.AppendLine($"`{counter}` **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.Name, 40))}** - " +
                                                    $"*{track.Playcount}x*");
                    counter++;
                }

                response.Embed.AddField($"{determiner} top tracks", topTracksDescription.ToString(), true);
            }

            var topAlbums = await this._artistsService.GetTopAlbumsForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
            if (topAlbums.Any())
            {
                var topAlbumsDescription = new StringBuilder();
                artistAlbumsButton = true;

                var counter = 1;
                foreach (var album in topAlbums.Take(8))
                {
                    topAlbumsDescription.AppendLine($"`{counter}` **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.Name, 40))}** - " +
                                                    $"*{album.Playcount}x*");
                    counter++;
                }

                response.Embed.AddField($"{determiner} top albums", topAlbumsDescription.ToString(), true);
            }
        }

        if (fullArtist.ArtistGenres != null && fullArtist.ArtistGenres.Any())
        {
            footer.AppendLine(GenreService.GenresToString(fullArtist.ArtistGenres.ToList()));
        }

        var components = new ComponentBuilder()
            .WithButton("Artist", $"{InteractionConstants.Artist.Info}-{fullArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, emote: Emote.Parse(DiscordConstants.Info));

        components.WithButton("All top tracks",
            $"{InteractionConstants.Artist.Tracks}-{fullArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, disabled: !artistTracksButton, emote: Emoji.Parse("🎶"));

        components.WithButton("All top albums",
            $"{InteractionConstants.Artist.Albums}-{fullArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}", style: ButtonStyle.Secondary, disabled: !artistAlbumsButton, emote: Emoji.Parse("💽"));

        response.Components = components;

        response.Embed.WithFooter(footer.ToString());
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
            ResponseType = ResponseType.Paginator,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue, context.ContextUser.UserNameLastFM,
            context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm, true, userSettings.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
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
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 7, artistSearch.Artist.ArtistName);
                break;
            case TimePeriod.Monthly:
                topTracks = await this._playService.GetUserTopTracksForArtist(userSettings.UserId, 31, artistSearch.Artist.ArtistName);
                break;
            default:
                topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
                break;
        }

        if (topTracks.Count == 0 &&
            timeSettings.TimePeriod == TimePeriod.AllTime &&
            artistSearch.Artist.UserPlaycount >= 15 &&
            !userSettings.DifferentUser)
        {
            var user = await this._userService.GetUserForIdAsync(userSettings.UserId);
            await this._indexService.ModularUpdate(user, UpdateType.Tracks);
            topTracks = await this._artistsService.GetTopTracksForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
        }

        var pages = new List<PageBuilder>();
        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (topTracks.Count == 0)
        {
            response.Embed.WithDescription(
                $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()} has no registered tracks for the artist **{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}** in .fmbot.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var url = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/music/{UrlEncoder.Default.Encode(artistSearch.Artist.ArtistName)}");

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
                albumPageString.AppendLine($"{counter}. **{StringExtensions.Sanitize(track.Name)}** - *{track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}*");
                counter++;
            }

            var footer = new StringBuilder();

            if (artistSearch.IsRandom)
            {
                footer.AppendLine($"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
            }

            footer.AppendLine($"Page {pageCounter}/{topTrackPages.Count} - {topTracks.Count} different tracks");
            var title = new StringBuilder();

            if (userSettings.DifferentUser)
            {
                footer.AppendLine($"{userSettings.UserNameLastFm} has {artistSearch.Artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DisplayName}'s top tracks for '{artistSearch.Artist.ArtistName}'");
            }
            else
            {
                footer.Append($"{userTitle} has {artistSearch.Artist.UserPlaycount} total scrobbles on this artist");
                title.Append($"Your top tracks for '{artistSearch.Artist.ArtistName}'");

                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }

            response.EmbedAuthor.WithName(title.ToString());

            pages.Add(new PageBuilder()
                .WithDescription(albumPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        var optionId = $"{InteractionConstants.Artist.Overview}-{dbArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}";
        var optionEmote = new Emoji("\ud83d\udcca");

        if (pages.Count == 1)
        {
            response.ResponseType = ResponseType.Embed;
            response.SinglePageToEmbedResponseWithButton(pages.First(), optionId, optionEmote, "Overview");
        }
        else
        {
            response.StaticPaginator = StringService.BuildStaticPaginator(pages, optionId, optionEmote);
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
            ResponseType = ResponseType.Paginator,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, searchValue,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, userSettings.UserNameLastFm,
            true, userSettings.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var dbArtist =
            await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, redirectsEnabled: redirectsEnabled);

        if (artistSearch.Artist.UserPlaycount.HasValue && !userSettings.DifferentUser)
        {
            await this._updateService.CorrectUserArtistPlaycount(userSettings.UserId, artistSearch.Artist.ArtistName,
                artistSearch.Artist.UserPlaycount.Value);
        }

        var topAlbums = await this._artistsService.GetTopAlbumsForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (topAlbums.Count == 0 &&
            artistSearch.Artist.UserPlaycount >= 15 &&
            !userSettings.DifferentUser)
        {
            var user = await this._userService.GetUserForIdAsync(userSettings.UserId);
            await this._indexService.ModularUpdate(user, UpdateType.Albums);
            topAlbums = await this._artistsService.GetTopAlbumsForArtist(userSettings.UserId, artistSearch.Artist.ArtistName);
        }

        if (topAlbums.Count == 0)
        {
            response.Embed.WithDescription(
                $"{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()} has no scrobbles for this artist or their scrobbles have no album associated with them.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var url = $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/music/{UrlEncoder.Default.Encode(artistSearch.Artist.ArtistName)}";
        if (Uri.IsWellFormedUriString(url, UriKind.Absolute))
        {
            response.EmbedAuthor.WithUrl(url);
        }

        var pages = new List<PageBuilder>();
        var albumPages = topAlbums.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var albumPage in albumPages)
        {
            var albumPageString = new StringBuilder();
            foreach (var artistAlbum in albumPage)
            {
                albumPageString.AppendLine($"{counter}. **{artistAlbum.Name}** - *{artistAlbum.Playcount} {StringExtensions.GetPlaysString(artistAlbum.Playcount)}*");
                counter++;
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{albumPages.Count}");
            var title = new StringBuilder();

            if (userSettings.DifferentUser)
            {
                footer.AppendLine($" - {userSettings.UserNameLastFm} has {artistSearch.Artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DisplayName}'s top albums for '{artistSearch.Artist.ArtistName}'");
            }
            else
            {
                footer.Append($" - {userTitle} has {artistSearch.Artist.UserPlaycount} total scrobbles on this artist");
                title.Append($"Your top albums for '{artistSearch.Artist.ArtistName}'");

                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }

            response.EmbedAuthor.WithName(title.ToString());

            var page = new PageBuilder()
                .WithDescription(albumPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString());

            pages.Add(page);
            pageCounter++;
        }

        var optionId = $"{InteractionConstants.Artist.Overview}-{dbArtist.Id}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}";
        var optionEmote = new Emoji("\ud83d\udcca");

        if (pages.Count == 1)
        {
            response.ResponseType = ResponseType.Embed;
            response.SinglePageToEmbedResponseWithButton(pages.First(), optionId, optionEmote, "Overview");
        }
        else
        {
            response.StaticPaginator = StringService.BuildStaticPaginator(pages, optionId, optionEmote);
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
            topGuildArtists = await this._whoKnowsArtistService.GetTopAllTimeArtistsForGuild(guild.GuildId, guildListSettings.OrderType);
        }
        else
        {
            var plays = await this._playService.GetGuildUsersPlays(guild.GuildId,
                guildListSettings.AmountOfDaysWithBillboard);

            topGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.StartDateTime, guildListSettings.OrderType);
            previousTopGuildArtists = PlayService.GetGuildTopArtists(plays, guildListSettings.BillboardStartDateTime, guildListSettings.OrderType);
        }

        var title = $"Top {guildListSettings.TimeDescription.ToLower()} artists in {context.DiscordGuild.Name}";

        var footer = new StringBuilder();
        footer.AppendLine(guildListSettings.OrderType == OrderType.Listeners
            ? " - Ordered by listeners"
            : " - Ordered by plays");

        var rnd = new Random();
        var randomHintNumber = rnd.Next(0, 5);
        switch (randomHintNumber)
        {
            case 1:
                footer.AppendLine($"View specific track listeners with '{context.Prefix}whoknows'");
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
                    ? $"`{track.ListenerCount}` · **{track.ArtistName}** · *{track.TotalPlaycount} {StringExtensions.GetPlaysString(track.TotalPlaycount)}*"
                    : $"`{track.TotalPlaycount}` · **{track.ArtistName}** · *{track.ListenerCount} {StringExtensions.GetListenersString(track.ListenerCount)}*";

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

        var userUrl = LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm,
            $"/library/artists?{timeSettings.UrlParameter}");

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artists for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);

        var artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
            timeSettings, 200, 1);

        if (!artists.Success || artists.Content == null)
        {
            response.Embed.ErrorResponse(artists.Error, artists.Message, "top artists", context.DiscordUser);
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }
        if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
        {
            response.Embed.WithDescription($"Sorry, you or the user you're searching for don't have any top artists in the [selected time period]({userUrl}).");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (mode == ResponseMode.Image)
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            artists.Content.TopArtists = await this._artistsService.FillArtistImages(artists.Content.TopArtists);

            var firstArtistImage =
                artists.Content.TopArtists.FirstOrDefault(f => f.ArtistImageUrl != null)?.ArtistImageUrl;

            var image = await this._puppeteerService.GetTopList(userTitle, "Top Artists", "artists", timeSettings.Description,
                artists.Content.TotalAmount.GetValueOrDefault(), totalPlays.GetValueOrDefault(), firstArtistImage, artists.TopList);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"top-artists-{userSettings.DiscordUserId}";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var previousTopArtists = new List<TopArtist>();
        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._dataSourceFactory
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

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
                    $"**[{artist.ArtistName}]({artist.ArtistUrl})** - *{artist.UserPlaycount} {StringExtensions.GetPlaysString(artist.UserPlaycount)}*";

                if (topListSettings.Billboard && previousTopArtists.Any())
                {
                    var previousTopArtist = previousTopArtists.FirstOrDefault(f => f.ArtistName == artist.ArtistName);
                    int? previousPosition = previousTopArtist == null ? null : previousTopArtists.IndexOf(previousTopArtist);

                    artistPageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
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
                footer.Append($" - {artists.Content.TotalAmount} different artists in this time period");
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
                .WithDescription(artistPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
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
            response.Embed.WithDescription($"To see what artists you've recently discovered we need to store your lifetime Last.fm history. Your lifetime history and more are only available for supporters.");

            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            return response;
        }

        if (userSettings.UserType == UserType.User)
        {
            response.Embed.WithDescription($"Sorry, artist discoveries uses someone their lifetime listening history. You can only use this command on other supporters.");

            response.Components = new ComponentBuilder()
                .WithButton(".fmbot supporter", style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
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

            var totalPlays = allPlays.Count(w => w.TimePlayed >= timeSettings.StartDateTime && w.TimePlayed <= timeSettings.EndDateTime);
            var backgroundImage = (await this._artistsService.GetArtistFromDatabase(topNewArtists.First().ArtistName))?.SpotifyImageUrl;

            var image = await this._puppeteerService.GetTopList(userTitle, "Newly discovered artists", "new artists", timeSettings.Description,
                topNewArtists.Count, totalPlays, backgroundImage, topList);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"top-tracks-{userSettings.DiscordUserId}";
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
                    $"— *{newArtist.UserPlaycount} {StringExtensions.GetPlaysString(newArtist.UserPlaycount)}* " +
                    $"— on **<t:{newArtist.FirstPlay.Value.ToUnixEpochDate()}:D>**");

                counter++;
            }

            var footer = new StringBuilder();

            footer.Append($"Page {pageCounter}/{artistPages.Count}");

            footer.Append($" - {topNewArtists.Count} newly discovered artists");

            pages.Add(new PageBuilder()
                .WithDescription(artistPageString.ToString())
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

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
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
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var reply =
            $"**{StringExtensions.Sanitize(userSettings.DisplayName)}{userSettings.UserType.UserTypeToIcon()}** has " +
            $"**{artistSearch.Artist.UserPlaycount}** {StringExtensions.GetPlaysString(artistSearch.Artist.UserPlaycount)} for " +
            $"**{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}**";

        if (!userSettings.DifferentUser && context.ContextUser.LastUpdated != null)
        {
            var playsLastWeek =
                await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId, artistSearch.Artist.ArtistName);
            if (playsLastWeek != 0)
            {
                reply += $"\n-# *{playsLastWeek} {StringExtensions.GetPlaysString(playsLastWeek)} last week*";
            }
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
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        goalAmount = SettingService.GetGoalAmount(amount, artistSearch.Artist.UserPlaycount.GetValueOrDefault(0));

        var regularPlayCount = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom, userSettings.SessionKeyLastFm);

        if (regularPlayCount is null or 0)
        {
            response.Text = $"<@{context.DiscordUser.Id}> No plays found in the {timeSettings.Description} time period.";
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var artistPlayCount =
            await this._playService.GetArtistPlaycountForTimePeriodAsync(userSettings.UserId, artistSearch.Artist.ArtistName,
                timeSettings.PlayDays.GetValueOrDefault(30));

        if (artistPlayCount is 0)
        {
            response.Text = $"<@{context.DiscordUser.Id}> No plays found on **{artistSearch.Artist.ArtistName}** in the last {timeSettings.PlayDays} days.";
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
            reply.Append($"<@{context.DiscordUser.Id}> My estimate is that the user '{StringExtensions.Sanitize(userSettings.UserNameLastFm)}'");
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

        reply.AppendLine($" will reach **{goalAmount}** plays on **{StringExtensions.Sanitize(artistSearch.Artist.ArtistName)}** {goalDateString}");

        reply.AppendLine(
            $"This is based on {determiner} avg of {Math.Round(avgPerDay, 2)} per day in the last {Math.Round(totalDays, 0)} days ({artistPlayCount} total - {artistSearch.Artist.UserPlaycount} alltime)");

        response.Text = reply.ToString();
        return response;
    }

    public async Task<ResponseModel> WhoKnowsArtistAsync(ContextModel context,
        ResponseMode mode,
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
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName, redirectsEnabled);

        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var contextGuild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var usersWithArtist = await this._whoKnowsArtistService.GetIndexedUsersForArtist(context.DiscordGuild, guildUsers, contextGuild.GuildId, artistSearch.Artist.ArtistName);

        var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId);
        await this._indexService.GetOrAddUserToGuild(guildUsers, contextGuild, discordGuildUser, context.ContextUser);
        await this._indexService.UpdateGuildUser(guildUsers, discordGuildUser, context.ContextUser.UserId, contextGuild);

        usersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, context.ContextUser, artistSearch.Artist.ArtistName, context.DiscordGuild, artistSearch.Artist.UserPlaycount);

        var (filterStats, filteredUsersWithArtist) = WhoKnowsService.FilterWhoKnowsObjects(usersWithArtist, contextGuild, roles);

        CrownModel crownModel = null;
        if (contextGuild.CrownsDisabled != true && filteredUsersWithArtist.Count >= 1 && !displayRoleSelector && redirectsEnabled)
        {
            crownModel =
                await this._crownService.GetAndUpdateCrownForArtist(filteredUsersWithArtist, contextGuild, artistSearch.Artist.ArtistName);
            if (crownModel?.Stolen == true)
            {
                showCrownButton = true;
            }
        }

        if (showCrownButton)
        {
            var stolen = crownModel?.Stolen == true;
            response.Components = new ComponentBuilder()
                .WithButton("Crown history", $"{InteractionConstants.Artist.Crown}-{cachedArtist.Id}-{stolen}", style: ButtonStyle.Secondary, emote: new Emoji("👑"));
        }

        if (mode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"in <b>{context.DiscordGuild.Name}</b>", imgUrl, artistSearch.Artist.ArtistName,
                filteredUsersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server, crownModel?.Crown, crownModel?.CrownHtmlResult);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"whoknows-{artistSearch.Artist.ArtistName}";
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

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server, crownModel);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody in this server (not even you) has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = new StringBuilder();
        if (artistSearch.IsRandom)
        {
            footer.AppendLine($"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
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
            var serverListeners = filteredUsersWithArtist.Count(c => c.Playcount > 0);
            var serverPlaycount = filteredUsersWithArtist.Sum(a => a.Playcount);
            var avgServerPlaycount = filteredUsersWithArtist.Average(a => a.Playcount);

            footer.Append("Artist - ");
            footer.Append($"{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ");
            footer.Append($"{serverPlaycount} {StringExtensions.GetPlaysString(serverPlaycount)} - ");
            footer.AppendLine($"{(int)avgServerPlaycount} avg");
        }

        var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(context.ContextUser.UserId,
            guildUsers, contextGuild, artistSearch.Artist.ArtistName);

        if (guildAlsoPlaying != null)
        {
            footer.AppendLine(guildAlsoPlaying);
        }

        response.Embed.WithTitle($"{artistSearch.Artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} in {context.DiscordGuild.Name}");

        if (artistSearch.Artist.ArtistUrl != null && Uri.IsWellFormedUriString(artistSearch.Artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.Artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnailUrl(imgUrl);
        }

        if (displayRoleSelector)
        {
            if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
            {
                var allowedRoles = new SelectMenuBuilder()
                    .WithPlaceholder("Apply role filter..")
                    .WithCustomId($"{InteractionConstants.WhoKnowsRolePicker}-{cachedArtist.Id}")
                    .WithType(ComponentType.RoleSelect)
                    .WithMinValues(0)
                    .WithMaxValues(25);

                response.Components = new ComponentBuilder().WithSelectMenu(allowedRoles);
            }
            else
            {
                //response.Components = new ComponentBuilder().WithButton(Constants.GetPremiumServer, disabled: true, customId: "1");
            }
        }

        return response;
    }

    public SelectMenuBuilder GetFilterSelectMenu(string customId, Guild guild, IDictionary<int, FullGuildUser> guildUsers)
    {
        var builder = new SelectMenuBuilder();

        builder
            .WithPlaceholder("Set filter options")
            .WithCustomId(customId)
            .WithMinValues(0);

        if (guildUsers.Any(a => a.Value.BlockedFromWhoKnows))
        {
            builder.AddOption(new SelectMenuOptionBuilder("Blocked users", "blocked-users", "Filter out users you've manually blocked", isDefault: true));
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
            useCachedArtists: true, userId: context.ContextUser.UserId, redirectsEnabled: settings.RedirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName, settings.RedirectsEnabled);

        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var usersWithArtist = await this._whoKnowsArtistService.GetGlobalUsersForArtists(context.DiscordGuild, artistSearch.Artist.ArtistName);

        var filteredUsersWithArtist = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist, settings.QualityFilterDisabled);

        filteredUsersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(filteredUsersWithArtist, context.ContextUser, artistSearch.Artist.ArtistName, context.DiscordGuild, artistSearch.Artist.UserPlaycount);

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

        if (settings.ResponseMode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"in <b>.fmbot 🌐</b>", imgUrl, artistSearch.Artist.ArtistName,
                filteredUsersWithArtist, context.ContextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"global-whoknows-{artistSearch.Artist.ArtistName}";
            response.ResponseType = ResponseType.ImageOnly;

            if (safeForChannel == CensorService.CensorResult.Nsfw)
            {
                response.Spoiler = true;
                response.Embed.WithTitle("⚠️ NSFW - Click to reveal");
                response.ResponseType = ResponseType.ImageWithEmbed;
            }

            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithArtist, context.ContextUser.UserId, privacyLevel, hidePrivateUsers: settings.HidePrivateUsers);
        if (filteredUsersWithArtist.Count == 0)
        {
            serverUsers = "Nobody that uses .fmbot has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = new StringBuilder();
        if (artistSearch.IsRandom)
        {
            footer.AppendLine($"Artist #{artistSearch.RandomArtistPosition} ({artistSearch.RandomArtistPlaycount} {StringExtensions.GetPlaysString(artistSearch.RandomArtistPlaycount)})");
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
            footer.Append($"{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append($"{globalPlaycount} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{(int)avgPlaycount} avg");
        }

        //var guildAlsoPlaying = this._whoKnowsPlayService.GuildAlsoPlayingArtist(context.ContextUser.UserId,
        //    contextGuild, usersWithArtist, artistSearch.Artist.ArtistName);

        //if (guildAlsoPlaying != null)
        //{
        //    footer.AppendLine(guildAlsoPlaying);
        //}

        response.Embed.WithTitle($"{artistSearch.Artist.ArtistName}{ArtistsService.IsArtistBirthday(cachedArtist?.StartDate)} globally");

        if (Uri.IsWellFormedUriString(artistSearch.Artist.ArtistUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(artistSearch.Artist.ArtistUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnailUrl(imgUrl);
        }

        return response;
    }

    public async Task<ResponseModel> FriendsWhoKnowArtistAsync(ContextModel context,
        ResponseMode mode,
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
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist =
            await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName, redirectsEnabled);

        var safeForChannel = await this._censorService.IsSafeForChannel(context.DiscordGuild, context.DiscordChannel, cachedArtist.Name);
        var imgUrl = cachedArtist.SpotifyImageUrl;

        if (safeForChannel == CensorService.CensorResult.NotSafe)
        {
            imgUrl = null;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild?.Id);

        var usersWithArtist = await this._whoKnowsArtistService.GetFriendUsersForArtists(context.DiscordGuild, guildUsers, guild?.GuildId ?? 0, context.ContextUser.UserId, artistSearch.Artist.ArtistName);

        usersWithArtist = await WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, context.ContextUser, artistSearch.Artist.ArtistName, context.DiscordGuild, artistSearch.Artist.UserPlaycount);
        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (mode == ResponseMode.Image)
        {
            var image = await this._puppeteerService.GetWhoKnows("WhoKnows", $"from <b>{userTitle}</b>'s friends", imgUrl, artistSearch.Artist.ArtistName,
                usersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"friends-whoknow-{artistSearch.Artist.ArtistName}";
            response.ResponseType = ResponseType.ImageOnly;

            if (safeForChannel == CensorService.CensorResult.Nsfw)
            {
                response.Spoiler = true;
                response.Embed.WithTitle("⚠️ NSFW - Click to reveal");
                response.ResponseType = ResponseType.ImageWithEmbed;
            }

            return response;
        }

        var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithArtist, context.ContextUser.UserId, PrivacyLevel.Server);
        if (usersWithArtist.Count == 0)
        {
            serverUsers = "None of your friends has listened to this artist.";
        }

        response.Embed.WithDescription(serverUsers);

        var footer = new StringBuilder();

        if (cachedArtist.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
        {
            footer.AppendLine($"{GenreService.GenresToString(cachedArtist.ArtistGenres.ToList())}");
        }

        var amountOfHiddenFriends = context.ContextUser.Friends.Count(c => !c.FriendUserId.HasValue);
        if (amountOfHiddenFriends > 0)
        {
            footer.AppendLine($"{amountOfHiddenFriends} non-fmbot {StringExtensions.GetFriendsString(amountOfHiddenFriends)} not visible");
        }

        if (usersWithArtist.Any() && usersWithArtist.Count > 1)
        {
            var globalListeners = usersWithArtist.Count;
            var globalPlaycount = usersWithArtist.Sum(a => a.Playcount);
            var avgPlaycount = usersWithArtist.Average(a => a.Playcount);

            footer.Append($"{globalListeners} {StringExtensions.GetListenersString(globalListeners)} - ");
            footer.Append($"{globalPlaycount} {StringExtensions.GetPlaysString(globalPlaycount)} - ");
            footer.AppendLine($"{(int)avgPlaycount} avg");
        }

        footer.AppendLine($"Friends WhoKnow artist for {userTitle}");

        response.Embed.WithTitle($"{cachedArtist.Name} with friends");

        if (Uri.IsWellFormedUriString(cachedArtist.LastFmUrl, UriKind.Absolute))
        {
            response.Embed.WithUrl(cachedArtist.LastFmUrl);
        }

        response.EmbedFooter.WithText(footer.ToString());
        response.Embed.WithFooter(response.EmbedFooter);

        if (imgUrl != null && safeForChannel == CensorService.CensorResult.Safe)
        {
            response.Embed.WithThumbnailUrl(imgUrl);
        }

        return response;
    }

    public async Task<ResponseModel> TasteAsync(
        ContextModel context,
        TasteSettings tasteSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var ownLastFmUsername = context.ContextUser.UserNameLastFM;
        string lastfmToCompare = null;

        if (userSettings.DifferentUser)
        {
            lastfmToCompare = userSettings.UserNameLastFm;
        }

        if (lastfmToCompare == null)
        {
            response.Embed.WithDescription($"Please enter a valid Last.fm username or mention someone to compare yourself to.\n" +
                                        $"Examples:\n" +
                                        $"- `{context.Prefix}taste fm-bot`\n" +
                                        $"- `{context.Prefix}taste @.fmbot`");
            response.CommandResponse = CommandResponse.WrongInput;
            response.ResponseType = ResponseType.Embed;
            return response;
        }
        if (lastfmToCompare.ToLower() == ownLastFmUsername)
        {
            response.Embed.WithDescription($"You can't compare your own taste with yourself. For viewing your top artists, use `{context.Prefix}topartists`.\n\n" +
                                        $"Please enter a Last.fm username or mention someone to compare yourself to.\n" +
                                        $"Examples:\n" +
                                        $"- `{context.Prefix}taste fm-bot`\n" +
                                        $"- `{context.Prefix}taste @.fmbot`");
            response.CommandResponse = CommandResponse.WrongInput;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var ownArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(ownLastFmUsername, timeSettings, 1000);
        var otherArtistsTask = this._dataSourceFactory.GetTopArtistsAsync(lastfmToCompare, timeSettings, 1000);

        var ownArtists = await ownArtistsTask;
        var otherArtists = await otherArtistsTask;

        if (!ownArtists.Success || ownArtists.Content == null)
        {
            response.Embed.ErrorResponse(ownArtists.Error, ownArtists.Message, "taste", context.DiscordUser, "artist list");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (!otherArtists.Success || otherArtists.Content == null)
        {
            response.Embed.ErrorResponse(otherArtists.Error, otherArtists.Message, "taste", context.DiscordUser, "artist list");
            response.CommandResponse = CommandResponse.LastFmError;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (ownArtists.Content.TopArtists == null || ownArtists.Content.TopArtists.Count == 0 || otherArtists.Content.TopArtists == null || otherArtists.Content.TopArtists.Count == 0)
        {
            response.Text = "Sorry, you or the other user don't have any artist plays in the selected time period.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }


        var amount = tasteSettings.EmbedSize switch
        {
            EmbedSize.Default => 14,
            EmbedSize.Small => 7,
            EmbedSize.Large => 28,
            _ => 14
        };
        var pages = new List<PageBuilder>();

        var url = LastfmUrlExtensions.GetUserUrl(lastfmToCompare, $"/library/artists?{timeSettings.UrlParameter}");

        var ownName = context.DiscordUser.GlobalName ?? context.DiscordUser.Username;
        var otherName = userSettings.DisplayName;

        if (context.DiscordGuild != null)
        {
            var discordGuildUser = await context.DiscordGuild.GetUserAsync(context.ContextUser.DiscordUserId, CacheMode.CacheOnly);
            if (discordGuildUser != null)
            {
                ownName = discordGuildUser.DisplayName;
            }
        }

        var ownTopArtists =
            ownArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList();
        var otherTopArtists =
            otherArtists.Content.TopArtists.Select(s => new TasteItem(s.ArtistName, s.UserPlaycount)).ToList();

        var ownTopGenres = await this._genreService.GetTopGenresForTopArtists(ownArtists.Content.TopArtists);
        var otherTopGenres = await this._genreService.GetTopGenresForTopArtists(otherArtists.Content.TopArtists);

        var ownTopCountries = await this._countryService.GetTopCountriesForTopArtists(ownArtists.Content.TopArtists, true);
        var otherTopCountries = await this._countryService.GetTopCountriesForTopArtists(otherArtists.Content.TopArtists, true);

        var ownWithDiscogs = await this._userService.GetUserWithDiscogs(context.ContextUser.DiscordUserId);
        var otherWithDiscogs = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);
        var willCreateDiscogsPage = ownWithDiscogs.UserDiscogs != null && otherWithDiscogs.UserDiscogs != null;

        var artistPage = new PageBuilder();
        artistPage.WithTitle($"Top artist comparison - {StringExtensions.Sanitize(ownName)} vs {StringExtensions.Sanitize(otherName)}");
        artistPage.WithUrl(url);

        if (tasteSettings.TasteType == TasteType.FullEmbed)
        {
            var taste = this._artistsService.GetEmbedTaste(ownTopArtists, otherTopArtists, amount, timeSettings.TimePeriod);

            artistPage.WithDescription(taste.Description);
            artistPage.AddField("Artist", taste.LeftDescription, true);
            artistPage.AddField("Plays", taste.RightDescription, true);
        }
        else
        {
            var taste = this._artistsService.GetTableTaste(ownTopArtists, otherTopArtists, amount, timeSettings.TimePeriod, ownLastFmUsername, lastfmToCompare, "Artist");

            artistPage.WithDescription(taste.result);
            artistPage.WithFooter("➡️ Genres");
        }

        pages.Add(artistPage);

        if (ownTopGenres.Any() && otherTopGenres.Any())
        {
            var genrePage = new PageBuilder();
            genrePage.WithTitle($"Top genre comparison - {StringExtensions.Sanitize(ownName)} vs {StringExtensions.Sanitize(otherName)}");
            genrePage.WithUrl(url);

            var ownTopGenresTaste =
                ownTopGenres.Select(s => new TasteItem(s.GenreName, s.UserPlaycount.Value)).ToList();
            var otherTopGenresTaste =
                otherTopGenres.Select(s => new TasteItem(s.GenreName, s.UserPlaycount.Value)).ToList();

            var taste = this._artistsService.GetTableTaste(ownTopGenresTaste, otherTopGenresTaste, amount, timeSettings.TimePeriod, ownLastFmUsername, lastfmToCompare, "Genre");

            genrePage.WithDescription(taste.result);
            genrePage.WithFooter("⬅️ Artists\n" +
                                  "➡️ Countries");

            pages.Add(genrePage);
        }

        if (ownTopCountries.Any() && otherTopCountries.Any())
        {
            var countryPage = new PageBuilder();
            countryPage.WithTitle($"Top country comparison - {StringExtensions.Sanitize(ownName)} vs {StringExtensions.Sanitize(otherName)}");
            countryPage.WithUrl(url);

            var ownTopCountriesTaste =
                ownTopCountries.Select(s => new TasteItem(s.CountryName, s.UserPlaycount.Value)).ToList();
            var otherTopCountriesTaste =
                otherTopCountries.Select(s => new TasteItem(s.CountryName, s.UserPlaycount.Value)).ToList();

            var taste = this._artistsService.GetTableTaste(ownTopCountriesTaste, otherTopCountriesTaste, amount, timeSettings.TimePeriod, ownLastFmUsername, lastfmToCompare, "Country");

            countryPage.WithDescription(taste.result);

            if (willCreateDiscogsPage)
            {
                countryPage.WithFooter("⬅️ Genres\n" +
                                        "➡️ Discogs");
            }
            else
            {
                countryPage.WithFooter("⬅️ Genres");
            }

            pages.Add(countryPage);
        }

        if (willCreateDiscogsPage)
        {
            var discogsPage = new PageBuilder();
            discogsPage.WithTitle($"Top Discogs comparison - {StringExtensions.Sanitize(ownName)} vs {StringExtensions.Sanitize(otherName)}");
            discogsPage.WithUrl($"{Constants.DiscogsUserUrl}{otherWithDiscogs.UserDiscogs.Username}/collection");

            var ownReleases = await this._discogsService.GetUserCollection(ownWithDiscogs.UserId);
            var otherReleases = await this._discogsService.GetUserCollection(otherWithDiscogs.UserId);

            var ownTopArtistsTaste = ownReleases.GroupBy(s => s.Release.Artist).Select(g => new TasteItem(g.Key, g.Count())).ToList();
            var otherTopArtistsTaste = otherReleases.GroupBy(s => s.Release.Artist).Select(g => new TasteItem(g.Key, g.Count())).ToList();

            var taste = this._artistsService.GetTableTaste(
                ownTopArtistsTaste,
                otherTopArtistsTaste,
                amount,
                timeSettings.TimePeriod,
                ownWithDiscogs.UserDiscogs.Username,
                otherWithDiscogs.UserDiscogs.Username,
                "Artist");

            discogsPage.WithDescription(taste.result);
            discogsPage.WithFooter($"⬅️ Countries");

            pages.Add(discogsPage);
        }

        if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            await this._smallIndexRepository.UpdateUserArtists(context.ContextUser, ownArtists.Content.TopArtists);
        }

        response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);
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

        var guildTopAllTimeTask = this._whoKnowsArtistService.GetAllTimeTopArtistForGuild(guild.GuildId, largeGuild, bypassCache);
        var guildTopQuarterlyTask = this._whoKnowsArtistService.GetQuarterlyTopArtistForGuild(guild.GuildId, largeGuild, bypassCache);

        var guildTopAllTime = await guildTopAllTimeTask;
        var guildTopQuarterly = await guildTopQuarterlyTask;

        var ownAllTime = guildTopAllTime.Where(w => w.UserId == userSettings.UserId).ToList();
        var ownQuarterly = guildTopQuarterly.Where(w => w.UserId == userSettings.UserId).ToList();

        var concurrentNeighbors = await this._whoKnowsArtistService.GetAffinity(guildTopAllTime, ownAllTime, guildTopQuarterly, ownQuarterly);

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
            pageFooter.Append($"Page {pageCounter}/{neighborPages.Count} - {filteredUserIds.Count} .fmbot members in this server");

            pages.Add(new PageBuilder()
                .WithTitle($"Server neighbors for {userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}")
                .WithDescription(pageString.ToString())
                .WithFooter(pageFooter.ToString()));
            pageCounter++;
        }

        if (!neighborPages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("Could not find users with a similar music taste."));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    private string CalculateAffinityPercentage(double neighborPoints, double ownPoints, int multiplier = 2)
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
}
