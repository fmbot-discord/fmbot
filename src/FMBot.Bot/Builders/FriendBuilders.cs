using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dasync.Collections;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class FriendBuilders
{
    private readonly FriendsService _friendsService;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly UpdateService _updateService;
    private readonly SettingService _settingService;

    public FriendBuilders(FriendsService friendsService, UserService userService, GuildService guildService,
        IDataSourceFactory dataSourceFactory, UpdateService updateService, SettingService settingService)
    {
        this._friendsService = friendsService;
        this._userService = userService;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this._updateService = updateService;
        this._settingService = settingService;
    }

    private record FriendResult(DateTime? TimePlayed, string Result);

    public async Task<ResponseModel> FriendsAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);

        if (friends?.Any() != true)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                "We couldn't find any friends. To add friends:\n" +
                $"`{context.Prefix}friendsadd {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`\n\n" +
                "Or right-click a user, go to apps and click 'Add as friend'.\n\n" +
                "You can also sync your Last.fm friends — open **Manage** below."));
            response.ComponentsContainer.AddComponent(FriendButtons());
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var visibleFriends = friends
            .Where(f => f.FriendType >= FriendType.VisibleInNowPlaying)
            .ToList();

        if (visibleFriends.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                $"You have **{friends.Count}** {StringExtensions.GetFriendsString(friends.Count)}, but none of them are set to show here.\n\n" +
                "Use **Manage** below to choose who appears in your now playing list."));
            response.ComponentsContainer.AddComponent(FriendButtons());
            return response;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);

        var footerText = "Total scrobbles: ";
        string title;
        if (visibleFriends.Count > 1)
        {
            title = $"Now playing for {visibleFriends.Count} friends from ";
        }
        else
        {
            title = "Now playing for 1 friend from ";
            footerText = "Total scrobbles: ";
        }

        title += await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        var totalPlaycount = 0;
        var friendResult = new ConcurrentBag<FriendResult>();
        await visibleFriends.ParallelForEachAsync(async friend =>
        {
            var friendUsername = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
            var friendNameToDisplay = friendUsername;

            if (guild?.GuildUsers != null && guild.GuildUsers.Any() && friend.FriendUserId.HasValue)
            {
                var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == friend.FriendUserId.Value);
                if (guildUser?.UserName != null)
                {
                    friendNameToDisplay = guildUser.UserName;

                    var user = await this._userService.GetUserForIdAsync(guildUser.UserId);
                    if (context.DiscordGuild?.Users.TryGetValue(user.DiscordUserId, out var discordGuildUser) == true)
                    {
                        friendNameToDisplay = discordGuildUser.GetDisplayName();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(friendNameToDisplay))
            {
                friendUsername = friend.LastFMUserName;
            }

            string sessionKey = null;
            if (!string.IsNullOrWhiteSpace(friend.FriendUser?.SessionKeyLastFm))
            {
                sessionKey = friend.FriendUser.SessionKeyLastFm;
            }

            Response<RecentTrackList> tracks;

            if (friend.FriendUserId != null && friend.FriendUser?.SessionKeyLastFm != null)
            {
                tracks = await this._updateService.UpdateUserAndGetRecentTracks(friend.FriendUser);
            }
            else
            {
                tracks = await this._dataSourceFactory.GetRecentTracksAsync(friendUsername, useCache: true,
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

                Interlocked.Add(ref totalPlaycount, (int)tracks.Content.TotalAmount);
            }

            friendResult.Add(new FriendResult(timePlayed,
                $"**{StringExtensions.MarkdownLink(friendNameToDisplay, LastfmUrlExtensions.GetUserUrl(friendUsername))}** | {track}"));
        }, maxDegreeOfParallelism: 6);

        var friendsText = new StringBuilder();
        foreach (var friend in friendResult.OrderByDescending(o => o.TimePlayed).ThenBy(o => o.Result))
        {
            friendsText.AppendLine(friend.Result);
        }

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"### {title}"));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(friendsText.ToString().TrimEnd()));
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
            new ButtonProperties(InteractionConstants.Friends.Overview, "Manage", ButtonStyle.Secondary))
        {
            Components =
            [
                new TextDisplayProperties($"-# {footerText}{totalPlaycount:0}")
            ]
        });

        return response;
    }

    private static ActionRowProperties FriendButtons()
    {
        return new ActionRowProperties()
            .WithButton("Manage",
                customId: InteractionConstants.Friends.Overview,
                style: ButtonStyle.Secondary);
    }

    public async Task<ResponseModel> AddFriendsAsync(ContextModel context, string[] enteredFriends)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var addedFriendsList = new List<(string Name, FriendType Type)>();
        var friendNotFoundList = new List<string>();
        var duplicateFriendsList = new List<(string Name, FriendType Type)>();

        var existingFriends = await this._friendsService.GetFriendsAsync(context.DiscordUser.Id);

        var isSupporter = context.ContextUser.UserType != UserType.User;
        var visibleCap = isSupporter ? Constants.MaxVisibleFriendsSupporter : Constants.MaxVisibleFriends;
        var visibleCount = existingFriends.Count(f => f.FriendType >= FriendType.VisibleInNowPlaying);

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
                context.DiscordGuild, context.DiscordUser, true, true);

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
                if (await this._dataSourceFactory.LastFmUserExistsAsync(friendUsername))
                {
                    var friendType = visibleCount < visibleCap
                        ? FriendType.VisibleInNowPlaying
                        : FriendType.Normal;

                    await this._friendsService.AddLastFmFriendAsync(context.ContextUser, friendUsername,
                        friendUserId, friendType);
                    addedFriendsList.Add((friendUsername, friendType));
                    existingFriends.Add(new Friend
                    {
                        LastFMUserName = friendUsername,
                        FriendType = friendType
                    });

                    if (friendType == FriendType.VisibleInNowPlaying)
                    {
                        visibleCount++;
                    }
                }
                else
                {
                    friendNotFoundList.Add(friendUsername);
                }
            }
            else
            {
                var existingMatch = existingFriends.FirstOrDefault(w =>
                    w.LastFMUserName?.ToLower() == friendUsername.ToLower() ||
                    w.FriendUser?.UserNameLastFM?.ToLower() == friendUsername.ToLower());
                duplicateFriendsList.Add((friendUsername, existingMatch?.FriendType ?? FriendType.Normal));
            }
        }

        var body = new StringBuilder();

        if (addedFriendsList.Count > 0)
        {
            body.AppendLine(
                $"Successfully added {addedFriendsList.Count} {StringExtensions.GetFriendsString(addedFriendsList.Count)}:");
            foreach (var addedFriend in addedFriendsList)
            {
                var typeNote = addedFriend.Type == FriendType.VisibleInNowPlaying
                    ? "👁️ Visible in all friends commands including `friendsfm`"
                    : "👥 Visible in all friends commands except `friendsfm`";
                body.AppendLine(
                    $"- *[{addedFriend.Name}]({LastfmUrlExtensions.GetUserUrl(addedFriend.Name)})* — {typeNote}");
            }
        }

        if (friendNotFoundList.Count > 0)
        {
            if (body.Length > 0)
            {
                body.AppendLine();
            }

            body.AppendLine(
                $"Could not add {friendNotFoundList.Count} {StringExtensions.GetFriendsString(friendNotFoundList.Count)}. Ensure they are registered in .fmbot and their Last.fm is not set to private.");
            foreach (var notFoundFriend in friendNotFoundList)
            {
                body.AppendLine($"- *[{notFoundFriend}]({LastfmUrlExtensions.GetUserUrl(notFoundFriend)})*");
            }
        }

        if (duplicateFriendsList.Count > 0)
        {
            if (body.Length > 0)
            {
                body.AppendLine();
            }

            body.AppendLine(
                $"Could not add {duplicateFriendsList.Count} {StringExtensions.GetFriendsString(duplicateFriendsList.Count)} because you already have them added:");
            foreach (var dupeFriend in duplicateFriendsList)
            {
                var typeNote = dupeFriend.Type switch
                {
                    FriendType.CloseFriend => "⭐ Close friend",
                    FriendType.VisibleInNowPlaying => "👁️ Visible in all friends commands including `friendsfm`",
                    _ => "👥 Visible in all friends commands except `friendsfm`"
                };
                body.AppendLine(
                    $"- *[{dupeFriend.Name}]({LastfmUrlExtensions.GetUserUrl(dupeFriend.Name)})* — {typeNote}");
            }
        }

        if (body.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(body.ToString().TrimEnd()));
            response.ComponentsContainer.AddComponent(new ActionRowProperties()
                .WithButton("Manage friends", customId: InteractionConstants.Friends.Overview,
                    style: ButtonStyle.Secondary));
        }

        if (friendLimitReached)
        {
            if (body.Length > 0)
            {
                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            }

            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            if (context.ContextUser.UserType == UserType.User)
            {
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                    $"**Friend limit reached** — You can't have more than {Constants.MaxFriends} friends. Supporters can add up to {Constants.MaxFriendsSupporter}."));
                response.Components = new ActionRowProperties().WithButton(Constants.GetSupporterButton,
                    style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "friends-limit"));
            }
            else
            {
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                    $"**Friend limit reached** — You can't have more than {Constants.MaxFriendsSupporter} friends."));
            }
        }

        if (context.ContextUser.UserType != UserType.User && !friendLimitReached &&
            existingFriends.Count >= Constants.MaxFriendsSupporter - 5)
        {
            var userType = context.ContextUser.UserType.ToString().ToLower();
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                $"-# Thank you for being an .fmbot {userType}! You can add up to {Constants.MaxFriendsSupporter} friends."));
        }

        return response;
    }

    public async Task<ResponseModel> RemoveFriendsAsync(ContextModel context, string[] enteredFriends,
        bool contextCommand = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var removedFriendsList = new List<string>();
        var failedRemoveFriends = new List<string>();

        var existingFriends = await this._friendsService.GetFriendsAsync(context.DiscordUser.Id);

        foreach (var enteredFriendParameter in enteredFriends)
        {
            var foundFriend =
                await this._settingService.GetUser(enteredFriendParameter, context.ContextUser, context.DiscordGuild,
                    context.DiscordUser, true, true);

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

        var body = new StringBuilder();

        if (removedFriendsList.Count > 0)
        {
            body.AppendLine($"Successfully removed {removedFriendsList.Count} friend(s):");
            foreach (var removedFriend in removedFriendsList)
            {
                body.AppendLine($"- *[{removedFriend}]({LastfmUrlExtensions.GetUserUrl(removedFriend)})*");
            }
        }

        if (failedRemoveFriends.Count > 0)
        {
            if (body.Length > 0)
            {
                body.AppendLine();
            }

            body.AppendLine($"Could not remove {failedRemoveFriends.Count} friend(s):");
            foreach (var failedRemovedFriend in failedRemoveFriends)
            {
                body.AppendLine($"- *[{failedRemovedFriend}]({LastfmUrlExtensions.GetUserUrl(failedRemovedFriend)})*");
            }
        }

        if (removedFriendsList.Count == 0 && failedRemoveFriends.Count == 0)
        {
            body.AppendLine(contextCommand
                ? "Could not find that user in your friend list."
                : "Could not find any friends to remove. Please enter their Last.fm username, mention them or use their Discord id.");
        }

        response.ComponentsContainer.AddComponent(new TextDisplayProperties(body.ToString().TrimEnd()));
        response.ComponentsContainer.AddComponent(new ActionRowProperties()
            .WithButton("Manage friends", customId: InteractionConstants.Friends.Overview,
                style: ButtonStyle.Secondary));

        return response;
    }

    public async Task<ResponseModel> FriendedAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        if (context.DiscordGuild != null)
        {
            response.Embed.WithDescription("This command is only supported in DMs.");
            response.CommandResponse = CommandResponse.OnlySupportedInDm;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var friended = await this._friendsService.GetFriendedAsync(context.ContextUser.UserId);

        if (friended?.Any() != true)
        {
            response.Embed.WithDescription("It doesn't seem like anyone's added you as a friend yet.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        response.EmbedAuthor.WithName("People who have added you as a friend in .fmbot");

        var pages = new List<PageBuilder>();

        var friendedPages = friended.ChunkBy(10);
        var counter = 1;
        var pageCounter = 1;
        foreach (var friendedPage in friendedPages)
        {
            var friendedPageString = new StringBuilder();

            foreach (var friend in friendedPage)
            {
                friendedPageString.AppendLine(
                    $"{counter}. **[{friend.User.UserNameLastFM}]({LastfmUrlExtensions.GetUserUrl(friend.User.UserNameLastFM)})**");
                counter++;
            }

            pages.Add(new PageBuilder()
                .WithDescription(friendedPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter($"Page {pageCounter}/{friendedPages.Count} - {friended?.Count} users"));

            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> ManageFriendsAsync(ContextModel context, int page = 0, string note = null,
        bool noteSuccess = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);

        var isSupporter = context.ContextUser.UserType != UserType.User;
        var totalCap = isSupporter ? Constants.MaxFriendsSupporter : Constants.MaxFriends;
        var visibleCap = isSupporter ? Constants.MaxVisibleFriendsSupporter : Constants.MaxVisibleFriends;
        var visibleCount = friends.Count(f => f.FriendType >= FriendType.VisibleInNowPlaying);
        var closeCount = friends.Count(f => f.FriendType == FriendType.CloseFriend);

        response.ComponentsContainer.WithAccentColor(string.IsNullOrWhiteSpace(note)
            ? DiscordConstants.InformationColorBlue
            : noteSuccess
                ? DiscordConstants.SuccessColorGreen
                : DiscordConstants.WarningColorOrange);

        var header = $"-# {friends.Count}/{totalCap} friends · {visibleCount}/{visibleCap} shown in now playing";
        if (isSupporter)
        {
            header += $" · {closeCount}/{Constants.MaxCloseFriends} close friends";
        }

        response.ComponentsContainer.AddComponent(new TextDisplayProperties(
            "## 👥 Manage friends\n" + header));

        if (friends.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                "You don't have any friends yet. Add some with `addfriends`, or sync them from Last.fm below."));
            AddLastFmSection(response, note);
            return response;
        }

        const int pageSize = 8;
        var totalPages = (int)Math.Ceiling(friends.Count / (double)pageSize);
        if (page < 0)
        {
            page = 0;
        }
        if (page >= totalPages)
        {
            page = totalPages - 1;
        }

        var pageFriends = friends
            .OrderByDescending(o => o.FriendType)
            .ThenBy(o => o.LastFmFriend)
            .ThenBy(o => o.FriendUser?.UserNameLastFM ?? o.LastFMUserName)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        foreach (var friend in pageFriends)
        {
            var friendName = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
            var typeLabel = friend.FriendType switch
            {
                FriendType.CloseFriend => "⭐ Close friend - always visible",
                FriendType.VisibleInNowPlaying => "👁️ Visible in all friend commands",
                _ => "👥 Visible in commands"
            };
            var lastFmTag = friend.LastFmFriend ? " · `Last.fm`" : "";

            response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
                new ButtonProperties($"{InteractionConstants.Friends.Manage}:{friend.FriendId}:{page}",
                    EmojiProperties.Standard("⚙️"), ButtonStyle.Secondary))
            {
                Components =
                [
                    new TextDisplayProperties(
                        $"**{StringExtensions.MarkdownLink(friendName, LastfmUrlExtensions.GetUserUrl(friendName))}**\n" +
                        $"-# {typeLabel}{lastFmTag}")
                ]
            });
        }

        if (totalPages > 1)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

            var navRow = new ActionRowProperties();
            navRow.Add(new ButtonProperties(
                $"{InteractionConstants.Friends.OverviewPage}:first:0",
                EmojiProperties.Custom(DiscordConstants.PagesFirst), ButtonStyle.Secondary) { Disabled = page == 0 });
            navRow.Add(new ButtonProperties(
                $"{InteractionConstants.Friends.OverviewPage}:prev:{page - 1}",
                EmojiProperties.Custom(DiscordConstants.PagesPrevious), ButtonStyle.Secondary) { Disabled = page == 0 });
            navRow.Add(new ButtonProperties(
                $"{InteractionConstants.Friends.OverviewPage}:current:{page}",
                $"{page + 1}/{totalPages}", ButtonStyle.Secondary) { Disabled = true });
            navRow.Add(new ButtonProperties(
                $"{InteractionConstants.Friends.OverviewPage}:next:{page + 1}",
                EmojiProperties.Custom(DiscordConstants.PagesNext), ButtonStyle.Secondary) { Disabled = page >= totalPages - 1 });
            navRow.Add(new ButtonProperties(
                $"{InteractionConstants.Friends.OverviewPage}:last:{totalPages - 1}",
                EmojiProperties.Custom(DiscordConstants.PagesLast), ButtonStyle.Secondary) { Disabled = page >= totalPages - 1 });
            response.ComponentsContainer.AddComponent(navRow);
        }

        AddLastFmSection(response, note);

        return response;
    }

    private static void AddLastFmSection(ResponseModel response, string note)
    {
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var text = "**Sync Last.fm friends**";
        text += !string.IsNullOrWhiteSpace(note)
            ? $"\n{note}"
            : "\n-# People you follow on Last.fm";

        response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
            new ButtonProperties(InteractionConstants.Friends.Sync, "Sync", ButtonStyle.Secondary))
        {
            Components =
            [
                new TextDisplayProperties(text)
            ]
        });
    }

    public async Task<string> SetFriendTypeAsync(ContextModel context, int friendId, FriendType newType)
    {
        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);
        var friend = friends.FirstOrDefault(f => f.FriendId == friendId);

        if (friend == null)
        {
            return "This friend could not be found.";
        }

        if (friend.FriendType == newType)
        {
            return null;
        }

        var isSupporter = context.ContextUser.UserType != UserType.User;
        var visibleCap = isSupporter ? Constants.MaxVisibleFriendsSupporter : Constants.MaxVisibleFriends;
        var visibleCount = friends.Count(f => f.FriendId != friendId && f.FriendType >= FriendType.VisibleInNowPlaying);
        var closeCount = friends.Count(f => f.FriendId != friendId && f.FriendType == FriendType.CloseFriend);

        if (newType == FriendType.CloseFriend)
        {
            if (!isSupporter)
            {
                return $"**Close friends are [a Supporter perk]({Constants.GetSupporterOverviewLink}).** They're always shown in your now playing list and pinned in WhoKnows regardless of their position.";
            }
            if (closeCount >= Constants.MaxCloseFriends)
            {
                return $"You can have at most **{Constants.MaxCloseFriends}** close friends. Change another close friend's type first.";
            }
            if (visibleCount >= visibleCap)
            {
                return $"You can show at most **{visibleCap}** friends in your now playing list. Hide another friend first.";
            }
        }
        else if (newType == FriendType.VisibleInNowPlaying && visibleCount >= visibleCap)
        {
            var supporterHint = isSupporter ? "" : $" Supporters can show up to {Constants.MaxVisibleFriendsSupporter}.";
            return $"You can show at most **{visibleCap}** friends in your now playing list. Hide another friend first.{supporterHint}";
        }

        await this._friendsService.SetFriendTypeAsync(friendId, newType);
        return null;
    }

    public async Task<string> RemoveFriendAsync(ContextModel context, int friendId)
    {
        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);
        var friend = friends.FirstOrDefault(f => f.FriendId == friendId);

        if (friend == null)
        {
            return "This friend could not be found.";
        }

        var friendName = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
        await this._friendsService.RemoveFriendByIdAsync(friendId);

        return $"Removed **{friendName}** from your friends.";
    }

    public async Task<(string Note, bool Success)> SyncLastFmFriendsAsync(ContextModel context)
    {
        var existingFriends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);
        var isSupporter = context.ContextUser.UserType != UserType.User;
        var totalCap = isSupporter ? Constants.MaxFriendsSupporter : Constants.MaxFriends;

        var remainingSlots = totalCap - existingFriends.Count;
        if (remainingSlots <= 0)
        {
            return ($"You've reached your friend limit of **{totalCap}**, so no Last.fm friends were synced.", false);
        }

        var friendsResponse = await this._dataSourceFactory.GetFriendsAsync(context.ContextUser.UserNameLastFM);

        if (!friendsResponse.Success || friendsResponse.Content?.Friends == null)
        {
            return ("Last.fm returned an error while fetching your friends. Please try again later.", false);
        }

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingFriends)
        {
            if (existing.LastFMUserName != null)
            {
                existingNames.Add(existing.LastFMUserName);
            }
            if (existing.FriendUser?.UserNameLastFM != null)
            {
                existingNames.Add(existing.FriendUser.UserNameLastFM);
            }
        }

        var candidateNames = friendsResponse.Content.Friends
            .Where(f => !string.IsNullOrWhiteSpace(f.UserName) && !existingNames.Contains(f.UserName))
            .Select(f => f.UserName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var registeredUserIds = await this._friendsService.GetRegisteredUserIdsAsync(candidateNames);
        var toAdd = candidateNames.Where(n => registeredUserIds.ContainsKey(n.ToLower())).ToList();

        if (toAdd.Count == 0)
        {
            return ("No friends to add - none of your Last.fm friends use .fmbot, or you've already added the ones that do.", false);
        }

        var capped = toAdd.Count > remainingSlots;
        if (capped)
        {
            toAdd = toAdd.Take(remainingSlots).ToList();
        }

        var added = await this._friendsService.AddLastFmFriendsAsync(context.ContextUser, toAdd, registeredUserIds);

        var note =
            $"Added **{added}** {StringExtensions.GetFriendsString(added)} from Last.fm. " +
            "Use ⚙️ to adjust their visibility.";
        if (capped)
        {
            note += $"\nSome friends weren't added because you reached your limit of **{totalCap}** friends.";
        }

        return (note, true);
    }

    public async Task<(string Note, bool Success)> RemoveSyncedLastFmFriendsAsync(ContextModel context)
    {
        var removed = await this._friendsService.RemoveSyncedLastFmFriendsAsync(context.ContextUser.UserId);

        return removed == 0
            ? ("No synced Last.fm friends to remove.", false)
            : ($"Removed **{removed}** synced Last.fm {StringExtensions.GetFriendsString(removed)}.", true);
    }
}
