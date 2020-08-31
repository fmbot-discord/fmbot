using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.LastFM
{
    public class UserCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly FriendsService _friendsService;
        private readonly GuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly Logger.Logger _logger;
        private readonly TimerService _timer;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        private readonly IIndexService _indexService;


        public UserCommands(TimerService timer,
            Logger.Logger logger,
            IPrefixService prefixService,
            ILastfmApi lastfmApi,
            GuildService guildService,
            LastFMService lastFmService,
            IIndexService indexService)
        {
            this._timer = timer;
            this._logger = logger;
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._friendsService = new FriendsService();
            this._userService = new UserService();
            this._lastFmService = lastFmService;
            this._indexService = indexService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("stats", RunMode = RunMode.Async)]
        [Summary("Displays user stats related to Last.FM and FMBot")]
        public async Task StatsAsync(string user = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.Bot.Prefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
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
                var userAvatar = userImages?.Large?.AbsoluteUri;

                if (!string.IsNullOrWhiteSpace(userAvatar))
                {
                    this._embed.WithThumbnailUrl(userAvatar);
                }

                this._embed.AddField("Last.FM Name", lastFMUserName, true);
                this._embed.AddField("User Type", userInfo.Content.Type, true);
                this._embed.AddField("Total scrobbles", userInfo.Content.Playcount, true);
                this._embed.AddField("Country", userInfo.Content.Country, true);
                this._embed.AddField("Is subscriber?", userInfo.Content.IsSubscriber.ToString(), true);
                this._embed.AddField("Bot Chart Mode", userSettings.FmEmbedType, true);
                this._embed.AddField("Bot user type", userSettings.UserType, true);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show your stats on Last.FM due to an internal error. Try setting a Last.FM name with the 'fmset' command, scrobbling something, and then use the command again.");
            }
        }

        [Command("featured", RunMode = RunMode.Async)]
        [Summary("Displays the featured avatar.")]
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task FeaturedAsync()
        {
            try
            {
                var selfUser = this.Context.Client.CurrentUser;
                this._embed.WithThumbnailUrl(selfUser.GetAvatarUrl());
                this._embed.AddField("Featured:", this._timer.GetTrackString());

                if (PublicProperties.IssuesAtLastFM)
                {
                    this._embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
                }


                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show the featured avatar on FMBot due to an internal error. \n" +
                    "The bot might not have changed its avatar since its last startup. Please wait until a new featured user is chosen.");
            }
        }

        [Command("set", RunMode = RunMode.Async)]
        [Summary(
            "Sets your Last.FM name and FM mode. Please note that users in shared servers will be able to see and request your Last.FM username.")]
        [Alias("setname", "setmode", "fm set")]
        public async Task SetAsync([Summary("Your Last.FM name")] string lastFMUserName = null,
            params string[] otherSettings)
        {
            var prfx = ConfigData.Data.Bot.Prefix;

            var existingUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (lastFMUserName == null || lastFMUserName == "help")
            {
                var replyString = $"{prfx}set is the command you use to set your last.fm username in the bot, so it knows who you are on the last.fm website. \n" +
                                  "Don't have a last.fm account yet? Register here: https://www.last.fm/join \n \n" +
                                  "Sets your username, mode and playcount for the `.fm` command:\n \n" +
                                  $"`{prfx}set 'Last.FM Username' 'embedmini/embedfull/textmini/textfull' 'artist/album/track'` \n \n";

                if (existingUserSettings?.UserNameLastFM != null)
                {
                    var differentMode = existingUserSettings.FmEmbedType == FmEmbedType.embedmini ? "embedfull" : "embedmini";
                    replyString += "Example of picking a different mode: \n" +
                                   $"`{prfx}set {existingUserSettings.UserNameLastFM} {differentMode} album`";
                }
                else
                {
                    replyString += "Example of picking a mode and playcount: \n" +
                                   $"`{prfx}set lastfmusername embedfull artist`\n" +
                                   $"*Replace `lastfmusername` with your own last.fm username*";
                }

                this._embed.WithTitle("Changing your .fmbot settings");
                this._embed.WithUrl($"{Constants.DocsUrl}/commands/");
                this._embed.WithDescription(replyString);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            lastFMUserName = lastFMUserName.Replace("'", "");
            if (!await this._lastFmService.LastFMUserExistsAsync(lastFMUserName))
            {
                await ReplyAsync("LastFM user could not be found. Please check if the name you entered is correct.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (lastFMUserName == "lastfmusername")
            {
                await ReplyAsync("Please enter your own last.fm username and not `lastfmusername`.\n");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userSettingsToAdd = new User
            {
                UserNameLastFM = lastFMUserName
            };

            userSettingsToAdd = this._userService.SetSettings(userSettingsToAdd, otherSettings);

            this._userService.SetLastFM(this.Context.User, userSettingsToAdd);

            var setReply = $"Your Last.FM name has been set to '{lastFMUserName}' and your .fm mode to '{userSettingsToAdd.FmEmbedType}'";
            if (userSettingsToAdd.FmCountType != null)
            {
                setReply += $" with the '{userSettingsToAdd.FmCountType.ToString().ToLower()}' playcount.";
            }

            if (otherSettings.Length < 1)
            {
                setReply += $". \nWant more info about the different modes? Use `{prfx}set help`";
            }

            await ReplyAsync(setReply);

            this.Context.LogCommandUsed();

            var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            if (existingUserSettings == null ||
                existingUserSettings.UserNameLastFM.ToLower() != newUserSettings.UserNameLastFM.ToLower())
            {
                await this._indexService.IndexUser(newUserSettings);
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                await this._indexService.AddUserToGuild(Context.Guild, newUserSettings);

                var perms = await this._guildService.CheckSufficientPermissionsAsync(this.Context);
                if (!perms.EmbedLinks || !perms.AttachFiles)
                {
                    await ReplyAsync(
                        "Please note that the bot also needs the 'Attach files' and 'Embed links' permissions for most commands. One or both of these permissions are currently missing.");
                }
            }
        }

        [Command("remove", RunMode = RunMode.Async)]
        [Summary("Deletes your FMBot data.")]
        [Alias("delete", "removedata", "deletedata")]
        public async Task RemoveAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (userSettings == null)
            {
                await ReplyAsync("Sorry, but we don't have any data from you in our database.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            await this._friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserId);
            await this._userService.DeleteUser(userSettings.UserId);

            await ReplyAsync("Your settings, friends and any other data have been successfully deleted.");
            this.Context.LogCommandUsed();
        }

        [Command("suggest", RunMode = RunMode.Async)]
        [Summary("Suggest features you want to see in the bot, or report inappropriate images.")]
        [Alias("report", "suggestion", "suggestions")]
        public async Task Suggest(string suggestion = null)
        {
            try
            {
                /*
                if (string.IsNullOrWhiteSpace(suggestion))
                {
                    await ReplyAsync(cfgjson.Prefix + "fmsuggest 'text in quotes'");
                    return;
                }
                else
                {
                */




                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName(this.Context.User.ToString());
                this._embed.WithAuthor(this._embedAuthor);

                this._embed.WithTitle(this.Context.User.Username + "'s suggestion:");
                this._embed.WithDescription(suggestion);
                this._embed.WithTimestamp(DateTimeOffset.UtcNow);

                var BroadcastServerID = ConfigData.Data.Bot.BaseServerId;
                var BroadcastChannelID = ConfigData.Data.Bot.SuggestionChannelId;

                var guild = await this.Context.Client.GetGuildAsync(BroadcastServerID);
                var channel = await guild.GetChannelAsync(BroadcastChannelID);



                await ReplyAsync("Your suggestion has been sent to the .fmbot server!");
                this.Context.LogCommandUsed();

                //}
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
            }
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
