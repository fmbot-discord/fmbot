using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using SpotifyAPI.Web;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[ModuleName("Spotify")]
public class SpotifyCommands(
    IPrefixService prefixService,
    IDataSourceFactory dataSourceFactory,
    UserService userService,
    SpotifyService spotifyService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;


    [Command("spotify", "sp", "s", "spotifyfind", "spotifysearch")]
    [Summary("Shares a link to a Spotify track based on what a user is listening to or searching for")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (searchValue != null && searchValue.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
            {
                searchValue = searchValue.Replace("play ", "", StringComparison.OrdinalIgnoreCase);
            }

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Track != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Track}";
                }
            }

            string querystring;
            string artistName = null;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                querystring = searchValue;
            }
            else
            {
                string sessionKey = null;
                if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context, userService))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
                artistName = currentTrack.ArtistName;
            }

            var item = await spotifyService.GetSearchResultAsync(querystring);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item.Tracks?.Items?.Any() == true)
            {
                var track = item.Tracks.Items.FirstOrDefault();
                if (artistName != null)
                {
                    var result = item.Tracks.Items.FirstOrDefault(f => f.Artists.Any(a => string.Equals(a.Name, artistName, StringComparison.OrdinalIgnoreCase)));

                    if (result != null)
                    {
                        track = result;
                    }
                }

                response.Text = $"https://open.spotify.com/track/{track.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 2) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await userService.HintShownBefore(userSettings.UserId, "spotify"))
                {
                    response.Text += $"\n-# *Tip: Search for other songs by simply adding the searchvalue behind {prfx}spotify.*";
                    response.HintShown = true;
                }

                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = track.Artists.First().Name,
                    Track = track.Name
                };
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("spotifyalbum", "spab")]
    [Summary("Shares a link to a Spotify album based on what a user is listening to or searching for")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyAlbumAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Album != null)
                {
                    searchValue = $"{internalLookup.Artist} | {internalLookup.Album}";
                }
            }

            string querystring;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                querystring = searchValue;
            }
            else
            {
                string sessionKey = null;
                if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context, userService))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                querystring = $"{currentTrack.ArtistName} {currentTrack.AlbumName}";
            }

            var item = await spotifyService.GetSearchResultAsync(querystring, SearchRequest.Types.Album);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item.Albums?.Items?.Any() == true)
            {
                var album = item.Albums.Items.FirstOrDefault();
                response.Text = $"https://open.spotify.com/album/{album.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await userService.HintShownBefore(userSettings.UserId, "spotifyalbum"))
                {
                    response.Text += $"\n-# *Tip: Search for other albums by simply adding the searchvalue behind `{prfx}spotifyalbum` (or `.fmspab`).*";
                    response.HintShown = true;
                }

                response.ReferencedMusic = new ReferencedMusic
                {
                    Artist = album.Artists.First().Name,
                    Album = album.Name
                };
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("spotifyartist", "spa")]
    [Summary("Shares a link to a Spotify artist based on what a user is listening to or searching for")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyArtistAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

                if (internalLookup?.Artist != null)
                {
                    searchValue = $"{internalLookup.Artist}";
                }
            }

            string querystring;
            if (!string.IsNullOrWhiteSpace(searchValue))
            {
                querystring = searchValue;
            }
            else
            {
                string sessionKey = null;
                if (!string.IsNullOrEmpty(userSettings.SessionKeyLastFm))
                {
                    sessionKey = userSettings.SessionKeyLastFm;
                }

                var recentScrobbles = await dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context, userService))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];


                querystring = $"{currentTrack.ArtistName}";
            }

            var item = await spotifyService.GetSearchResultAsync(querystring, SearchRequest.Types.Artist);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item.Artists.Items?.Any() == true)
            {
                var artist = item.Artists.Items.OrderByDescending(o => o.Popularity).FirstOrDefault(f => f.Name.ToLower() == querystring.ToLower()) ??
                             item.Artists.Items.OrderByDescending(o => o.Popularity).FirstOrDefault();

                response.Text = $"https://open.spotify.com/artist/{artist.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await userService.HintShownBefore(userSettings.UserId, "spotifyartist"))
                {
                    response.Text += $"\n-# *Tip: Search for other artists by simply adding the searchvalue behind `{prfx}spotifyartist` (or `{prfx}spa`).*";
                    response.HintShown = true;
                }

                response.ReferencedMusic = new ReferencedMusic { Artist = artist.Name };
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
