using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord;
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

namespace FMBot.Bot.Builders;

public class FriendBuilders
{
    private readonly FriendsService _friendsService;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly IUpdateService _updateService;
    private readonly SettingService _settingService;

    public FriendBuilders(FriendsService friendsService, UserService userService, GuildService guildService,
        LastFmRepository lastFmRepository, IUpdateService updateService, SettingService settingService)
    {
        this._friendsService = friendsService;
        this._userService = userService;
        this._guildService = guildService;
        this._lastFmRepository = lastFmRepository;
        this._updateService = updateService;
        this._settingService = settingService;
    }

    private record FriendResult(DateTime? timePlayed, string Result);

    public async Task<ResponseModel> FriendsAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);

        if (friends?.Any() != true)
        {
            response.Embed.WithDescription("We couldn't find any friends. To add friends:\n" +
                                           $"`{context.Prefix}friendsadd {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`\n\n" +
                                           $"Or right-click a user, go to apps and click 'Add as friend'");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);

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

        embedTitle += await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName(embedTitle);
        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }

        response.EmbedAuthor.WithUrl(Constants.LastFMUserUrl + context.ContextUser.UserNameLastFM);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var totalPlaycount = 0;
        var friendResult = new List<FriendResult>();
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

                    var user = await this._userService.GetUserForIdAsync(guildUser.UserId);
                    var discordUser = await context.DiscordGuild.GetUserAsync(user.DiscordUserId, CacheMode.CacheOnly);
                    if (discordUser?.Username != null)
                    {
                        friendNameToDisplay = discordUser.Nickname ?? discordUser.Username;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(friendNameToDisplay))
            {
                friendUsername = friend.LastFMUserName;
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
                tracks = await this._lastFmRepository.GetRecentTracksAsync(friendUsername, useCache: true,
                    sessionKey: sessionKey);
            }

            string track;
            DateTime? timePlayed = null;
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
                    timePlayed = new DateTime(2200, 1, 1);
                    track += " 🎶";
                }
                else if (lastTrack.TimePlayed.HasValue)
                {
                    timePlayed = lastTrack.TimePlayed.Value;
                    track += $" ({StringExtensions.GetTimeAgoShortString(lastTrack.TimePlayed.Value)})";
                }

                totalPlaycount += (int)tracks.Content.TotalAmount;
            }

            friendResult.Add(new FriendResult(timePlayed, $"**[{friendNameToDisplay}]({Constants.LastFMUserUrl}{friendUsername})** | {track}"));
        }, maxDegreeOfParallelism: 3);

        response.EmbedFooter.WithText(embedFooterText + totalPlaycount.ToString("0"));
        response.Embed.WithFooter(response.EmbedFooter);

        var embedDescription = new StringBuilder();
        foreach (var friend in friendResult.OrderByDescending(o => o.timePlayed).ThenBy(o => o.Result))
        {
            embedDescription.AppendLine(friend.Result);
        }

        response.Embed.WithDescription(embedDescription.ToString());

        return response;
    }

    public async Task<ResponseModel> AddFriendsAsync(ContextModel context, string[] enteredFriends)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var addedFriendsList = new List<string>();
        var friendNotFoundList = new List<string>();
        var duplicateFriendsList = new List<string>();

        var existingFriends = await this._friendsService.GetFriendsAsync(context.DiscordUser.Id);

        var friendLimitReached = false;

        foreach (var enteredFriendParameter in enteredFriends)
        {
            if (context.ContextUser.UserType == UserType.User && existingFriends.Count >= Constants.MaxFriends ||
                context.ContextUser.UserType != UserType.User && existingFriends.Count >= Constants.MaxFriendsSupporter)
            {
                friendLimitReached = true;
                break;
            }

            var foundFriend = await this._settingService.GetUser(enteredFriendParameter, context.ContextUser,
                context.DiscordGuild, context.DiscordUser, true);

            string friendUsername;
            int? friendUserId = null;

            if (foundFriend.DifferentUser)
            {
                friendUsername = foundFriend.UserNameLastFm;
                friendUserId = foundFriend.UserId != 0 ? foundFriend.UserId : null;
            }
            else
            {
                friendUsername = enteredFriendParameter;
            }

            if (!existingFriends.Where(w => w.LastFMUserName != null).Select(s => s.LastFMUserName.ToLower())
                    .Contains(friendUsername.ToLower()) &&
                !existingFriends.Where(w => w.FriendUser != null).Select(s => s.FriendUser.UserNameLastFM.ToLower())
                    .Contains(friendUsername.ToLower()))
            {
                if (await this._lastFmRepository.LastFmUserExistsAsync(friendUsername))
                {
                    await this._friendsService.AddLastFmFriendAsync(context.ContextUser, friendUsername, friendUserId);
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
            if (context.ContextUser.UserType == UserType.User)
            {
                response.Embed.AddField("Friend limit reached",
                    $"Sorry, but you can't have more than {Constants.MaxFriends} friends. \n\n" +
                    $".fmbot supporters can add up to {Constants.MaxFriendsSupporter} friends. [Click here to view all perks of being a supporter]({Constants.GetSupporterOverviewLink}).");
                response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: SupporterService.GetSupporterLink());
            }
            else
            {
                response.Embed.AddField("Friend limit reached",
                    $"Sorry, but you can't have more than {Constants.MaxFriendsSupporter} friends.");
            }
        }

        var reply = "";
        if (addedFriendsList.Count > 0)
        {
            reply +=
                $"Successfully added {addedFriendsList.Count} {StringExtensions.GetFriendsString(addedFriendsList.Count)}:\n";
            foreach (var addedFriend in addedFriendsList)
            {
                reply += $"- *[{addedFriend}]({Constants.LastFMUserUrl}{addedFriend})*\n";
            }

            reply += "\n";
        }

        if (friendNotFoundList.Count > 0)
        {
            reply +=
                $"Could not add {addedFriendsList.Count} {StringExtensions.GetFriendsString(friendNotFoundList.Count)}. Please ensure you spelled their name correctly and/or that they are registered in .fmbot.\n";
            foreach (var notFoundFriend in friendNotFoundList)
            {
                reply += $"- *[{notFoundFriend}]({Constants.LastFMUserUrl}{notFoundFriend})*\n";
            }

            reply += "\n";
        }

        if (duplicateFriendsList.Count > 0)
        {
            reply +=
                $"Could not add {duplicateFriendsList.Count} {StringExtensions.GetFriendsString(duplicateFriendsList.Count)} because you already have them added:\n";
            foreach (var dupeFriend in duplicateFriendsList)
            {
                reply += $"- *[{dupeFriend}]({Constants.LastFMUserUrl}{dupeFriend})*\n";
            }
        }

        response.Embed.WithDescription(reply);

        if (context.ContextUser.UserType != UserType.User && !friendLimitReached && existingFriends.Count >= Constants.MaxFriendsSupporter - 5)
        {
            var userType = context.ContextUser.UserType.ToString().ToLower();
            response.Embed.WithFooter(
                $"Thank you for being an .fmbot {userType}! You can now add up to {Constants.MaxFriendsSupporter} friends.");
        }

        return response;
    }

    public async Task<ResponseModel> RemoveFriendsAsync(ContextModel context, string[] enteredFriends, bool contextCommand = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var removedFriendsList = new List<string>();
        var failedRemoveFriends = new List<string>();

        var existingFriends = await this._friendsService.GetFriendsAsync(context.DiscordUser.Id);

        foreach (var enteredFriendParameter in enteredFriends)
        {
            var foundFriend =
                await this._settingService.GetUser(enteredFriendParameter, context.ContextUser, context.DiscordGuild, context.DiscordUser, true);

            var friendUsername = foundFriend.DifferentUser ? foundFriend.UserNameLastFm : enteredFriendParameter;

            if (existingFriends.Where(w => w.LastFMUserName != null).Select(s => s.LastFMUserName.ToLower())
                    .Contains(friendUsername.ToLower()) ||
                existingFriends.Where(w => w.FriendUser != null).Select(s => s.FriendUser.UserNameLastFM.ToLower())
                    .Contains(friendUsername.ToLower()))
            {
                var friendSuccessfullyRemoved =
                    await this._friendsService.RemoveLastFmFriendAsync(context.ContextUser.UserId, friendUsername);
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

        if (removedFriendsList.Count == 0 && failedRemoveFriends.Count == 0)
        {
            if (contextCommand)
            {
                reply +=
                    $"Could not find the user you want to remove from your friends in your friend list.";
            }
            else
            {
                reply +=
                    $"Could not find any friends to remove. Please enter their Last.fm username, mention them or use their Discord id.";
            }
        }

        response.Embed.WithDescription(reply);

        return response;
    }
}
