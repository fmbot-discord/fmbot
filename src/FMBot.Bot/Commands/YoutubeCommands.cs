using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    public class YoutubeCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFMService _lastFmService;
        private readonly UserService _userService;
        private readonly YoutubeService _youtubeService;

        private readonly IPrefixService _prefixService;

        public YoutubeCommands(
            IPrefixService prefixService,
            LastFMService lastFmService,
            UserService userService,
            YoutubeService youtubeService)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._youtubeService = youtubeService;
            this._lastFmService = lastFmService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
        }

        [Command("youtube")]
        [Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("yt", "y", "youtubesearch", "ytsearch", "yts")]
        [UsernameSetRequired]
        public async Task YoutubeAsync(params string[] searchValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                string querystring;
                if (searchValues.Length > 0)
                {
                    querystring = string.Join(" ", searchValues);
                }
                else
                {
                    var tracks = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM);

                    if (tracks?.Any() != true)
                    {
                        this._embed.NoScrobblesFoundErrorResponse(tracks.Status, prfx, userSettings.UserNameLastFM);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    var currentTrack = tracks.Content[0];
                    querystring = currentTrack.Name + " - " + currentTrack.ArtistName;
                }

                try
                {
                    var youtubeResult = await this._youtubeService.GetSearchResult(querystring);

                    var name = await this._userService.GetNameAsync(this.Context);

                    var reply = $"{name} searched for: `{querystring}`";

                    var user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                    if (user.GuildPermissions.EmbedLinks)
                    {
                        reply += $"\nhttps://www.youtube.com/watch?v={youtubeResult.Id.VideoId}";
                    }
                    else
                    {
                        reply += $"\n<https://www.youtube.com/watch?v={youtubeResult.Id.VideoId}>" +
                                 $"\n`{youtubeResult.Snippet.Title}`" +
                                 $"\n*Embed disabled because user that requested link is not allowed to embed links.*";
                    }

                    var rnd = new Random();
                    if (rnd.Next(0, 5) == 1 && searchValues.Length < 1)
                    {
                        reply += $"\n*Tip: Search for other songs or videos by simply adding the searchvalue behind {prfx}youtube.*";
                    }

                    await ReplyAsync(reply.FilterOutMentions());

                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("No results have been found for this query.\n" +
                                     "It could also be that we've currently exceeded the YouTube ratelimits. This is an issue that will be fixed soon.");
                }
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show Last.FM info via YouTube due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }
    }
}
