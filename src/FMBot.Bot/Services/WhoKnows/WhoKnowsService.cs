using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Core;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord.Rest;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsService
{
    public static ResponseModel IndexRequiredResponse(ContextModel context, ResponseModel response)
    {
        response.ResponseType = ResponseType.Embed;
        response.Embed.WithDescription(context.Localize("errors.indexRequired",
            ("refreshCommand", $"{context.Prefix}refreshmembers")));
        response.CommandResponse = CommandResponse.IndexRequired;
        return response;
    }

    private readonly GlobalWhoKnowsFilter _globalFilter;

    public WhoKnowsService(GlobalWhoKnowsFilter globalFilter)
    {
        this._globalFilter = globalFilter;
    }

    public static async Task<IList<WhoKnowsObjectWithUser>> AddOrReplaceUserToIndexList(
        IList<WhoKnowsObjectWithUser> users, User contextUser, string name, NetCord.Gateway.Guild discordGuild = null,
        long? playcount = null)
    {
        if (!playcount.HasValue)
        {
            return users;
        }

        NetCord.GuildUser netcordGuildUser = null;
        if (discordGuild != null)
        {
            netcordGuildUser = await discordGuild.GetCachedGuildUserAsync(contextUser.DiscordUserId);
        }

        var guildUser = new GuildUser
        {
            UserName = netcordGuildUser != null ? netcordGuildUser.GetDisplayName() : contextUser.UserNameLastFM,
            Roles = netcordGuildUser?.RoleIds?.ToArray(),
            LastMessage = DateTime.UtcNow,
            User = contextUser
        };

        var existingUsers = users
            .Where(f => f.LastFMUsername.ToLower() == guildUser.User.UserNameLastFM.ToLower());
        if (existingUsers.Any())
        {
            users = users
                .Where(f => f.LastFMUsername.ToLower() != guildUser.User.UserNameLastFM.ToLower())
                .ToList();
        }

        var userPlaycount = int.Parse(playcount.GetValueOrDefault(0).ToString());
        users.Add(new WhoKnowsObjectWithUser
        {
            UserId = guildUser.User.UserId,
            Name = name,
            Playcount = userPlaycount,
            LastFMUsername = guildUser.User.UserNameLastFM,
            LastUsed = guildUser.User.LastUsed,
            LastMessage = guildUser.LastMessage,
            DiscordName = guildUser.UserName,
            PrivacyLevel = PrivacyLevel.Global,
            Roles = guildUser.Roles
        });

        return users.OrderByDescending(o => o.Playcount).ToList();
    }

    public static (FilterStats stats, IDictionary<int, FullGuildUser> filteredGuildUsers) FilterGuildUsers(
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        int contextUserId,
        List<ulong> roles = null)
    {
        return WhoKnowsFilter.FilterGuildUsers(guildUsers, guild,
            PremiumGuildLookup.IsPremiumGuild(guild.DiscordGuildId), contextUserId, roles);
    }

    public static (FilterStats stats, List<WhoKnowsObjectWithUser> filteredUsers) FilterWhoKnowsObjects(
        ICollection<WhoKnowsObjectWithUser> users,
        IDictionary<int, FullGuildUser> guildUsers,
        Persistence.Domain.Models.Guild guild,
        int contextUserId,
        List<ulong> roles = null,
        bool filterDisabled = false)
    {
        return WhoKnowsFilter.FilterWhoKnowsObjects(users, guildUsers, guild,
            PremiumGuildLookup.IsPremiumGuild(guild.DiscordGuildId), contextUserId, roles, filterDisabled);
    }

    public Task<IList<WhoKnowsObjectWithUser>> FilterGlobalUsersAsync(IEnumerable<WhoKnowsObjectWithUser> users,
        bool qualityFilterDisabled = false)
    {
        return this._globalFilter.FilterGlobalUsersAsync(users, qualityFilterDisabled);
    }

    public const string GlobalFilterSetsCacheKey = GlobalWhoKnowsFilter.CacheKey;

    public static StringBuilder GetGlobalWhoKnowsFooter(StringBuilder footer, WhoKnowsSettings settings,
        ContextModel context)
    {
        if (settings.AdminView)
        {
            footer.AppendLine($"Admin view enabled - not for public channels");
        }

        if (settings.QualityFilterDisabled)
        {
            footer.AppendLine(context.Localize("whoknows.globalFilterDisabled"));
        }

        if (context.ContextUser.PrivacyLevel != PrivacyLevel.Global)
        {
            footer.AppendLine(context.Localize("whoknows.notGloballyVisible", ("command", $"{context.Prefix}privacy")));
        }

        if (settings.HidePrivateUsers)
        {
            footer.AppendLine(context.Localize("whoknows.privateUsersHidden"));
        }

        return footer;
    }

    public static IList<WhoKnowsObjectWithUser> ShowGuildMembersInGlobalWhoKnowsAsync(
        IList<WhoKnowsObjectWithUser> users, IDictionary<int, FullGuildUser> guildUsers)
    {
        return WhoKnowsFilter.ShowGuildMembersInGlobalWhoKnows(users, guildUsers);
    }

    public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId,
        PrivacyLevel minPrivacyLevel, Localizer localizer, CrownModel crownModel = null,
        bool hidePrivateUsers = false, bool doNotLinkEmojis = false, HashSet<int> closeFriendUserIds = null)
    {
        var reply = new StringBuilder();

        var whoKnowsCount = whoKnowsObjects.Count;
        if (whoKnowsCount > 14)
        {
            whoKnowsCount = 14;
        }

        var usersToShow = whoKnowsObjects
            .OrderByDescending(o => o.Playcount)
            .ToList();

        var spacer = crownModel?.Crown == null ? "" : " ";

        var indexNumber = 1;
        var timesNameAdded = 0;
        var requestedUserAdded = false;
        var addedUsers = new HashSet<int>();
        var addedLastFmUsers = new HashSet<string>();

        // Note: You might not be able to see them, but this code contains specific spacers
        // https://www.compart.com/en/unicode/category/Zs
        for (var index = 0; timesNameAdded < whoKnowsCount; index++)
        {
            if (index >= usersToShow.Count)
            {
                break;
            }

            var user = usersToShow[index];

            if (addedUsers.Any(a => a.Equals(user.UserId)) ||
                addedLastFmUsers.Any(a => a.Equals(user.LastFMUsername)))
            {
                continue;
            }

            string nameWithLink;
            if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
            {
                nameWithLink = PrivateName(localizer);
                if (hidePrivateUsers)
                {
                    indexNumber += 1;
                    continue;
                }
            }
            else
            {
                nameWithLink = NameWithLink(user, doNotLinkEmojis);
                if (user.UserId == requestedUserId)
                {
                    nameWithLink = $"**{nameWithLink}";
                }
            }

            var positionCounter = $"{spacer}{indexNumber}.";
            positionCounter = user.UserId == requestedUserId
                ? user.SameServer == true ? $"__**{positionCounter}** __" : $"**{positionCounter}** "
                : user.SameServer == true
                    ? $"__{positionCounter}__ "
                    : $"{positionCounter} ";

            if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
            {
                positionCounter = "👑 ";
            }

            var afterPositionSpacer = index + 1 == 10 ? "" : (index + 1 == 7 || index + 1 == 9) ? " " : " ";

            reply.Append($"{positionCounter}{afterPositionSpacer}{nameWithLink}");

            if (user.UserId == requestedUserId)
            {
                reply.Append($" - {localizer.TranslateCount("shared.plays", user.Playcount)}**\n");
            }
            else
            {
                reply.Append($" - {localizer.TranslateCount("shared.playsBold", user.Playcount)}\n");
            }

            indexNumber += 1;
            timesNameAdded += 1;

            addedUsers.Add(user.UserId);
            addedLastFmUsers.Add(user.LastFMUsername);

            if (user.UserId == requestedUserId)
            {
                requestedUserAdded = true;
            }
        }

        var pinnedUsers = new List<WhoKnowsObjectWithUser>();

        if (!requestedUserAdded)
        {
            var requestedUser = usersToShow.FirstOrDefault(f => f.UserId == requestedUserId);
            if (requestedUser != null)
            {
                pinnedUsers.Add(requestedUser);
            }
        }

        if (closeFriendUserIds is { Count: > 0 })
        {
            foreach (var closeFriend in usersToShow
                         .Where(w => closeFriendUserIds.Contains(w.UserId) && w.UserId != requestedUserId &&
                                     !addedUsers.Contains(w.UserId))
                         .GroupBy(g => g.UserId)
                         .Select(s => s.First()))
            {
                if (minPrivacyLevel == PrivacyLevel.Global && closeFriend.PrivacyLevel != PrivacyLevel.Global)
                {
                    continue;
                }

                pinnedUsers.Add(closeFriend);
                addedUsers.Add(closeFriend.UserId);
            }
        }

        foreach (var pinnedUser in pinnedUsers.OrderByDescending(o => o.Playcount))
        {
            var nameWithLink = NameWithLink(pinnedUser, doNotLinkEmojis);
            var rank = usersToShow.IndexOf(pinnedUser) + 1;

            if (pinnedUser.UserId == requestedUserId)
            {
                reply.Append($"**{spacer}{rank}.  {nameWithLink}  - {localizer.TranslateCount("shared.plays", pinnedUser.Playcount)}**\n");
            }
            else
            {
                reply.Append(
                    $"{spacer}{rank}.  *{nameWithLink}* - {localizer.TranslateCount("shared.playsBold", pinnedUser.Playcount)}\n");
            }
        }

        if (crownModel?.CrownResult != null)
        {
            reply.Append($"\n{crownModel.CrownResult}");
        }

        return reply.ToString();
    }

    public static string NameWithLink(WhoKnowsObjectWithUser user, bool doNotLinkEmojis = false)
    {
        var discordName = user.DiscordName != null
            ? StringExtensions.Sanitize(user.DiscordName
                .Replace("[", "")
                .Replace("]", "")
                .Replace(" ", "")
                .Replace("ٴ", ""))
            : null;

        if (string.IsNullOrWhiteSpace(discordName))
        {
            discordName = user.LastFMUsername;
        }

        if (doNotLinkEmojis && user.DiscordName.ContainsEmoji())
        {
            return $"\u2066{discordName}\u2069";
        }

        var nameWithLink = $"[\u2066{discordName}\u2069]({LastfmUrlExtensions.GetUserUrl(user.LastFMUsername)})";
        return nameWithLink;
    }

    private static string PrivateName(Localizer localizer)
    {
        return localizer.Translate("whoknows.privateUser");
    }

    public static ComponentPaginatorBuilder CreateWhoKnowsPaginator(
        IList<WhoKnowsObjectWithUser> whoKnowsObjects,
        int requestedUserId,
        PrivacyLevel minPrivacyLevel,
        Localizer localizer,
        string title,
        string footerText,
        CrownModel crownModel = null,
        bool hidePrivateUsers = false,
        int usersPerPage = 10,
        HashSet<int> closeFriendUserIds = null)
    {
        var usersToShow = whoKnowsObjects
            .OrderByDescending(o => o.Playcount)
            .ToList();

        var deduplicated = new List<WhoKnowsObjectWithUser>();
        var addedUsers = new HashSet<int>();
        var addedLastFmUsers = new HashSet<string>();

        foreach (var user in usersToShow)
        {
            if (addedUsers.Contains(user.UserId) ||
                addedLastFmUsers.Contains(user.LastFMUsername))
            {
                continue;
            }

            if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global && hidePrivateUsers)
            {
                continue;
            }

            addedUsers.Add(user.UserId);
            addedLastFmUsers.Add(user.LastFMUsername);
            deduplicated.Add(user);
        }

        var pages = deduplicated
            .ChunkBy(usersPerPage)
            .ToList();

        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        var requestedUser = deduplicated.FirstOrDefault(f => f.UserId == requestedUserId);
        var requestedUserIndex = requestedUser != null ? deduplicated.IndexOf(requestedUser) + 1 : -1;

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(pages.Count)
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        return paginator;

        IPage GeneratePage(IComponentPaginator p)
        {
            var pageIndex = p.CurrentPageIndex;
            var pageUsers = pages.ElementAtOrDefault(pageIndex) ?? [];

            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var description = new StringBuilder();
            var indexNumber = pageIndex * usersPerPage + 1;
            var requestedUserOnPage = false;

            foreach (var user in pageUsers)
            {
                string nameWithLink;
                if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
                {
                    nameWithLink = PrivateName(localizer);
                }
                else
                {
                    nameWithLink = NameWithLink(user, true);
                    if (user.UserId == requestedUserId)
                    {
                        nameWithLink = $"**{nameWithLink}";
                    }
                }

                var positionCounter = $"{indexNumber}.";
                positionCounter = user.UserId == requestedUserId
                    ? user.SameServer == true ? $"__{positionCounter}__" : $"{positionCounter} "
                    : user.SameServer == true
                        ? $"__{positionCounter}__ "
                        : $"{positionCounter} ";

                if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
                {
                    positionCounter = "👑 ";
                }

                description.Append($"{positionCounter} {nameWithLink}");

                if (user.UserId == requestedUserId)
                {
                    description.Append($" - {localizer.TranslateCount("shared.plays", user.Playcount)}**\n");
                    requestedUserOnPage = true;
                }
                else
                {
                    description.Append($" - {localizer.TranslateCount("shared.playsBold", user.Playcount)}\n");
                }

                indexNumber++;
            }

            if (description.Length == 0)
            {
                description.Append(localizer.Translate("whoknows.noListenersFound"));
            }

            container.WithTextDisplay(description.ToString());

            if (pageIndex == 0 && !requestedUserOnPage && requestedUser != null)
            {
                container.WithSeparator();

                var reqNameWithLink = NameWithLink(requestedUser, true);
                container.WithTextDisplay(
                    $"**{requestedUserIndex}.  {reqNameWithLink}  - {localizer.TranslateCount("shared.plays", requestedUser.Playcount)}**");
            }

            if (pageIndex == 0 && closeFriendUserIds is { Count: > 0 })
            {
                var shownOnPage = new HashSet<int>(pageUsers.Select(u => u.UserId));
                var closeFriendsBuilder = new StringBuilder();

                foreach (var closeFriend in deduplicated
                             .Where(w => closeFriendUserIds.Contains(w.UserId) && w.UserId != requestedUserId &&
                                         !shownOnPage.Contains(w.UserId)))
                {
                    if (minPrivacyLevel == PrivacyLevel.Global && closeFriend.PrivacyLevel != PrivacyLevel.Global)
                    {
                        continue;
                    }

                    var cfNameWithLink = NameWithLink(closeFriend, true);
                    closeFriendsBuilder.Append(
                        $"{deduplicated.IndexOf(closeFriend) + 1}.  *{cfNameWithLink}*  - {localizer.TranslateCount("shared.playsBold", closeFriend.Playcount)}\n");
                }

                if (closeFriendsBuilder.Length > 0)
                {
                    container.WithSeparator();
                    container.WithTextDisplay(closeFriendsBuilder.ToString().TrimEnd());
                }
            }

            container.WithSeparator();

            var footerBuilder = new StringBuilder();
            footerBuilder.Append(localizer.Translate("shared.pageCounter", ("page", (pageIndex + 1).ToString()),
                ("pages", pages.Count.ToString())));

            if (!string.IsNullOrWhiteSpace(footerText))
            {
                footerBuilder.Append($"\n{footerText.TrimEnd()}");
            }

            if (crownModel?.CrownResult != null)
            {
                footerBuilder.Append($"\n{crownModel.CrownResult}");
            }

            var footer = "-# " + footerBuilder
                .Replace("\n", "\n-# ")
                .ToString()
                .TrimEnd("\n-# ")
                .ToString();
            container.WithTextDisplay(footer);

            container.WithActionRow(StringService.GetPaginationActionRow(p));

            var pageBuilder = new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(NetCord.MessageFlags.IsComponentsV2)
                .WithComponents([container]);

            return pageBuilder.Build();
        }
    }
}
