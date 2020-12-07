using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;

namespace FMBot.Bot.Commands
{
    public class FriendsCommands : ModuleBase
    {
        private readonly FriendsService _friendsService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly LastFmService _lastFmService;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public FriendsCommands(
                FriendsService friendsService,
                GuildService guildService,
                IPrefixService prefixService,
                LastFmService lastFmService,
                UserService userService
            )
        {
            this._friendsService = friendsService;
            this._guildService = guildService;
            this._lastFmService = lastFmService;
            this._prefixService = prefixService;
            this._userService = userService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("friends", RunMode = RunMode.Async)]
        [Summary("Displays a user's friends and what they are listening to.")]
        [Alias("recentfriends", "friendsrecent", "f")]
        [UsernameSetRequired]
        public async Task FriendsAsync()
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            try
            {
                var friends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                if (friends?.Any() != true)
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                                     "`.fmaddfriends 'lastfmname/discord name'`");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
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
                    var tracks = await this._lastFmService.GetRecentTracksAsync(friend, useCache: true);

                    string track;
                    string friendTitle = "";
                    if (!tracks.Success || tracks.Content == null)
                    {
                        track = "Friend could not be retrieved";
                    }
                    else if (!tracks.Content.RecentTracks.Track.Any())
                    {
                        track = "No scrobbles found.";
                    }
                    else
                    {
                        var lastTrack = tracks.Content.RecentTracks.Track[0];
                        track = LastFmService.TrackToOneLinedString(lastTrack);
                        if (lastTrack.Attr != null && lastTrack.Attr.Nowplaying)
                        {
                            friendTitle += "🎶";
                        }
                        else if (lastTrack.Date != null)
                        {
                            var dateTime = DateTime.UnixEpoch.AddSeconds(lastTrack.Date.Uts).ToUniversalTime();
                            friendTitle += $"({StringExtensions.GetTimeAgoShortString(dateTime)})";
                        }

                        totalPlaycount += (int)tracks.Content.RecentTracks.Attr.Total;
                    }

                    embedDescription += $"**[{friend}]({Constants.LastFMUserUrl}{friend})** {friendTitle} - {track} \n";
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
        [Summary("Adds your friends' Last.fm names.")]
        [Alias("friendsset", "setfriends", "friendsadd", "addfriend", "setfriend")]
        [UsernameSetRequired]
        public async Task AddFriends([Summary("Friend names")] params string[] enteredFriends)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Sorry, but adding friends in dms is not supported.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            if (enteredFriends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to add. You can use their last.fm usernames, discord mention or discord id.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            try
            {
                var addedFriendsList = new List<string>();
                var friendNotFoundList = new List<string>();
                var duplicateFriendsList = new List<string>();

                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                if (existingFriends.Count + enteredFriends.Length > 10)
                {
                    await ReplyAsync("Sorry, but you can't have more than 10 friends.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, enteredFriendParameter);

                    Persistence.Domain.Models.User friendUserSettings;
                    string friendUsername;

                    if (guildUser != null)
                    {
                        friendUserSettings = await this._userService.GetUserSettingsAsync(guildUser);
                        friendUsername = friendUserSettings?.UserNameLastFM ?? enteredFriendParameter;
                    }
                    else
                    {
                        friendUsername = enteredFriendParameter;
                        friendUserSettings = null;
                    }

                    if (!existingFriends.Select(s => s.ToLower()).Contains(friendUsername.ToLower()))
                    {
                        if (await this._lastFmService.LastFmUserExistsAsync(friendUsername))
                        {
                            await this._friendsService.AddLastFMFriendAsync(this.Context.User.Id, friendUsername, friendUserSettings?.UserId);
                            addedFriendsList.Add(friendUsername);
                            existingFriends.Add(friendUsername);
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

                var reply = "";
                if (addedFriendsList.Count > 0)
                {
                    reply += $"Succesfully added {addedFriendsList.Count} friend(s):\n";
                    foreach (var addedFriend in addedFriendsList)
                    {
                        reply += $"- *[{addedFriend}]({Constants.LastFMUserUrl}{addedFriend})*\n";
                    }
                    reply += "\n";
                }
                if (friendNotFoundList.Count > 0)
                {
                    reply += $"Could not add {addedFriendsList.Count} friend(s). Please ensure you spelled their name correctly or that they are registered in .fmbot.\n";
                    foreach (var notFoundFriend in friendNotFoundList)
                    {
                        reply += $"- *[{notFoundFriend}]({Constants.LastFMUserUrl}{notFoundFriend})*\n";
                    }
                    reply += "\n";
                }
                if (duplicateFriendsList.Count > 0)
                {
                    reply += $"Could not add {duplicateFriendsList.Count} friend(s) because you already have them added:\n";
                    foreach (var dupeFriend in duplicateFriendsList)
                    {
                        reply += $"- *[{dupeFriend}]({Constants.LastFMUserUrl}{dupeFriend})*\n";
                    }
                }

                this._embed.WithDescription(reply);
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
        [Summary("Remove your friends' Last.fm names.")]
        [Alias("friendsremove", "deletefriend", "deletefriends", "removefriend")]
        [UsernameSetRequired]
        public async Task RemoveFriends([Summary("Friend names")] params string[] enteredFriends)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

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
                var existingFriends = await this._friendsService.GetFMFriendsAsync(this.Context.User);

                foreach (var enteredFriendParameter in enteredFriends)
                {
                    var friendGuildUser = await this._guildService.FindUserFromGuildAsync(this.Context, enteredFriendParameter);

                    string friendUsername;

                    if (friendGuildUser != null)
                    {
                        var friendUserSettings = await this._userService.GetUserSettingsAsync(friendGuildUser);
                        friendUsername = friendUserSettings?.UserNameLastFM ?? enteredFriendParameter;
                    }
                    else
                    {
                        friendUsername = enteredFriendParameter;
                    }

                    if (existingFriends.Select(s => s.ToLower()).Contains(friendUsername.ToLower()))
                    {
                        var friendSuccesfullyRemoved = await this._friendsService.RemoveLastFMFriendAsync(userSettings.UserId, friendUsername);
                        if (friendSuccesfullyRemoved)
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
        [Alias("friendsremoveall")]
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
