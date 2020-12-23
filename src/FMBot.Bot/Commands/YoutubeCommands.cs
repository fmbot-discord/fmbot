using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    [Name("Youtube")]
    public class YoutubeCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;

        private readonly LastFmService _lastFmService;
        private readonly UserService _userService;
        private readonly YoutubeService _youtubeService;

        private readonly IPrefixService _prefixService;

        public YoutubeCommands(
            IPrefixService prefixService,
            LastFmService lastFmService,
            UserService userService,
            YoutubeService youtubeService)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._youtubeService = youtubeService;
            this._lastFmService = lastFmService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
        }

        [Command("youtube")]
        [Summary("Shares a link to a YouTube video based on what a user is listening to")]
        [Alias("yt", "y", "youtubesearch", "ytsearch", "yts")]
        [UsernameSetRequired]
        public async Task YoutubeAsync([Remainder] string searchValue = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

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

                    var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(userSettings.UserNameLastFM, 1, useCache: true, sessionKey: sessionKey);

                    if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, userSettings.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    var currentTrack = recentScrobbles.Content.RecentTracks.Track[0];
                    querystring = currentTrack.Name + " - " + currentTrack.Artist.Text;
                }

                try
                {
                    var youtubeResult = await this._youtubeService.GetSearchResult(querystring);

                    if (youtubeResult == null)
                    {
                        await ReplyAsync("No results have been found for this query.");
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return;
                    }

                    var name = await this._userService.GetNameAsync(this.Context);

                    var reply = $"{name} searched for: `{querystring}`";

                    var user = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                    if (user.GuildPermissions.EmbedLinks)
                    {
                        reply += $"\nhttps://www.youtube.com/watch?v={youtubeResult.VideoId}";
                    }
                    else
                    {
                        reply += $"\n<https://www.youtube.com/watch?v={youtubeResult.VideoId}>" +
                                 $"\n`{youtubeResult.Title}`" +
                                 $"\n*Embed disabled because user that requested link is not allowed to embed links.*";
                    }

                    var rnd = new Random();
                    if (rnd.Next(0, 7) == 1 && string.IsNullOrWhiteSpace(searchValue))
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
                    "Unable to show Last.fm info via YouTube due to an internal error. Please try again later or contact .fmbot support.");
            }
        }
    }
}
