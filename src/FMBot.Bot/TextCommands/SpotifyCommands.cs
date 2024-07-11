using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using SpotifyAPI.Web;

namespace FMBot.Bot.TextCommands;

[Name("Spotify")]
public class SpotifyCommands : BaseCommandModule
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SpotifyService _spotifyService;

    private readonly UserService _userService;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public SpotifyCommands(
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        SpotifyService spotifyService,
        IOptions<BotSettings> botSettings,
        InteractiveService interactivity) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this._spotifyService = spotifyService;
        this.Interactivity = interactivity;
        this._dataSourceFactory = dataSourceFactory;
    }

    [Command("spotify")]
    [Summary("Shares a link to a Spotify track based on what a user is listening to or searching for")]
    [Alias("sp", "s", "spotifyfind", "spotifysearch", "alexa play", "hey siri", "hey google", "ok google")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (searchValue != null && searchValue.StartsWith("play ", StringComparison.OrdinalIgnoreCase))
            {
                searchValue = searchValue.Replace("play ", "", StringComparison.OrdinalIgnoreCase);
            }

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await this._userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

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

                var recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
                artistName = currentTrack.ArtistName;
            }

            var item = await this._spotifyService.GetSearchResultAsync(querystring);

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
                if (rnd.Next(0, 2) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "spotify"))
                {
                    response.Text += $"\n-# *Tip: Search for other songs by simply adding the searchvalue behind {prfx}spotify.*";
                    response.HintShown = true;
                }

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, track.Artists.First().Name);
                PublicProperties.UsedCommandsTracks.TryAdd(this.Context.Message.Id, track.Name);
            }
            else
            {

                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("spotifyalbum")]
    [Summary("Shares a link to a Spotify album based on what a user is listening to or searching for")]
    [Alias("spab")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyAlbumAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await this._userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

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

                var recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];

                querystring = $"{currentTrack.ArtistName} {currentTrack.AlbumName}";
            }

            var item = await this._spotifyService.GetSearchResultAsync(querystring, SearchRequest.Types.Album);

            var response = new ResponseModel
            {
                ResponseType = ResponseType.Text
            };

            if (item.Albums?.Items?.Any() == true)
            {
                var album = item.Albums.Items.FirstOrDefault();
                response.Text = $"https://open.spotify.com/album/{album.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "spotifyalbum"))
                {
                    response.Text += $"\n-# *Tip: Search for other albums by simply adding the searchvalue behind `{prfx}spotifyalbum` (or `.fmspab`).*";
                    response.HintShown = true;
                }

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, album.Artists.First().Name);
                PublicProperties.UsedCommandsAlbums.TryAdd(this.Context.Message.Id, album.Name);
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("spotifyartist")]
    [Summary("Shares a link to a Spotify artist based on what a user is listening to or searching for")]
    [Alias("spa")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyArtistAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (this.Context.Message.ReferencedMessage != null && string.IsNullOrWhiteSpace(searchValue))
            {
                var internalLookup = CommandContextExtensions.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id)
                                     ??
                                     await this._userService.GetReferencedMusic(this.Context.Message.ReferencedMessage.Id);

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

                var recentScrobbles = await this._dataSourceFactory.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                {
                    return;
                }

                var currentTrack = recentScrobbles.Content.RecentTracks[0];


                querystring = $"{currentTrack.ArtistName}";
            }

            var item = await this._spotifyService.GetSearchResultAsync(querystring, SearchRequest.Types.Artist);

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
                if (rnd.Next(0, 8) == 1 && string.IsNullOrWhiteSpace(searchValue) && !await this._userService.HintShownBefore(userSettings.UserId, "spotifyartist"))
                {
                    response.Text += $"\n-# *Tip: Search for other artists by simply adding the searchvalue behind `{prfx}spotifyartist` (or `{prfx}spa`).*";
                    response.HintShown = true;
                }

                PublicProperties.UsedCommandsArtists.TryAdd(this.Context.Message.Id, artist.Name);
            }
            else
            {
                response.Text = $"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.";
                response.CommandResponse = CommandResponse.NotFound;
            }

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
