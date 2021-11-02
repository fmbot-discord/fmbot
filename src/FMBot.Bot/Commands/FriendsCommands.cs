using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
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

        public FriendsCommands(
                FriendsService friendsService,
                GuildService guildService,
                IPrefixService prefixService,
                LastFmRepository lastFmRepository,
                UserService userService,
                IOptions<BotSettings> botSettings,
                SettingService settingService,
                IUpdateService updateService) : base(botSettings)
        {
            this._friendsService = friendsService;
            this._guildService = guildService;
            this._lastFmRepository = lastFmRepository;
            this._prefixService = prefixService;
            this._userService = userService;
            this._settingService = settingService;
            this._updateService = updateService;
        }

        [Command("friends", RunMode = RunMode.Async)]
        [Summary("Displays your friends and what they're listening to.")]
        [Alias("recentfriends", "friendsrecent", "f")]
        [UsernameSetRequired]
        public async Task FriendsAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

            try
            {
                var friends = await this._friendsService.GetFmFriendsAsync(this.Context.User);

                if (friends?.Any() != true)
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                     $"`{prfx}addfriends {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                _ = this.Context.Channel.TriggerTypingAsync();

                var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

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
                    var friendUsername = friend.LastFMUserName;
                    var friendNameToDisplay = friendUsername;

                    if (guild?.GuildUsers != null && guild.GuildUsers.Any() && friend.FriendUserId.HasValue)
                    {
                        var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == friend.FriendUserId.Value);
                        if (guildUser?.UserName != null)
                        {
                            friendNameToDisplay = guildUser.UserName;

                            var discordUser = await this.Context.Guild.GetUserAsync(guildUser.User.DiscordUserId);
                            if (discordUser != null)
                            {
                                friendNameToDisplay = discordUser.Nickname ?? discordUser.Username;
                            }
                        }
                    }

                    string sessionKey = null;
                    if (friend.FriendUser?.UserNameLastFM != null)
                    {
                        friendUsername = friend.FriendUser.UserNameLastFM;
                        if (!string.IsNullOrWhiteSpace(friend.FriendUser.SessionKeyLastFm))
                        {
                            sessionKey = friend.FriendUser.SessionKeyLastFm;
                        }
                    }

                    Response<RecentTrackList> tracks;

                    if (friend.FriendUserId != null && friend.FriendUser?.SessionKeyLastFm != null)
                    {
                        tracks = await this._updateService.UpdateUserAndGetRecentTracks(friend.FriendUser);
                    }
                    else
                    {
                        tracks = await this._lastFmRepository.GetRecentTracksAsync(friendUsername, useCache: true, sessionKey: sessionKey);
                    }

                    string track;
                    if (!tracks.Success || tracks.Content == null)
                    {
                        track = $"Friend could not be retrieved ({tracks.Error})";
                    }
                    else if (!tracks.Content.RecentTracks.Any())
                    {
                        track = "No scrobbles found.";
                    }
                    else
                    {
                        var lastTrack = tracks.Content.RecentTracks[0];
                        track = LastFmRepository.TrackToOneLinedString(lastTrack);
                        if (lastTrack.NowPlaying)
                        {
                            track += " 🎶";
                        }
                        else if (lastTrack.TimePlayed.HasValue)
                        {
                            track += $" ({StringExtensions.GetTimeAgoShortString(lastTrack.TimePlayed.Value)})";
                        }

                        totalPlaycount += (int)tracks.Content.TotalAmount;
                    }

                    embedDescription += $"**[{friendNameToDisplay}]({Constants.LastFMUserUrl}{friendUsername})** | {track}\n";
                }, maxDegreeOfParallelism: 3);

                this._embedFooter.WithText(embedFooterText + totalPlaycount.ToString("0"));
                this._embed.WithFooter(this._embedFooter);

                this._embed.WithDescription(embedDescription);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
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

                var existingFriends = await this._friendsService.GetFmFriendsAsync(this.Context.User);

                var friendLimitReached = false;

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    if (contextUser.UserType == UserType.User && existingFriends.Count >= 12 ||
                        contextUser.UserType != UserType.User && existingFriends.Count >= 15)
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
                            $"Did you know that .fmbot supporters can add up to 15 friends? For more information, check out `{prfx}donate`.");
                    }
                    else
                    {
                        this._embed.AddField("Friend limit reached",
                            "Sorry, but you can't have more than 15 friends.");
                    }
                }

                var reply = "";
                if (addedFriendsList.Count > 0)
                {
                    reply += $"Succesfully added {addedFriendsList.Count} {StringExtensions.GetFriendsString(addedFriendsList.Count)}:\n";
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
                    this._embed.WithFooter($"Thank you for being an .fmbot {userType}! You can now add up to 15 friends.");
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
                var existingFriends = await this._friendsService.GetFmFriendsAsync(this.Context.User);

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    var foundFriend = await this._settingService.GetUser(enteredFriendParameter, contextUser, this.Context, true);

                    string friendUsername;

                    if (foundFriend.DifferentUser)
                    {
                        friendUsername = foundFriend.UserNameLastFm;
                    }
                    else
                    {
                        friendUsername = enteredFriendParameter;
                    }

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
                    reply += $"Succesfully removed {removedFriendsList.Count} friend(s):\n";
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
