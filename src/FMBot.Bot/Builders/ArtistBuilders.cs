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
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Enums;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class ArtistBuilders
{
    private readonly ArtistsService _artistsService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;
    private readonly SpotifyService _spotifyService;
    private readonly UserService _userService;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;
    private readonly PlayService _playService;
    private readonly IUpdateService _updateService;

    public ArtistBuilders(ArtistsService artistsService,
        LastFmRepository lastFmRepository,
        GuildService guildService,
        SpotifyService spotifyService,
        UserService userService,
        WhoKnowsArtistService whoKnowsArtistService,
        PlayService playService,
        IUpdateService updateService)
    {
        this._artistsService = artistsService;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
        this._spotifyService = spotifyService;
        this._userService = userService;
        this._whoKnowsArtistService = whoKnowsArtistService;
        this._playService = playService;
        this._updateService = updateService;
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

        var artistSearch = await GetArtist(response, discordUser, searchValue, contextUser.UserNameLastFM, contextUser.SessionKeyLastFm);
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
                    artistInfo.AppendLine($"Born: <t:{dateValue}:D>");
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
                    artistInfo.AppendLine($"Born: <t:{startDateValue}:D>");
                    artistInfo.AppendLine($"Died: <t:{endDateValue}:D>");
                }
                else
                {
                    artistInfo.AppendLine($"Started: <t:{startDateValue}:D>");
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
            var guild = await this._guildService.GetFullGuildAsync(discordGuild.Id);

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
            globalStats += $"\n`{await this._playService.GetWeekArtistPlaycountAsync(contextUser.UserId, artistSearch.artist.ArtistName)}` by you last week";
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

        var artistSearch = await GetArtist(response, discordUser, searchValue, contextUser.UserNameLastFM,
            contextUser.SessionKeyLastFm);
        if (artistSearch.artist == null)
        {
            return artistSearch.response;
        }

        var timeDescription = timeSettings.Description.ToLower();
        List<UserTrack> topTracks;
        switch (timeSettings.TimePeriod)
        {
            case TimePeriod.Weekly:
                topTracks = await this._playService.GetTopTracksForArtist(userSettings.UserId, 7, artistSearch.artist.ArtistName);
                break;
            case TimePeriod.Monthly:
                topTracks = await this._playService.GetTopTracksForArtist(userSettings.UserId, 31, artistSearch.artist.ArtistName);
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
            footer.Append($"Page {pageCounter}/{topTrackPages.Count}");
            var title = new StringBuilder();

            if (userSettings.DifferentUser && userSettings.UserId != contextUser.UserId)
            {
                footer.AppendLine($" - {userSettings.UserNameLastFm} has {artistSearch.artist.UserPlaycount} total scrobbles on this artist");
                footer.AppendLine($"Requested by {userTitle}");
                title.Append($"{userSettings.DiscordUserName} their top tracks for '{artistSearch.artist.ArtistName}'");
            }
            else
            {
                footer.Append($" - {userTitle} has {artistSearch.artist.UserPlaycount} total scrobbles on this artist");
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


    private async Task<(ArtistInfo artist, ResponseModel response)> GetArtist(ResponseModel response, IUser discordUser, string artistValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null)
    {
        if (!string.IsNullOrWhiteSpace(artistValues) && artistValues.Length != 0)
        {
            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistValues, lastFmUserName, otherUserUsername == null ? null : sessionKey);
            if (!artistCall.Success && artistCall.Error == ResponseStatus.MissingParameters)
            {
                response.Embed.WithDescription($"Artist `{artistValues}` could not be found, please check your search values and try again.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return (null, response);
            }
            if (!artistCall.Success || artistCall.Content == null)
            {
                response.Embed.ErrorResponse(artistCall.Error, artistCall.Message, null, discordUser, "artist");
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
                return (null, response);
            }

            return (artistCall.Content, response);
        }
        else
        {
            var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(lastFmUserName, 1, true, sessionKey);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
            {
                response.Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(recentScrobbles, lastFmUserName);
                response.ResponseType = ResponseType.Embed;
                return (null, response);
            }

            if (otherUserUsername != null)
            {
                lastFmUserName = otherUserUsername;
            }

            var lastPlayedTrack = recentScrobbles.Content.RecentTracks[0];
            var artistCall = await this._lastFmRepository.GetArtistInfoAsync(lastPlayedTrack.ArtistName, lastFmUserName);

            if (artistCall.Content == null || !artistCall.Success)
            {
                response.Embed.WithDescription($"Last.fm did not return a result for **{lastPlayedTrack.ArtistName}**.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return (null, response);
            }

            return (artistCall.Content, response);
        }
    }
}
