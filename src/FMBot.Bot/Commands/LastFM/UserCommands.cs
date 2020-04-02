using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.DatabaseModels;

namespace FMBot.Bot.Commands.LastFM
{
    public class UserCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly FriendsService _friendsService = new FriendsService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly TimerService _timer;

        private readonly UserService _userService = new UserService();

        public UserCommands(TimerService timer, Logger.Logger logger)
        {
            this._timer = timer;
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmstats", RunMode = RunMode.Async)]
        [Summary("Displays user stats related to Last.FM and FMBot")]
        public async Task StatsAsync(string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            try
            {
                var lastFMUserName = userSettings.UserNameLastFM;
                var self = true;

                if (user != null)
                {
                    var alternativeLastFmUserName = await FindUser(user);
                    if (!string.IsNullOrEmpty(alternativeLastFmUserName))
                    {
                        lastFMUserName = alternativeLastFmUserName;
                        self = false;
                    }
                }

                string userTitle;
                if (self)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{lastFMUserName}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName("Last.FM & fmbot user data for " + userTitle);
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithUrl(Constants.LastFMUserUrl + lastFMUserName);
                this._embed.WithTitle("Click here to go to profile");

                this._embedFooter.WithText(
                    "To see info for other users, use .fmstats 'Discord username/ Last.FM username'");
                this._embed.WithFooter(this._embedFooter);

                var userInfo = await this._lastFmService.GetUserInfoAsync(lastFMUserName);

                var userImages = userInfo.Content.Avatar;
                var userAvatar = userImages?.Large.AbsoluteUri;

                if (!string.IsNullOrWhiteSpace(userAvatar))
                {
                    this._embed.WithThumbnailUrl(userAvatar);
                }

                this._embed.AddField("Last.FM Name", lastFMUserName, true);
                this._embed.AddField("User Type", userInfo.Content.Type, true);
                this._embed.AddField("Total scrobbles", userInfo.Content.Playcount, true);
                this._embed.AddField("Country", userInfo.Content.Country, true);
                this._embed.AddField("Is subscriber?", userInfo.Content.IsSubscriber.ToString(), true);
                this._embed.AddField("Bot Chart Mode", userSettings.ChartType, true);
                this._embed.AddField("Bot user type", userSettings.UserType, true);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("fmfeatured", RunMode = RunMode.Async)]
        [Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task FeaturedAsync()
        {
            try
            {
                var selfUser = this.Context.Client.CurrentUser;
                this._embed.WithThumbnailUrl(selfUser.GetAvatarUrl());
                this._embed.AddField("Featured:", this._timer.GetTrackString());

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Unable to show the featured avatar on FMBot due to an internal error. \n" +
                    "The bot might not have changed its avatar since its last startup. Please wait until a new featured user is chosen.");
            }
        }

        [Command("fmset", RunMode = RunMode.Async)]
        [Summary(
            "Sets your Last.FM name and FM mode. Please note that users in shared servers will be able to see and request your Last.FM username.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task SetAsync([Summary("Your Last.FM name")] string lastFMUserName = null,
            [Summary("The mode you want to use.")] string chartType = null)
        {
            var prfx = ConfigData.Data.CommandPrefix;

            if (lastFMUserName == null || lastFMUserName == "help")
            {
                var replyString = ".fmset is the command you use to set your last.fm username in the bot, so it knows who you are on the last.fm website. \n" +
                                  "Don't have a last.fm account yet? Register here: https://www.last.fm/join \n \n" +
                                  "Sets your username and mode for the `.fm` command:\n \n" +
                                  $"`{prfx}fmset 'Last.FM Username' 'embedmini/embedfull/textmini/textfull'` \n \n";

                var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
                if (userSettings?.UserNameLastFM != null)
                {
                    var differentMode = userSettings.ChartType == ChartType.embedmini ? "embedfull" : "embedmini";
                    replyString += "Example of picking a different mode: \n" +
                                   $"`{prfx}fmset {userSettings.UserNameLastFM} {differentMode}`";
                }
                else
                {
                    replyString += "Example of picking a mode: \n" +
                                   $"`{prfx}fmset lastfmusername embedfull`";
                }

                this._embed.WithTitle("Changing your .fmbot settings");
                this._embed.WithUrl($"{Constants.DocsUrl}/commands/");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            lastFMUserName = lastFMUserName.Replace("'", "");
            if (!await this._lastFmService.LastFMUserExistsAsync(lastFMUserName))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                return;
            }

            var chartTypeEnum = ChartType.embedmini;
            if (chartType != null && !Enum.TryParse(chartType.Replace("'", ""), true, out chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }

            this._userService.SetLastFM(this.Context.User, lastFMUserName, chartTypeEnum);

            var setReply = $"Your Last.FM name has been set to '{lastFMUserName}'";

            if (chartType == null)
            {
                setReply += $" and your .fm mode has been set to '{chartTypeEnum}', which is the default mode. \n" +
                            $"Want more info about the different modes? Use `{prfx}fmset help`";
            }
            else
            {
                setReply += $" and your .fm mode has been set to '{chartTypeEnum}.'";
            }

            await ReplyAsync(setReply);
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync(
                        "Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.");
                }
            }
        }

        [Command("fmremove", RunMode = RunMode.Async)]
        [Summary("Deletes your FMBot data.")]
        [Alias("fmdelete", "fmremovedata", "fmdeletedata")]
        public async Task RemoveAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.");
                return;
            }

            await this._friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserId);
            await this._userService.DeleteUser(userSettings.UserId);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted.");
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                this.Context.Message.Content);
        }

        [Command("fmsuggest", RunMode = RunMode.Async)]
        [Summary("Suggest features you want to see in the bot, or report inappropriate images.")]
        [Alias("fmreport", "fmsuggestion", "fmsuggestions")]
        public async Task Suggest(string suggestion = null)
        {
            try
            {
                /*
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    await ReplyAsync(cfgjson.CommandPrefix + "fmsuggest 'text in quotes'");
                    return;
                }
                else
                {
                */
                var client = this.Context.Client as DiscordSocketClient;

                var BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.SuggestionsChannel);

                var guild = client.GetGuild(BroadcastServerID);
                var channel = guild.GetTextChannel(BroadcastChannelID);

                var builder = new EmbedBuilder();
                var eab = new EmbedAuthorBuilder
                {
                    IconUrl = this.Context.User.GetAvatarUrl(),
                    Name = this.Context.User.Username
                };
                builder.WithAuthor(eab);
                builder.WithTitle(this.Context.User.Username + "'s suggestion:");
                builder.WithDescription(suggestion);

                await channel.SendMessageAsync("", false, builder.Build());

                await ReplyAsync("Your suggestion has been sent to the .fmbot server!");
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);

                //}
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
            await ReplyAsync("", false, this._embed.Build());
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, user);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
