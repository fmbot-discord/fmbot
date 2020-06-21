using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.LastFM.Services;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Commands
{
    public class FriendsCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        private readonly FriendsService _friendsService;
        private readonly IGuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly Logger.Logger _logger;
        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public FriendsCommands(Logger.Logger logger,
            IPrefixService prefixService,
            ILastfmApi lastfmApi,
            IGuildService guildService)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._userService = new UserService();
            this._friendsService = new FriendsService();
            this._lastFmService = new LastFMService(lastfmApi);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("friends", RunMode = RunMode.Async)]
        [Summary("Displays a user's friends and what they are listening to.")]
        [Alias("recentfriends", "friendsrecent")]
        [LoginRequired]
        public async Task FriendsAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

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
        [LoginRequired]
        public async Task AddFriends([Summary("Friend names")] params string[] friends)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Sorry, but adding friends in dms is not supported.");
                return;
            }

            try
            {
                var friendNotFoundList = new List<string>();
                var duplicateFriendsList = new List<string>();

                var addedFriendsCount = 0;

                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                if (existingFriends.Count + friends.Length > 10)
                {
                    await ReplyAsync("Sorry, but you can't have more than 10 friends.");
                    return;
                }

                foreach (var friend in friends)
                {
                    var friendGuildUser = await this._guildService.FindUserFromGuildAsync(this.Context, friend);

                    Persistence.Domain.Models.User friendUserSettings;
                    string friendUsername;

                    if (friendGuildUser != null)
                    {
                        friendUserSettings = await this._userService.GetUserSettingsAsync(friendGuildUser);
                        friendUsername = friendUserSettings?.UserNameLastFM ?? friend;
                    }
                    else
                    {
                        friendUsername = friend;
                        friendUserSettings = null;
                    }

                    if (!existingFriends.Select(s => s.ToLower()).Contains(friendUsername))
                    {
                        if (await this._lastFmService.LastFMUserExistsAsync(friendUsername))
                        {
                            await this._friendsService.AddLastFMFriendAsync(this.Context.User.Id, friendUsername, friendUserSettings?.UserId);
                            addedFriendsCount++;
                        }
                        else
                        {
                            friendNotFoundList.Add(friendUsername);
                        }
                    }
                    else
                    {
                        duplicateFriendsList.Add(friendUsername);
                    }
                }

                if (addedFriendsCount > 1)
                {
                    await ReplyAsync("Succesfully added " + addedFriendsCount + " friends."
                    );
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

                if (duplicateFriendsList.Count > 0)
                {
                    await ReplyAsync("Couldn't add " + duplicateFriendsList.Count + " duplicate friends.");
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
        [LoginRequired]
        public async Task RemoveFriends([Summary("Friend names")] params string[] friends)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

            if (friends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to remove. Please use their last.fm usernames or discord id.");
                return;
            }

            var removedFriendsCount = 0;

            try
            {
                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                foreach (var friend in friends)
                {
                    var friendGuildUser = await this._guildService.FindUserFromGuildAsync(this.Context, friend);

                    string username;

                    if (friendGuildUser != null)
                    {
                        var friendUserSettings = await this._userService.GetUserSettingsAsync(friendGuildUser);
                        username = friendUserSettings?.UserNameLastFM ?? friend;
                    }
                    else
                    {
                        username = friend;
                    }

                    if (existingFriends.Select(s => s.ToLower()).Contains(username.ToLower()))
                    {
                        var friendSuccesfullyRemoved = await this._friendsService.RemoveLastFMFriendAsync(userSettings.UserId, username);
                        if (friendSuccesfullyRemoved)
                        {
                            removedFriendsCount++;
                        }
                    }
                }

                if (removedFriendsCount > 1)
                {
                    await ReplyAsync("Succesfully removed " + removedFriendsCount + " friends.");
                }
                else if (removedFriendsCount < 1)
                {
                    await ReplyAsync("Couldn't remove " + removedFriendsCount +
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
        [LoginRequired]
        public async Task RemoveAllFriends()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild.Id) ?? ConfigData.Data.CommandPrefix;

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
