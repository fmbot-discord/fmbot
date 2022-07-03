using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands
{
    [Name("Friends")]
    public class FriendsCommands : BaseCommandModule
    {
        private readonly FriendsService _friendsService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly UserService _userService;
        private readonly SettingService _settingService;
        private readonly IUpdateService _updateService;
        private readonly FriendBuilders _friendBuilders;

        private InteractiveService Interactivity { get; }

        public FriendsCommands(
                FriendsService friendsService,
                GuildService guildService,
                IPrefixService prefixService,
                LastFmRepository lastFmRepository,
                UserService userService,
                IOptions<BotSettings> botSettings,
                SettingService settingService,
                IUpdateService updateService,
                FriendBuilders friendBuilders,
                InteractiveService interactivity) : base(botSettings)
        {
            this._friendsService = friendsService;
            this._guildService = guildService;
            this._lastFmRepository = lastFmRepository;
            this._prefixService = prefixService;
            this._userService = userService;
            this._settingService = settingService;
            this._updateService = updateService;
            this._friendBuilders = friendBuilders;
            this.Interactivity = interactivity;
        }

        [Command("friends", RunMode = RunMode.Async)]
        [Summary("Displays your friends and what they're listening to.")]
        [Alias("recentfriends", "friendsrecent", "f")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Friends)]
        public async Task FriendsAsync()
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

            try
            {
                var response = await this._friendBuilders.FriendsAsync(new ContextModel(this.Context, prfx, contextUser));

                await this.Context.SendResponse(this.Interactivity, response);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Unable to show friends due to an internal error.");
            }
        }

        [Command("addfriends", RunMode = RunMode.Async)]
        [Summary("Adds users to your friend list")]
        [Options(Constants.UserMentionExample)]
        [Examples("addfriends fm-bot @user", "addfriends 356268235697553409")]
        [Alias("friendsset", "setfriends", "friendsadd", "addfriend", "setfriend", "friends add", "friend add", "add friends")]
        [UsernameSetRequired]
        [GuildOnly]
        [CommandCategories(CommandCategory.Friends)]
        public async Task AddFriends([Summary("Friend names")] params string[] enteredFriends)
        {
            if (enteredFriends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to add. You can use their last.fm usernames, discord mention or discord id.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

            try
            {
                var addedFriendsList = new List<string>();
                var friendNotFoundList = new List<string>();
                var duplicateFriendsList = new List<string>();

                var existingFriends = await this._friendsService.GetFriendsAsync(this.Context.User.Id);

                var friendLimitReached = false;

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    if (contextUser.UserType == UserType.User && existingFriends.Count >= 12 ||
                        contextUser.UserType != UserType.User && existingFriends.Count >= 16)
                    {
                        friendLimitReached = true;
                        break;
                    }

                    var foundFriend = await this._settingService.GetUser(enteredFriendParameter, contextUser, this.Context, true);

                    string friendUsername;
                    int? friendUserId = null;

                    if (foundFriend.DifferentUser)
                    {
                        friendUsername = foundFriend.UserNameLastFm;
                        friendUserId = foundFriend.UserId;
                    }
                    else
                    {
                        friendUsername = enteredFriendParameter;
                    }

                    if (!existingFriends.Where(w => w.LastFMUserName != null).Select(s => s.LastFMUserName.ToLower()).Contains(friendUsername.ToLower()) &&
                        !existingFriends.Where(w => w.FriendUser != null).Select(s => s.FriendUser.UserNameLastFM.ToLower()).Contains(friendUsername.ToLower()))
                    {
                        if (await this._lastFmRepository.LastFmUserExistsAsync(friendUsername))
                        {
                            await this._friendsService.AddLastFmFriendAsync(contextUser, friendUsername, friendUserId);
                            addedFriendsList.Add(friendUsername);
                            existingFriends.Add(new Friend
                            {
                                LastFMUserName = friendUsername
                            });
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

                if (friendLimitReached)
                {
                    if (contextUser.UserType == UserType.User)
                    {
                        this._embed.AddField("Friend limit reached",
                            "Sorry, but you can't have more than 12 friends. \n\n" +
                            $"Did you know that .fmbot supporters can add up to 16 friends? [Get supporter here](https://opencollective.com/fmbot/contribute) or use `{prfx}donate` for more info.");
                    }
                    else
                    {
                        this._embed.AddField("Friend limit reached",
                            "Sorry, but you can't have more than 16 friends.");
                    }
                }

                var reply = "";
                if (addedFriendsList.Count > 0)
                {
                    reply += $"Successfully added {addedFriendsList.Count} {StringExtensions.GetFriendsString(addedFriendsList.Count)}:\n";
                    foreach (var addedFriend in addedFriendsList)
                    {
                        reply += $"- *[{addedFriend}]({Constants.LastFMUserUrl}{addedFriend})*\n";
                    }
                    reply += "\n";
                }
                if (friendNotFoundList.Count > 0)
                {
                    reply += $"Could not add {addedFriendsList.Count} {StringExtensions.GetFriendsString(friendNotFoundList.Count)}. Please ensure you spelled their name correctly or that they are registered in .fmbot.\n";
                    foreach (var notFoundFriend in friendNotFoundList)
                    {
                        reply += $"- *[{notFoundFriend}]({Constants.LastFMUserUrl}{notFoundFriend})*\n";
                    }
                    reply += "\n";
                }
                if (duplicateFriendsList.Count > 0)
                {
                    reply += $"Could not add {duplicateFriendsList.Count} {StringExtensions.GetFriendsString(duplicateFriendsList.Count)} because you already have them added:\n";
                    foreach (var dupeFriend in duplicateFriendsList)
                    {
                        reply += $"- *[{dupeFriend}]({Constants.LastFMUserUrl}{dupeFriend})*\n";
                    }
                }

                this._embed.WithDescription(reply);

                if (contextUser.UserType != UserType.User && !friendLimitReached)
                {
                    var userType = contextUser.UserType.ToString().ToLower();
                    this._embed.WithFooter($"Thank you for being an .fmbot {userType}! You can now add up to 16 friends.");
                }

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to add friend(s) due to an internal error.");
            }
        }

        [Command("removefriends", RunMode = RunMode.Async)]
        [Summary("Removes users from your friend list")]
        [Options(Constants.UserMentionExample)]
        [Examples("removefriends fm-bot @user", "removefriend 356268235697553409")]
        [Alias("friendsremove", "deletefriend", "deletefriends", "removefriend", "remove friend", "remove friends", "friends remove", "friend remove")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Friends)]
        public async Task RemoveFriends([Summary("Friend names")] params string[] enteredFriends)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

            if (enteredFriends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to remove. You can use their last.fm usernames, discord mention or discord id.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var removedFriendsList = new List<string>();
            var failedRemoveFriends = new List<string>();

            try
            {
                var existingFriends = await this._friendsService.GetFriendsAsync(this.Context.User.Id);

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    var foundFriend = await this._settingService.GetUser(enteredFriendParameter, contextUser, this.Context, true);

                    var friendUsername = foundFriend.DifferentUser ? foundFriend.UserNameLastFm : enteredFriendParameter;

                    if (existingFriends.Where(w => w.LastFMUserName != null).Select(s => s.LastFMUserName.ToLower()).Contains(friendUsername.ToLower()) ||
                        existingFriends.Where(w => w.FriendUser != null).Select(s => s.FriendUser.UserNameLastFM.ToLower()).Contains(friendUsername.ToLower()))
                    {
                        var friendSuccessfullyRemoved = await this._friendsService.RemoveLastFmFriendAsync(contextUser.UserId, friendUsername);
                        if (friendSuccessfullyRemoved)
                        {
                            removedFriendsList.Add(friendUsername);
                        }
                        else
                        {
                            failedRemoveFriends.Add(friendUsername);
                        }
                    }
                }

                var reply = "";
                if (removedFriendsList.Count > 0)
                {
                    reply += $"Successfully removed {removedFriendsList.Count} friend(s):\n";
                    foreach (var removedFriend in removedFriendsList)
                    {
                        reply += $"- *[{removedFriend}]({Constants.LastFMUserUrl}{removedFriend})*\n";
                    }
                    reply += "\n";
                }
                if (failedRemoveFriends.Count > 0)
                {
                    reply += $"Could not remove {failedRemoveFriends.Count} friend(s).\n";
                    foreach (var failedRemovedFriend in failedRemoveFriends)
                    {
                        reply += $"- *[{failedRemovedFriend}]({Constants.LastFMUserUrl}{failedRemovedFriend})*\n";
                    }
                    reply += "\n";
                }
                if (removedFriendsList.Count == 0 || failedRemoveFriends.Count == 0)
                {
                    reply += $"Could not find any friends to remove. Please enter their Last.fm username, mention them or use their Discord id.";
                }

                this._embed.WithDescription(reply);
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to remove friend(s) due to an internal error. Please contact .fmbot staff.");
            }
        }

        [Command("removeallfriends", RunMode = RunMode.Async)]
        [Summary("Remove all your friends")]
        [Alias("friendsremoveall", "friends remove all")]
        [UsernameSetRequired]
        [CommandCategories(CommandCategory.Friends)]
        public async Task RemoveAllFriends()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            try
            {
                await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);

                await ReplyAsync("Removed all your friends.");
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to remove all friends due to an internal error.");
            }
        }
    }
}
