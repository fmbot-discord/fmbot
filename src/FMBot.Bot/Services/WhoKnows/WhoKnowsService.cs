using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows;

public class WhoKnowsService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public WhoKnowsService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public static async Task<IList<WhoKnowsObjectWithUser>> AddOrReplaceUserToIndexList(IList<WhoKnowsObjectWithUser> users, User contextUser, string name, IGuild discordGuild = null, long? playcount = null)
    {
        if (!playcount.HasValue)
        {
            return users;
        }

        IGuildUser discordGuildUser = null;
        if (discordGuild != null)
        {
            discordGuildUser = await discordGuild.GetUserAsync(contextUser.DiscordUserId);
        }

        var guildUser = new GuildUser
        {
            UserName = discordGuildUser != null ? discordGuildUser.DisplayName : contextUser.UserNameLastFM,
            Roles = discordGuildUser?.RoleIds?.ToArray(),
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

    public static (FilterStats stats, List<WhoKnowsObjectWithUser>) FilterWhoKnowsObjectsAsync(
        ICollection<WhoKnowsObjectWithUser> users,
        Persistence.Domain.Models.Guild guild,
        List<ulong> roles = null)
    {
        var stats = new FilterStats
        {
            StartCount = users.Count,
            RequesterFiltered = false,
            Roles = roles
        };

        if (guild.ActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.LastUsed != null &&
                    w.LastUsed >= DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                .ToList();

            stats.ActivityThresholdFiltered = preFilterCount - users.Count;
        }
        if (guild.UserActivityThresholdDays.HasValue)
        {
            var preFilterCount = users.Count;

            users = users.Where(w =>
                    w.LastMessage != null &&
                    w.LastMessage >= DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value))
                .ToList();

            stats.GuildActivityThresholdFiltered = preFilterCount - users.Count;
        }
        if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
        {
            var preFilterCount = users.Count;

            var usersToFilter = guild.GuildBlockedUsers
                .DistinctBy(d => d.UserId)
                .Where(w => w.BlockedFromWhoKnows)
                .Select(s => s.UserId)
                .ToHashSet();

            users = users
                .Where(w => !usersToFilter.Contains(w.UserId))
                .ToList();

            stats.BlockedFiltered = preFilterCount - users.Count;
        }
        if (guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && guild.AllowedRoles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.AllowedRolesFiltered = preFilterCount - users.Count;
        }
        if (guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && !guild.BlockedRoles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.BlockedRolesFiltered = preFilterCount - users.Count;
        }
        if (roles != null && roles.Any())
        {
            var preFilterCount = users.Count;

            users = users
                .Where(w => w.Roles != null && roles.Any(a => w.Roles.Contains(a)))
                .ToList();

            stats.ManualRoleFilter = preFilterCount - users.Count;
        }

        stats.EndCount = users.Count;

        return (stats, users.ToList());
    }

    public async Task<IList<WhoKnowsObjectWithUser>> FilterGlobalUsersAsync(IEnumerable<WhoKnowsObjectWithUser> users)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var bottedUsers = await db.BottedUsers
            .AsQueryable()
            .Where(w => w.BanActive)
            .ToListAsync();

        var userNamesToFilter = bottedUsers
            .DistinctBy(d => d.UserNameLastFM.ToLower())
            .Select(s => s.UserNameLastFM.ToLower())
            .ToHashSet();

        var userDatesToFilter = bottedUsers
            .Where(we => we.LastFmRegistered != null)
            .DistinctBy(d => d.LastFmRegistered)
            .Select(s => s.LastFmRegistered)
            .ToHashSet();

        return users
            .Where(w =>
                !userNamesToFilter
                    .Contains(w.LastFMUsername.ToLower())
                &&
                !userDatesToFilter
                    .Contains(w.RegisteredLastFm))
            .ToList();
    }

    public static IList<WhoKnowsObjectWithUser> ShowGuildMembersInGlobalWhoKnowsAsync(IList<WhoKnowsObjectWithUser> users, IDictionary<int, FullGuildUser> guildUsers)
    {
        foreach (var user in users.Where(w => guildUsers.ContainsKey(w.UserId)))
        {
            user.PrivacyLevel = PrivacyLevel.Global;
            user.SameServer = true;
        }

        return users;
    }

    public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId,
        PrivacyLevel minPrivacyLevel, CrownModel crownModel = null, bool hidePrivateUsers = false)
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
        var addedUsers = new List<int>();

        // Note: You might not be able to see them, but this code contains specific spacers
        // https://www.compart.com/en/unicode/category/Zs
        for (var index = 0; timesNameAdded < whoKnowsCount; index++)
        {
            if (index >= usersToShow.Count)
            {
                break;
            }

            var user = usersToShow[index];

            if (addedUsers.Any(a => a.Equals(user.UserId)))
            {
                continue;
            }

            string nameWithLink;
            if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
            {
                nameWithLink = PrivateName();
                if (hidePrivateUsers)
                {
                    indexNumber += 1;
                    continue;
                }
            }
            else
            {
                nameWithLink = NameWithLink(user);
                if (user.UserId == requestedUserId)
                {
                    nameWithLink = $"**{nameWithLink}**";
                }
            }

            var playString = StringExtensions.GetPlaysString(user.Playcount);

            var positionCounter = $"{spacer}{indexNumber}.";
            positionCounter = user.UserId == requestedUserId ?
                user.SameServer == true ? $"__**{positionCounter}** __" : $"**{positionCounter}** " :
                user.SameServer == true ? $"__{positionCounter}__ " : $"{positionCounter} ";

            if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
            {
                positionCounter = "👑 ";
            }

            var afterPositionSpacer = index + 1 == 10 ? "" : (index + 1 == 7 || index + 1 == 9) ? " " : " ";

            reply.Append($"{positionCounter}{afterPositionSpacer}{nameWithLink}");

            reply.Append($" - **{user.Playcount}** {playString}\n");

            indexNumber += 1;
            timesNameAdded += 1;

            addedUsers.Add(user.UserId);

            if (user.UserId == requestedUserId)
            {
                requestedUserAdded = true;
            }
        }

        if (!requestedUserAdded)
        {
            var requestedUser = whoKnowsObjects.FirstOrDefault(f => f.UserId == requestedUserId);
            if (requestedUser != null)
            {
                var nameWithLink = NameWithLink(requestedUser);
                var playString = StringExtensions.GetPlaysString(requestedUser.Playcount);

                reply.Append($"**{spacer}{whoKnowsObjects.IndexOf(requestedUser) + 1}.  {nameWithLink}** ");

                reply.Append($" - **{requestedUser.Playcount}** {playString}\n");
            }
        }

        if (crownModel?.CrownResult != null)
        {
            reply.Append($"\n{crownModel.CrownResult}");
        }

        return reply.ToString();
    }

    public static string NameWithLink(WhoKnowsObjectWithUser user)
    {
        var discordName = user.DiscordName != null ? StringExtensions.Sanitize(user.DiscordName.Replace("[", "").Replace("]", "")) : null;

        if (string.IsNullOrWhiteSpace(discordName))
        {
            discordName = user.LastFMUsername;
        }

        var nameWithLink = $"[\u2066{discordName}\u2069]({Constants.LastFMUserUrl}{user.LastFMUsername})";
        return nameWithLink;
    }

    private static string PrivateName()
    {
        return "Private user";
    }
}
