using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using SpotifyAPI.Web;

namespace FMBot.Bot.SlashCommands;

public class SpotifySlashCommands : InteractionModuleBase
{
    private readonly SpotifyService _spotifyService;
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;

    private InteractiveService Interactivity { get; }

    public SpotifySlashCommands(InteractiveService interactivity, SpotifyService spotifyService, UserService userService, IDataSourceFactory dataSourceFactory)
    {
        this.Interactivity = interactivity;
        this._spotifyService = spotifyService;
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
    }

    public enum SpotifySearch
    {
        Track = 1,
        Album = 2,
        Artist = 3,
        Playlist = 4
    }

    [SlashCommand("spotify", "Search through Spotify.")]
    [UsernameSetRequired]
    [CommandContextType(InteractionContextType.BotDm, InteractionContextType.PrivateChannel, InteractionContextType.Guild)]
    [IntegrationType(ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall)]
    public async Task SpotifyAsync(
        [Summary("Search", "Search value")] string searchValue = null,
        [Summary("Type", "What you want to search for on Spotify (defaults to track)")] SpotifySearch type = SpotifySearch.Track,
        [Summary("Private", "Only show response to you")] bool privateResponse = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            string reply;
            string querystring;

            if (searchValue != null)
            {
                reply = $"Results for *`{StringExtensions.Sanitize(searchValue)}`*\n";
                querystring = searchValue;
            }
            else
            {
                reply = $"";
                string sessionKey = null;
                if (!string.IsNullOrEmpty(contextUser.SessionKeyLastFm))
                {
                    sessionKey = contextUser.SessionKeyLastFm;
                }

                var recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(contextUser.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
                {
                    var errorResponse = GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles, contextUser.UserNameLastFM);

                    await this.Context.SendResponse(this.Interactivity, errorResponse);
                    this.Context.LogCommandUsed(errorResponse.CommandResponse);
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                switch (type)
                {
                    case SpotifySearch.Album:
                        querystring = $"{currentTrack.ArtistName} {currentTrack.AlbumName}";
                        break;
                    case SpotifySearch.Artist:
                    case SpotifySearch.Playlist:
                        querystring = $"{currentTrack.ArtistName}";
                        break;
                    case SpotifySearch.Track:
                        querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }
            }

            var spotifySearchType = type switch
            {
                SpotifySearch.Track => SearchRequest.Types.Track,
                SpotifySearch.Album => SearchRequest.Types.Album,
                SpotifySearch.Artist => SearchRequest.Types.Artist,
                SpotifySearch.Playlist => SearchRequest.Types.Playlist,
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };

            var item = await this._spotifyService.GetSearchResultAsync(querystring, spotifySearchType);

            var result = false;
            switch (type)
            {
                case SpotifySearch.Album:
                    var album = item.Albums.Items?.FirstOrDefault();
                    if (album != null)
                    {
                        reply += $"https://open.spotify.com/album/{album.Id}";
                        result = true;
                    }
                    break;
                case SpotifySearch.Artist:
                    var artist = item.Artists.Items?.FirstOrDefault();
                    if (artist != null)
                    {
                        reply += $"https://open.spotify.com/artist/{artist.Id}";
                        result = true;
                    }
                    break;
                case SpotifySearch.Playlist:
                    var playlist = item.Playlists.Items?.FirstOrDefault();
                    if (playlist != null)
                    {
                        reply += $"https://open.spotify.com/playlist/{playlist.Id}";
                        result = true;
                    }
                    break;
                case SpotifySearch.Track:
                    var track = item.Tracks.Items?.FirstOrDefault();
                    if (track != null)
                    {
                        reply += $"https://open.spotify.com/track/{track.Id}";
                        result = true;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            if (result)
            {
                await RespondAsync(reply, allowedMentions: AllowedMentions.None, ephemeral: privateResponse);
                this.Context.LogCommandUsed();
            }
            else
            {
                await RespondAsync($"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.", allowedMentions: AllowedMentions.None, ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
