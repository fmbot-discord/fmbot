using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using SpotifyAPI.Web;

namespace FMBot.Bot.SlashCommands;

public class SpotifySlashCommands : InteractionModuleBase
{
    private readonly SpotifyService _spotifyService;
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly ImportService _importService;
    private readonly PlayService _playService;

    private InteractiveService Interactivity { get; }

    public SpotifySlashCommands(InteractiveService interactivity, SpotifyService spotifyService, UserService userService, IDataSourceFactory dataSourceFactory, ImportService importService, PlayService playService)
    {
        this.Interactivity = interactivity;
        this._spotifyService = spotifyService;
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
        this._importService = importService;
        this._playService = playService;
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
                    var response = new ResponseModel
                    {
                        ResponseType = ResponseType.Embed,
                        Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(recentScrobbles, contextUser.UserNameLastFM)
                    };

                    await this.Context.SendResponse(this.Interactivity, response);
                    this.Context.LogCommandUsed(response.CommandResponse);
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

    [SlashCommand("import", "Import your Spotify history")]
    [UsernameSetRequired]
    public async Task SpotifyAsync(
        [Summary("file-1", "Spotify endsong.json file")] IAttachment attachment1,
        [Summary("file-2", "Spotify endsong.json file")] IAttachment attachment2 = null,
        [Summary("file-3", "Spotify endsong.json file")] IAttachment attachment3 = null,
        [Summary("file-4", "Spotify endsong.json file")] IAttachment attachment4 = null,
        [Summary("file-5", "Spotify endsong.json file")] IAttachment attachment5 = null,
        [Summary("file-6", "Spotify endsong.json file")] IAttachment attachment6 = null,
        [Summary("file-7", "Spotify endsong.json file")] IAttachment attachment7 = null,
        [Summary("file-8", "Spotify endsong.json file")] IAttachment attachment8 = null,
        [Summary("file-9", "Spotify endsong.json file")] IAttachment attachment9 = null,
        [Summary("file-10", "Spotify endsong.json file")] IAttachment attachment10 = null,
        [Summary("file-11", "Spotify endsong.json file")] IAttachment attachment11 = null,
        [Summary("file-12", "Spotify endsong.json file")] IAttachment attachment12 = null,
        [Summary("file-13", "Spotify endsong.json file")] IAttachment attachment13 = null,
        [Summary("file-14", "Spotify endsong.json file")] IAttachment attachment14 = null,
        [Summary("file-15", "Spotify endsong.json file")] IAttachment attachment15 = null,
        [Summary("file-16", "Spotify endsong.json file")] IAttachment attachment16 = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (contextUser.UserType != UserType.Admin && contextUser.UserType != UserType.Owner)
        {
            await RespondAsync("Not available yet!");
            return;
        }

        var attachments = new List<IAttachment>
        {
            attachment1, attachment2, attachment3, attachment4, attachment5, attachment6,
            attachment7, attachment8, attachment9, attachment10, attachment11, attachment12,
            attachment13, attachment14, attachment15, attachment16
        };

        await DeferAsync();

        try
        {
            var embed = new EmbedBuilder();
            var description = new StringBuilder();

            embed.WithTitle("Importing Spotify... (Beta)");
            embed.WithColor(DiscordConstants.SpotifyColorGreen);
            embed.WithDescription("- <a:loading:821676038102056991> Loading import files...");
            var message = await FollowupAsync(embed: embed.Build());

            var imports = await this._importService.HandleSpotifyFiles(attachments);

            if (!imports.success)
            {
                embed.WithColor(DiscordConstants.WarningColorOrange);
                await UpdateImportEmbed(message, embed, description, $"- ❌ Invalid Spotify import file. Make sure you select the right files, for example `endsong_1.json`.", true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            else
            {
                await UpdateImportEmbed(message, embed, description, $"- **{imports.result.Count}** Spotify imports found");
            }

            var plays = await this._importService.SpotifyImportToUserPlays(contextUser.UserId, imports.result);
            await UpdateImportEmbed(message, embed, description, $"- **{plays.Count}** actual plays found");

            var playsWithoutDuplicates =
                await this._importService.RemoveDuplicateSpotifyImports(contextUser.UserId, plays);
            await UpdateImportEmbed(message, embed, description, $"- **{playsWithoutDuplicates.Count}** after filtering already imported");

            if (playsWithoutDuplicates.Count > 0)
            {
                await this._importService.InsertImportPlays(playsWithoutDuplicates);
                await UpdateImportEmbed(message, embed, description, $"- Added plays to database");
            }

            await this._importService.UpdateExistingPlays(contextUser.UserId);

            var files = new StringBuilder();
            foreach (var attachment in attachments
                         .Where(w => w != null)
                         .OrderBy(o => o.Filename))
            {
                files.AppendLine($"`{attachment.Filename}`");
            }

            embed.AddField("Processed files", files.ToString());

            var years = new StringBuilder();
            var allPlays = await this._playService
                .GetAllUserPlays(contextUser.UserId);

            var yearGroups = allPlays
                .Where(w => w.PlaySource == PlaySource.SpotifyImport)
                .OrderByDescending(o => o.TimePlayed)
                .GroupBy(g => g.TimePlayed.Year);

            foreach (var year in yearGroups)
            {
                years.AppendLine(
                    $"**`{year.Key}`** " +
                    $"- **{year.Count()}** plays");
            }
            if (years.Length > 0)
            {
                embed.AddField("Total imported plays", years.ToString());
            }

            await UpdateImportEmbed(message, embed, description, $"- ✅ Import complete", true);
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private static async Task UpdateImportEmbed(IUserMessage msg, EmbedBuilder embed, StringBuilder builder, string lineToAdd, bool lastLine = false)
    {
        const string loadingLine = "- <a:loading:821676038102056991> Processing...";
        builder.Replace($"\r\n{loadingLine}", "");
        builder.AppendLine(lineToAdd);

        if (!lastLine)
        {
            builder.AppendLine(loadingLine);
        }

        embed.WithDescription(builder.ToString());

        await msg.ModifyAsync(m =>
        {
            m.Embed = embed.Build();
        });
    }
}
