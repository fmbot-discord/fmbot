using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using SpotifyAPI.Web;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class SpotifySlashCommands(
    InteractiveService interactivity,
    SpotifyService spotifyService,
    UserService userService,
    IDataSourceFactory dataSourceFactory)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    public enum SpotifySearch
    {
        Track = 1,
        Album = 2,
        Artist = 3,
        Playlist = 4
    }

    [SlashCommand("spotify", "Search through Spotify",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task SpotifyAsync(
        [SlashCommandParameter(Name = "search", Description = "Search value")]
        string searchValue = null,
        [SlashCommandParameter(Name = "type",
            Description = "What you want to search for on Spotify (defaults to track)")]
        SpotifySearch type = SpotifySearch.Track,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        await RespondAsync(InteractionCallback.DeferredMessage());

        try
        {
            string reply;
            string querystring;

            RecentTrack currentTrack = null;
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

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(contextUser.UserNameLastFM, 1,
                    useCache: true, sessionKey: sessionKey);

                if (GenericEmbedService.RecentScrobbleCallFailed(recentScrobbles))
                {
                    var errorResponse =
                        GenericEmbedService.RecentScrobbleCallFailedResponse(recentScrobbles,
                            contextUser.UserNameLastFM);

                    await this.Context.SendFollowUpResponse(this.Interactivity, errorResponse, userService);
                    await this.Context.LogCommandUsedAsync(errorResponse, userService);
                    return;
                }

                currentTrack = recentScrobbles.Content.RecentTracks[0];

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

            var item = await spotifyService.GetSearchResultAsync(querystring, spotifySearchType);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            switch (type)
            {
                case SpotifySearch.Album:
                    var album = item.Albums.Items?.FirstOrDefault();
                    if (album != null)
                    {
                        response.Text = reply + $"https://open.spotify.com/album/{album.Id}";
                        response.ReferencedMusic = new ReferencedMusic
                        {
                            Artist = album.Artists.FirstOrDefault()?.Name,
                            Album = album.Name
                        };
                    }

                    break;
                case SpotifySearch.Artist:
                    var artist = item.Artists.Items?.FirstOrDefault();
                    if (artist != null)
                    {
                        response.Text = reply + $"https://open.spotify.com/artist/{artist.Id}";
                        response.ReferencedMusic = new ReferencedMusic
                        {
                            Artist = artist.Name
                        };
                    }

                    break;
                case SpotifySearch.Playlist:
                    var playlist = item.Playlists.Items?.FirstOrDefault();
                    if (playlist != null)
                    {
                        response.Text = reply + $"https://open.spotify.com/playlist/{playlist.Id}";
                    }

                    break;
                case SpotifySearch.Track:
                    FullTrack track;
                    if (currentTrack?.ArtistName != null)
                    {
                        track = item.Tracks.Items?.FirstOrDefault(f => f.Artists.Any(a =>
                            string.Equals(a.Name, currentTrack.ArtistName, StringComparison.OrdinalIgnoreCase)));
                    }
                    else
                    {
                        track = item.Tracks.Items?.FirstOrDefault();
                    }

                    if (track != null)
                    {
                        response.Text = reply + $"https://open.spotify.com/track/{track.Id}";
                        response.ReferencedMusic = new ReferencedMusic
                        {
                            Artist = track.Artists.FirstOrDefault()?.Name,
                            Track = track.Name
                        };
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }

            if (!string.IsNullOrEmpty(response.Text))
            {
                await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, privateResponse);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
                await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, true);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
