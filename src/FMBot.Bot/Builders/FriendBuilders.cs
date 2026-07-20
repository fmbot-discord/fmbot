using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dasync.Collections;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
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
    private readonly AdminService _adminService;

    public FriendBuilders(FriendsService friendsService, UserService userService, GuildService guildService,
        IDataSourceFactory dataSourceFactory, UpdateService updateService, SettingService settingService, AdminService adminService)
    {
        this._friendsService = friendsService;
        this._userService = userService;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this._updateService = updateService;
        this._settingService = settingService;
        this._adminService = adminService;
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
                context.Localize("friends.noFriendsFound",
                    ("command", $"{context.Prefix}friendsadd {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}"),
                    ("appCommand", "'Add as friend'"))));
            response.ComponentsContainer.AddComponent(FriendButtons(context));
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var visibleFriends = friends
            .Where(f => f.FriendType >= FriendType.VisibleInNowPlaying)
            .ToList();

        if (visibleFriends.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                context.LocalizeCount("friends.noneVisible", friends.Count)));
            response.ComponentsContainer.AddComponent(FriendButtons(context));
            return response;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);

        var title = context.LocalizeCount("friends.nowPlayingTitle", visibleFriends.Count,
            ("user", await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)));

        var totalPlaycount = 0;
        var friendResult = new ConcurrentBag<FriendResult>();
        await visibleFriends.ParallelForEachAsync(async friend =>
        {
            var friendUsername = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
            var friendNameToDisplay = friendUsername;

            var userAccounts = await this._adminService.GetUsersWithLfmUsernameAsync(friendUsername);
            var lastUsedFriendUser = userAccounts
                .OrderBy(o => o.LastUsed == null)
                .ThenByDescending(o => o.LastUsed)
                .FirstOrDefault();

            if (lastUsedFriendUser != null && lastUsedFriendUser.UserId != friend.FriendUserId)
            {
                friend.FriendUserId = lastUsedFriendUser.UserId;
                friend.FriendUser = lastUsedFriendUser;
            }

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
                track = context.Localize("friends.couldNotRetrieve", ("error", $"{tracks.Error}"));
            }
            else if (!tracks.Content.RecentTracks.Any())
            {
                track = context.Localize("errors.noScrobblesShort");
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
                    track += $" ({context.Localizer.TimeAgoShort(lastTrack.TimePlayed.Value)})";
                }

                Interlocked.Add(ref totalPlaycount, (int)tracks.Content.TotalAmount);
            }

            friendResult.Add(new FriendResult(timePlayed,
                $"**{StringExtensions.MarkdownLink(friendNameToDisplay, LastfmUrlExtensions.GetUserUrl(friendUsername))}** | {track}"));
        }, maxDegreeOfParallelism: 8);

        var friendsFooter =
            $"-# {context.LocalizeCount("footer.totalScrobbles", totalPlaycount)} - {context.LocalizeCount("friends.totalFriends", friends.Count)}";

        var orderedFriends = friendResult
            .OrderByDescending(o => o.TimePlayed)
            .ThenBy(o => o.Result)
            .ToList();

        const int friendsPerPage = 12;

        var pages = orderedFriends.ChunkBy(friendsPerPage);

        if (pages.Count <= 1)
        {
            AddFriendsContent(response.ComponentsContainer, orderedFriends);
            response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
                new ButtonProperties(InteractionConstants.Friends.Overview, context.Localize("buttons.manage"), ButtonStyle.Secondary))
            {
                Components =
                [
                    new TextDisplayProperties(friendsFooter)
                ]
            });
            return response;
        }

        response.ResponseType = ResponseType.Paginator;
        response.ComponentPaginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(pages.Count)
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        return response;

        void AddFriendsContent(ComponentContainerProperties container, IEnumerable<FriendResult> pageFriends)
        {
            var friendsText = new StringBuilder();
            foreach (var friend in pageFriends)
            {
                friendsText.AppendLine(friend.Result);
            }

            container.AddComponent(new TextDisplayProperties($"### {title}"));
            container.AddComponent(new ComponentSeparatorProperties());
            container.AddComponent(new TextDisplayProperties(friendsText.ToString().TrimEnd()));
            container.AddComponent(new ComponentSeparatorProperties());
        }

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();
            container.WithAccentColor(DiscordConstants.LastFmColorRed);

            AddFriendsContent(container, pages[p.CurrentPageIndex]);

            container.AddComponent(new TextDisplayProperties(friendsFooter));

            container.AddComponent(new ActionRowProperties()
                .AddPreviousButton(p, style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PagesPrevious))
                .AddNextButton(p, style: ButtonStyle.Secondary,
                    emote: EmojiProperties.Custom(DiscordConstants.PagesNext))
                .WithButton(context.Localize("buttons.manage"), customId: InteractionConstants.Friends.Overview,
                    style: ButtonStyle.Secondary));

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(NetCord.MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    private static ActionRowProperties FriendButtons(ContextModel context)
    {
        return new ActionRowProperties()
            .WithButton(context.Localize("buttons.manageFriends"),
                customId: InteractionConstants.Friends.Overview,
                style: ButtonStyle.Secondary);
    }

    public static ResponseModel FriendInputInstructions(ContextModel context, bool removing)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
            CommandResponse = CommandResponse.WrongInput,
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);

        if (removing)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                context.Localize("friends.removeInstructions",
                    ("command", $"{context.Prefix}removefriend {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}"))));
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                context.Localize("friends.addInstructions",
                    ("command", $"{context.Prefix}addfriend {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}"),
                    ("appCommand", "'Add as friend'"))));
        }

        response.ComponentsContainer.AddComponent(FriendButtons(context));

        return response;
    }

    public async Task<ResponseModel> AddFriendsAsync(ContextModel context, string[] enteredFriends)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var addedFriendsList = new List<(string Name, FriendType Type, int FriendId)>();
        var friendNotFoundList = new List<string>();
        var duplicateFriendsList = new List<(string Name, FriendType Type, int FriendId)>();

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

                    var friendId = await this._friendsService.AddLastFmFriendAsync(context.ContextUser, friendUsername,
                        friendUserId, friendType);
                    addedFriendsList.Add((friendUsername, friendType, friendId));
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
                duplicateFriendsList.Add((friendUsername, existingMatch?.FriendType ?? FriendType.Normal,
                    existingMatch?.FriendId ?? 0));
            }
        }

        var body = new StringBuilder();

        if (addedFriendsList.Count > 0)
        {
            body.AppendLine(context.LocalizeCount("friends.addedFriends", addedFriendsList.Count));
            foreach (var addedFriend in addedFriendsList)
            {
                body.AppendLine(
                    $"- *[{addedFriend.Name}]({LastfmUrlExtensions.GetUserUrl(addedFriend.Name)})* — {addedFriend.Type.GetAttribute<OptionAttribute>().Name}");
            }
        }

        if (friendNotFoundList.Count > 0)
        {
            if (body.Length > 0)
            {
                body.AppendLine();
            }

            body.AppendLine(context.LocalizeCount("friends.couldNotAdd", friendNotFoundList.Count));
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

            body.AppendLine(context.Localize("friends.alreadyFriends"));
            foreach (var dupeFriend in duplicateFriendsList)
            {
                body.AppendLine(
                    $"- *[{dupeFriend.Name}]({LastfmUrlExtensions.GetUserUrl(dupeFriend.Name)})* — {dupeFriend.Type.GetAttribute<OptionAttribute>().Name}");
            }
        }

        if (body.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(body.ToString().TrimEnd()));

            var buttons = new ActionRowProperties()
                .WithButton(context.Localize("buttons.manageFriends"), customId: InteractionConstants.Friends.Overview,
                    style: ButtonStyle.Secondary);
            if (addedFriendsList.Count == 1)
            {
                buttons.WithButton(context.Localize("buttons.changeType"),
                    customId: $"{InteractionConstants.Friends.Manage}:{addedFriendsList[0].FriendId}:0:add",
                    style: ButtonStyle.Secondary);
            }
            else if (addedFriendsList.Count == 0 && duplicateFriendsList.Count == 1 &&
                     duplicateFriendsList[0].FriendId > 0)
            {
                buttons.WithButton(context.Localize("buttons.changeType"),
                    customId: $"{InteractionConstants.Friends.Manage}:{duplicateFriendsList[0].FriendId}:0:add",
                    style: ButtonStyle.Secondary);
            }

            response.ComponentsContainer.AddComponent(buttons);
        }

        if (friendLimitReached)
        {
            if (body.Length > 0)
            {
                response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            }

            if (context.ContextUser.UserType == UserType.User)
            {
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                    context.Localize("friends.limitReached",
                        ("max", Constants.MaxFriends.ToString()),
                        ("supporterMax", Constants.MaxFriendsSupporter.ToString()))));
                response.Components = new ActionRowProperties().WithButton(Constants.GetSupporterButton,
                    style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "friends-limit"));
            }
            else
            {
                response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                    context.Localize("friends.limitReachedSupporter",
                        ("max", Constants.MaxFriendsSupporter.ToString()))));
            }
        }

        if (context.ContextUser.UserType != UserType.User && !friendLimitReached &&
            existingFriends.Count >= Constants.MaxFriendsSupporter - 5)
        {
            var userType = context.ContextUser.UserType.ToString().ToLower();
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                context.Localize("friends.thanksUserType",
                    ("userType", userType),
                    ("max", Constants.MaxFriendsSupporter.ToString()))));
        }

        response.ComponentsContainer.WithAccentColor(addedFriendsList.Count > 0
            ? DiscordConstants.SuccessColorGreen
            : DiscordConstants.WarningColorOrange);

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
            body.AppendLine(context.LocalizeCount("friends.removedFriends", removedFriendsList.Count));
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

            body.AppendLine(context.LocalizeCount("friends.couldNotRemove", failedRemoveFriends.Count));
            foreach (var failedRemovedFriend in failedRemoveFriends)
            {
                body.AppendLine($"- *[{failedRemovedFriend}]({LastfmUrlExtensions.GetUserUrl(failedRemovedFriend)})*");
            }
        }

        if (removedFriendsList.Count == 0 && failedRemoveFriends.Count == 0)
        {
            body.AppendLine(contextCommand
                ? context.Localize("friends.notInFriendList")
                : context.Localize("friends.noneToRemove"));
        }

        response.ComponentsContainer.AddComponent(new TextDisplayProperties(body.ToString().TrimEnd()));
        response.ComponentsContainer.AddComponent(new ActionRowProperties()
            .WithButton(context.Localize("buttons.manageFriends"), customId: InteractionConstants.Friends.Overview,
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
            response.Embed.WithDescription(context.Localize("friends.onlyInDms"));
            response.CommandResponse = CommandResponse.OnlySupportedInDm;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var friended = await this._friendsService.GetFriendedAsync(context.ContextUser.UserId);

        if (friended?.Any() != true)
        {
            response.Embed.WithDescription(context.Localize("friends.nobodyAddedYou"));
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        response.EmbedAuthor.WithName(context.Localize("friends.friendedTitle"));

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
                .WithFooter(context.LocalizeCount("friends.friendedPageCounter", friended?.Count ?? 0,
                    ("page", pageCounter.ToString()), ("pages", friendedPages.Count.ToString()))));

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

        var header = context.Localize("friends.manageHeader",
            ("friends", friends.Count.ToString()), ("max", totalCap.ToString()),
            ("visible", visibleCount.ToString()), ("visibleMax", visibleCap.ToString()));
        if (isSupporter)
        {
            header += $" · {context.Localize("friends.manageHeaderCloseFriends",
                ("close", closeCount.ToString()), ("max", Constants.MaxCloseFriends.ToString()))}";
        }

        response.ComponentsContainer.AddComponent(new TextDisplayProperties(
            context.Localize("friends.manageTitle") + "\n" + header));

        if (friends.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(
                context.Localize("friends.manageNoFriends", ("command", "addfriends"))));
            AddLastFmSection(context, response, note);
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
            var typeLabel = friend.FriendType.GetAttribute<OptionAttribute>().Name;
            var lastFmTag = friend.LastFmFriend ? " · `Last.fm`" : "";

            response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
                new ButtonProperties($"{InteractionConstants.Friends.Manage}:{friend.FriendId}:{page}:manage",
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

            var navRow = new ActionRowProperties
            {
                new ButtonProperties(
                    $"{InteractionConstants.Friends.OverviewPage}:first:0",
                    EmojiProperties.Custom(DiscordConstants.PagesFirst), ButtonStyle.Secondary) { Disabled = page == 0 },
                new ButtonProperties(
                    $"{InteractionConstants.Friends.OverviewPage}:prev:{page - 1}",
                    EmojiProperties.Custom(DiscordConstants.PagesPrevious), ButtonStyle.Secondary) { Disabled = page == 0 },
                new ButtonProperties(
                    $"{InteractionConstants.Friends.OverviewPage}:current:{page}",
                    $"{page + 1}/{totalPages}", ButtonStyle.Secondary) { Disabled = true },
                new ButtonProperties(
                    $"{InteractionConstants.Friends.OverviewPage}:next:{page + 1}",
                    EmojiProperties.Custom(DiscordConstants.PagesNext), ButtonStyle.Secondary) { Disabled = page >= totalPages - 1 },
                new ButtonProperties(
                    $"{InteractionConstants.Friends.OverviewPage}:last:{totalPages - 1}",
                    EmojiProperties.Custom(DiscordConstants.PagesLast), ButtonStyle.Secondary) { Disabled = page >= totalPages - 1 }
            };
            response.ComponentsContainer.AddComponent(navRow);
        }

        AddLastFmSection(context, response, note);

        return response;
    }

    private static void AddLastFmSection(ContextModel context, ResponseModel response, string note)
    {
        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());

        var text = context.Localize("friends.syncSectionTitle");
        text += !string.IsNullOrWhiteSpace(note)
            ? $"\n{note}"
            : $"\n{context.Localize("friends.syncSectionSubtitle")}";

        response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
            new ButtonProperties(InteractionConstants.Friends.Sync, context.Localize("buttons.sync"), ButtonStyle.Secondary))
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
            return context.Localize("friends.friendNotFound");
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
                return context.Localize("friends.closeFriendsSupporterPerk",
                    ("url", Constants.GetSupporterOverviewLink));
            }

            if (closeCount >= Constants.MaxCloseFriends)
            {
                return context.Localize("friends.closeFriendLimit",
                    ("max", Constants.MaxCloseFriends.ToString()));
            }

            if (visibleCount >= visibleCap)
            {
                return context.Localize("friends.visibleLimit", ("max", visibleCap.ToString()));
            }
        }
        else if (newType == FriendType.VisibleInNowPlaying && visibleCount >= visibleCap)
        {
            var supporterHint = isSupporter
                ? ""
                : $" {context.Localize("friends.visibleLimitSupporterHint", ("max", Constants.MaxVisibleFriendsSupporter.ToString()))}";
            return $"{context.Localize("friends.visibleLimit", ("max", visibleCap.ToString()))}{supporterHint}";
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
            return context.Localize("friends.friendNotFound");
        }

        var friendName = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
        await this._friendsService.RemoveFriendByIdAsync(friendId);

        return context.Localize("friends.removedFriend", ("name", friendName));
    }

    public async Task<(string Note, bool Success)> ApplyFriendTypeSelectionAsync(ContextModel context, int friendId,
        string selectedValue)
    {
        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);
        var friend = friends.FirstOrDefault(f => f.FriendId == friendId);

        if (friend == null)
        {
            return (context.Localize("friends.friendNotFound"), false);
        }

        var friendName = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;

        if (selectedValue == "remove")
        {
            await this._friendsService.RemoveFriendByIdAsync(friendId);
            return (context.Localize("friends.removedFriend", ("name", friendName)), true);
        }

        if (!int.TryParse(selectedValue, out var typeValue) || !Enum.IsDefined(typeof(FriendType), typeValue))
        {
            return (context.Localize("friends.optionNotProcessed"), false);
        }

        var newType = (FriendType)typeValue;
        var error = await this.SetFriendTypeAsync(context, friendId, newType);
        if (error != null)
        {
            return (error, false);
        }

        return (context.Localize("friends.setFriendType",
            ("name", friendName), ("type", newType.GetAttribute<OptionAttribute>().Name)), true);
    }

    public async Task<(string Note, bool Success)> SyncLastFmFriendsAsync(ContextModel context)
    {
        var existingFriends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);
        var isSupporter = context.ContextUser.UserType != UserType.User;
        var totalCap = isSupporter ? Constants.MaxFriendsSupporter : Constants.MaxFriends;

        var remainingSlots = totalCap - existingFriends.Count;
        if (remainingSlots <= 0)
        {
            return (context.Localize("friends.syncLimitReached", ("max", totalCap.ToString())), false);
        }

        var friendsResponse = await this._dataSourceFactory.GetFriendsAsync(context.ContextUser.UserNameLastFM);

        if (!friendsResponse.Success || friendsResponse.Content?.Friends == null)
        {
            return (context.Localize("friends.syncLastFmError"), false);
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
            return (context.Localize("friends.syncNoneToAdd"), false);
        }

        var capped = toAdd.Count > remainingSlots;
        if (capped)
        {
            toAdd = toAdd.Take(remainingSlots).ToList();
        }

        var added = await this._friendsService.AddLastFmFriendsAsync(context.ContextUser, toAdd, registeredUserIds);

        var note = context.LocalizeCount("friends.syncAdded", added);
        if (capped)
        {
            note += $"\n{context.Localize("friends.syncCapped", ("max", totalCap.ToString()))}";
        }

        return (note, true);
    }

    public async Task<(string Note, bool Success)> RemoveSyncedLastFmFriendsAsync(ContextModel context)
    {
        var removed = await this._friendsService.RemoveSyncedLastFmFriendsAsync(context.ContextUser.UserId);

        return removed == 0
            ? (context.Localize("friends.syncNoneToRemove"), false)
            : (context.LocalizeCount("friends.syncRemoved", removed), true);
    }
}
