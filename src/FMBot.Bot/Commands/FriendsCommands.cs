using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands
{
    public class FriendsCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        private readonly FriendsService _friendsService = new FriendsService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;
        private readonly UserService _userService = new UserService();

        private readonly IPrefixService _prefixService;

        public FriendsCommands(Logger.Logger logger, IPrefixService prefixService)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("friends", RunMode = RunMode.Async)]
        [Summary("Displays a user's friends and what they are listening to.")]
        [Alias("recentfriends", "friendsrecent")]
        public async Task FriendsAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            try
            {
                var friends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                if (friends?.Any() != true)
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                     "`.fmaddfriends 'lastfmname/discord name'`");
                    return;
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                var embedFooterText = "Amount of scrobbles of all your friends together: ";
                string embedTitle;
                if (friends.Count > 1)
                {
                    embedTitle = $"Last songs for {friends.Count} friends from ";
                }
                else
                {
                    embedTitle = "Last songs for 1 friend from ";
                    embedFooterText = "Amount of scrobbles from your friend: ";
                }

                embedTitle += await this._userService.GetUserTitleAsync(this.Context);

                this._embedAuthor.WithName(embedTitle);
                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithUrl(Constants.LastFMUserUrl + userSettings.UserNameLastFM);
                this._embed.WithAuthor(this._embedAuthor);

                var totalPlaycount = 0;
                var embedDescription = "";
                await friends.ParallelForEachAsync(async friend =>
                {
                    var tracks = await this._lastFmService.GetRecentScrobblesAsync(friend, 1);

                    string track;
                    string friendTitle = friend;
                    if (!tracks.Success)
                    {
                        track = "Friend could not be retrieved";
                    }
                    else if (tracks?.Any() != true)
                    {
                        track = "No scrobbles found.";
                    }
                    else
                    {
                        var lastTrack = tracks.Content[0];
                        track = LastFMService.TrackToOneLinedString(lastTrack);
                        if (lastTrack.IsNowPlaying == true)
                        {
                            friendTitle += " (Now Playing)";
                        }
                    }

                    embedDescription += $"[{friendTitle}]({Constants.LastFMUserUrl}{friend}) - {track} \n";

                    if (friends.Count <= 5)
                    {
                        var userInfo = await this._lastFmService.GetUserInfoAsync(friend);
                        totalPlaycount += userInfo.Content.Playcount;
                    }
                });

                if (friends.Count <= 5)
                {
                    this._embedFooter.WithText(embedFooterText + totalPlaycount.ToString("0"));
                    this._embed.WithFooter(this._embedFooter);
                }

                this._embed.WithDescription(embedDescription);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);

                await ReplyAsync(
                    "Unable to show friends due to an internal error.");
            }
        }

        [Command("addfriends", RunMode = RunMode.Async)]
        [Summary("Adds your friends' Last.FM names.")]
        [Alias("friendsset", "setfriends", "friendsadd", "addfriend", "setfriend")]
        public async Task AddFriends([Summary("Friend names")] params string[] friends)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Sorry, but adding friends in dms is not supported.");
                return;
            }

            try
            {
                var friendNotFoundList = new List<string>();

                var addedFriendsCount = 0;

                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                if (existingFriends.Count + friends.Length > 10)
                {
                    await ReplyAsync("Sorry, but you can't have more then 10 friends.");
                    return;
                }

                foreach (var friend in friends)
                {
                    if (!existingFriends.Select(s => s.ToLower()).Contains(friend.ToLower()))
                    {
                        var friendUser = await this._guildService.FindUserFromGuildAsync(this.Context, friend);

                        if (friendUser != null)
                        {
                            var friendUserSettings = await this._userService.GetUserSettingsAsync(friendUser);

                            if (friendUserSettings == null || friendUserSettings.UserNameLastFM == null)
                            {
                                if (await this._lastFmService.LastFMUserExistsAsync(friend))
                                {
                                    await this._friendsService.AddLastFMFriendAsync(this.Context.User.Id,
                                        friend);
                                    addedFriendsCount++;
                                }
                                else
                                {
                                    friendNotFoundList.Add(friend);
                                }
                            }
                            else
                            {
                                await this._friendsService.AddDiscordFriendAsync(this.Context.User.Id, friendUser.Id);
                                addedFriendsCount++;
                            }
                        }
                        else
                        {
                            if (await this._lastFmService.LastFMUserExistsAsync(friend))
                            {
                                await this._friendsService.AddLastFMFriendAsync(this.Context.User.Id,
                                    friend);
                                addedFriendsCount++;
                            }
                            else
                            {
                                friendNotFoundList.Add(friend);
                            }
                        }
                    }
                }

                if (addedFriendsCount > 1)
                {
                    await ReplyAsync("Succesfully added " + addedFriendsCount + " friends.");
                }
                else if (addedFriendsCount == 1)
                {
                    await ReplyAsync("Succesfully added a friend.");
                }

                if (friendNotFoundList.Count > 0)
                {
                    if (friendNotFoundList.Count > 1)
                    {
                        await ReplyAsync("Could not find " + friendNotFoundList.Count +
                                         " friends. Please ensure that you spelled their names correctly.");
                    }
                    else
                    {
                        await ReplyAsync("Could not find 1 friend. Please ensure that you spelled the name correctly.");
                    }
                }

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);

                var friendcount = friends.Length;

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to add " + friendcount + " friends due to an internal error.");
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error.");
                }
            }
        }

        [Command("removefriends", RunMode = RunMode.Async)]
        [Summary("Remove your friends' Last.FM names.")]
        [Alias("friendsremove", "deletefriend", "deletefriends", "removefriend")]
        public async Task RemoveFriends([Summary("Friend names")] params string[] friends)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            if (friends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to remove. Please use their last.fm usernames.");
                return;
            }

            var removedfriendcount = 0;

            try
            {
                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                foreach (var friend in friends)
                {
                    if (existingFriends.Select(s => s.ToLower()).Contains(friend.ToLower()))
                    {
                        await this._friendsService.RemoveLastFMFriendAsync(userSettings.UserId, friend);
                        removedfriendcount++;
                    }
                }

                if (removedfriendcount > 1)
                {
                    await ReplyAsync("Succesfully removed " + removedfriendcount + " friends.");
                }
                else if (removedfriendcount < 1)
                {
                    await ReplyAsync("Couldn't remove " + removedfriendcount +
                                     " friends. Please check if you spelled that all Last.FM names correct.");
                }
                else
                {
                    await ReplyAsync("Succesfully removed a friend.");
                }

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);

                if (friends.Length > 1)
                {
                    await ReplyAsync("Unable to remove " + friends.Length +
                                     " friends due to an internal error. Did you add anyone?");
                }
                else
                {
                    await ReplyAsync("Unable to remove a friend due to an internal error. Did you add anyone?");
                }
            }
        }

        [Command("removeallfriends", RunMode = RunMode.Async)]
        [Summary("Remove all your friends")]
        [Alias("friendsremoveall")]
        public async Task RemoveAllFriends()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (userSettings?.UserNameLastFM == null)
            {
                this._embed.UsernameNotSetErrorResponse(this.Context, prfx, this._logger);
                await ReplyAsync("", false, this._embed.Build());
                return;
            }

            try
            {
                await this._friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserId);

                await ReplyAsync("Removed all your friends.");
                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);

                await ReplyAsync("Unable to remove all friends due to an internal error.");
            }
        }
    }
}
