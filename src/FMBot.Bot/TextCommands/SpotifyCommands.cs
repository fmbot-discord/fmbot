using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
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

    public SpotifyCommands(
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        SpotifyService spotifyService,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this._spotifyService = spotifyService;
        this._dataSourceFactory = dataSourceFactory;
    }

    [Command("spotify")]
    [Summary("Shares a link to a Spotify track based on what a user is listening to or searching for")]
    [Alias("sp", "s", "spotifyfind", "spotifysearch")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task SpotifyAsync([Remainder] string searchValue = null)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

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

                querystring = $"{currentTrack.TrackName} {currentTrack.ArtistName} {currentTrack.AlbumName}";
            }

            var item = await this._spotifyService.GetSearchResultAsync(querystring);

            if (item.Tracks?.Items?.Any() == true)
            {
                var track = item.Tracks.Items.FirstOrDefault();
                var reply = $"https://open.spotify.com/track/{track.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                {
                    reply += $"\n*Tip: Search for other songs by simply adding the searchvalue behind {prfx}spotify.*";
                }

                await ReplyAsync(reply, allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync($"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.", allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
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

            if (item.Albums?.Items?.Any() == true)
            {
                var album = item.Albums.Items.FirstOrDefault();
                var reply = $"https://open.spotify.com/album/{album.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                {
                    reply += $"\n*Tip: Search for other albums by simply adding the searchvalue behind `{prfx}spotifyalbum` (or `.fmspab`).*";
                }

                await ReplyAsync(reply);
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync($"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.", allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
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

            if (item.Artists.Items?.Any() == true)
            {
                var artist = item.Artists.Items.OrderByDescending(o => o.Popularity).FirstOrDefault(f => f.Name.ToLower() == querystring.ToLower()) ??
                             item.Artists.Items.OrderByDescending(o => o.Popularity).FirstOrDefault();

                var reply = $"https://open.spotify.com/artist/{artist.Id}";

                var rnd = new Random();
                if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
                {
                    reply += $"\n*Tip: Search for other artists by simply adding the searchvalue behind `{prfx}spotifyartist` (or `{prfx}spa`).*";
                }

                await ReplyAsync(reply);
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync($"Sorry, Spotify returned no results for *`{StringExtensions.Sanitize(querystring)}`*.", allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
