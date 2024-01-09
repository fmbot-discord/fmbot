using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using FMBot.Domain.Enums;
using Genius.Models.Song;
using Serilog;
using Microsoft.Extensions.Options;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using Genius.Models.User;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Services;

public class AdminService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly DiscordShardedClient _client;

    public AdminService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, IMemoryCache cache, DiscordShardedClient client)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._client = client;
        this._botSettings = botSettings.Value;
    }

    public async Task<bool> HasCommandAccessAsync(IUser discordUser, UserType minUserType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var contextUser = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (contextUser == null)
        {
            return false;
        }

        switch (contextUser.UserType)
        {
            case UserType.Admin:
                switch (minUserType)
                {
                    case UserType.User:
                        return true;
                    case UserType.Admin:
                        return true;
                    default:
                        return false;
                }
            case UserType.Owner:
                switch (minUserType)
                {
                    case UserType.User:
                        return true;
                    case UserType.Admin:
                        return true;
                    default:
                        return true;
                }
            default:
                switch (minUserType)
                {
                    case UserType.User:
                        return true;
                    default:
                        return false;
                }
        }
    }

    public async Task<bool> SetUserTypeAsync(ulong discordUserId, UserType userType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null)
        {
            return false;
        }

        user.UserType = userType;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<List<UserInteraction>> GetRecentUserInteractions(int userId)
    {
        var dateFilter = DateTime.UtcNow.AddDays(-3);
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserInteractions
            .AsQueryable()
            .Where(w => w.Timestamp > dateFilter && w.UserId == userId)
            .OrderByDescending(o => o.Timestamp)
            .ToListAsync();
    }

    public async Task<bool> AddUserToBlocklistAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null || user.UserType == UserType.Owner)
        {
            return false;
        }

        user.Blocked = true;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        this._cache.Remove("blocked-users");

        return true;
    }

    public async Task<bool> RemoveUserFromBlocklistAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user == null || user.UserType == UserType.Owner)
        {
            return false;
        }

        user.Blocked = false;

        db.Entry(user).State = EntityState.Modified;

        this._cache.Remove("blocked-users");

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<BottedUser> GetBottedUserAsync(string lastFmUserName, DateTime? registeredDateTime = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser == null && registeredDateTime.HasValue)
        {
            bottedUser = await db.BottedUsers
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.LastFmRegistered == registeredDateTime.Value);
        }

        return bottedUser;
    }

    public async Task<GlobalFilteredUser> GetFilteredUserAsync(string lastFmUserName,
        DateTime? registeredDateTime = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var filteredUser = await db.GlobalFilteredUsers
            .AsQueryable()
            .OrderByDescending(o => o.Created)
            .FirstOrDefaultAsync(f => f.UserNameLastFm.ToLower() == lastFmUserName.ToLower());

        if (filteredUser == null && registeredDateTime.HasValue)
        {
            filteredUser = await db.GlobalFilteredUsers
                .AsQueryable()
                .OrderByDescending(o => o.Created)
                .FirstOrDefaultAsync(f => f.RegisteredLastFm == registeredDateTime.Value);
        }

        return filteredUser;
    }

    public async Task<GlobalFilteredUser> GetFilteredUserForIdAsync(int filteredUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.GlobalFilteredUsers
               .AsQueryable()
               .FirstOrDefaultAsync(f => f.GlobalFilteredUserId == filteredUserId);

    }

    public async Task<List<User>> GetUsersWithLfmUsernameAsync(string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => w.UserNameLastFM.ToLower() == lastFmUserName.ToLower())
            .ToListAsync();
    }

    public async Task<bool> DisableBottedUserBanAsync(string lastFmUserName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser == null)
        {
            return false;
        }

        bottedUser.BanActive = false;

        var stringToAdd = $"*[Unbanned <t:{DateTime.UtcNow.ToUnixEpochDate()}:F>]*";

        if (bottedUser.Notes == null)
        {
            bottedUser.Notes = stringToAdd;
        }
        else
        {
            bottedUser.Notes += $"\n{stringToAdd}";
        }

        db.Entry(bottedUser).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> EnableBottedUserBanAsync(string lastFmUserName, string reason, DateTime? registeredDate = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser == null)
        {
            return false;
        }

        bottedUser.BanActive = true;

        if (registeredDate.HasValue)
        {
            bottedUser.LastFmRegistered = DateTime.SpecifyKind(registeredDate.Value, DateTimeKind.Utc);
        }

        var stringToAdd = $"*[Re-banned <t:{DateTime.UtcNow.ToUnixEpochDate()}:F>]*";

        if (bottedUser.Notes == null)
        {
            if (reason != null)
            {
                bottedUser.Notes = stringToAdd;
                bottedUser.Notes = $"\n{reason}";
            }
            else
            {
                bottedUser.Notes = stringToAdd;
            }
        }
        else
        {
            bottedUser.Notes += $"\n\n{stringToAdd}";
            if (reason != null)
            {
                bottedUser.Notes += $"\n{reason}";
            }
        }

        db.Entry(bottedUser).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<bool> AddBottedUserAsync(string lastFmUserName, string reason, DateTime? registeredDate = null)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var bottedUser = await db.BottedUsers
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == lastFmUserName.ToLower());

        if (bottedUser != null)
        {
            return false;
        }

        if (registeredDate.HasValue)
        {
            registeredDate = DateTime.SpecifyKind(registeredDate.Value, DateTimeKind.Utc);
        }

        var newBottedUser = new BottedUser
        {
            UserNameLastFM = lastFmUserName,
            BanActive = true,
            Notes = reason,
            LastFmRegistered = registeredDate
        };

        await db.BottedUsers.AddAsync(newBottedUser);

        await db.SaveChangesAsync();

        return true;
    }

    public async Task<BottedUserReport> CreateBottedUserReportAsync(ulong discordUserId, string lastFmUserName, string note)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var report = new BottedUserReport
        {
            ReportStatus = ReportStatus.Pending,
            ReportedAt = DateTime.UtcNow,
            ReportedByDiscordUserId = discordUserId,
            ProvidedNote = note,
            UserNameLastFM = lastFmUserName
        };

        await db.BottedUserReport.AddAsync(report);
        await db.SaveChangesAsync();

        return report;
    }

    public async Task<BottedUserReport> GetReportForId(int id)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.BottedUserReport
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<bool> UserReportAlreadyExists(string userNameLastFM)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        userNameLastFM = userNameLastFM.ToLower();

        return await db.BottedUserReport.AnyAsync(a => a.ReportStatus == ReportStatus.Pending &&
                                                       a.UserNameLastFM == userNameLastFM);
    }

    public async Task PostReport(BottedUserReport report)
    {
        try
        {
            if (this._botSettings.Bot.BaseServerId == 0 && this._botSettings.Bot.GlobalWhoKnowsReportChannelId == 0)
            {
                Log.Warning("A botted user report was sent but the base server and/or report channel are not set in the config");
                return;
            }

            var guild = this._client.GetGuild(this._botSettings.Bot.BaseServerId);
            var channel = guild?.GetTextChannel(this._botSettings.Bot.GlobalWhoKnowsReportChannelId);

            if (channel == null)
            {
                Log.Warning("A botted user report was sent but the base server and report channel could not be found");
                return;
            }

            var embed = new EmbedBuilder();
            embed.WithTitle($"New gwk botted user report");

            var components = new ComponentBuilder()
                .WithButton("Ban", $"gwk-report-ban-{report.Id}", style: ButtonStyle.Success)
                .WithButton("Deny", $"gwk-report-deny-{report.Id}", style: ButtonStyle.Danger);

            components.WithButton("User", url: $"{Constants.LastFMUserUrl}{report.UserNameLastFM}", row: 1, style: ButtonStyle.Link);

            embed.AddField("User", $"**[{report.UserNameLastFM}]({Constants.LastFMUserUrl}{report.UserNameLastFM})**");

            if (!string.IsNullOrWhiteSpace(report.ProvidedNote))
            {
                embed.AddField("Provided note", report.ProvidedNote);
            }

            var filteredUser = await GetFilteredUserAsync(report.UserNameLastFM);

            if (filteredUser != null)
            {
                embed.AddField("User is currently filtered:", WhoKnowsFilterService.FilteredUserReason(filteredUser));

                components.WithButton($"Convert filter to ban", $"gwk-filtered-user-to-ban-{filteredUser.GlobalFilteredUserId}", style: ButtonStyle.Secondary, row: 2);
            }

            var reporter = guild.GetUser(report.ReportedByDiscordUserId);
            embed.AddField("Reporter",
                $"**{reporter?.DisplayName}** - <@{report.ReportedByDiscordUserId}> - `{report.ReportedByDiscordUserId}`");

            await channel.SendMessageAsync(embed: embed.Build(), components: components.Build());
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task UpdateReport(BottedUserReport report, ReportStatus status, ulong handlerDiscordId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        report.ReportStatus = status;
        report.ProcessedAt = DateTime.UtcNow;
        report.ProcessedByDiscordUserId = handlerDiscordId;

        db.BottedUserReport.Update(report);

        await db.SaveChangesAsync();
    }

    public async Task<bool?> ToggleSpecialGuildAsync(IGuild guild)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var existingGuild = await db.Guilds
            .AsQueryable()
            .FirstAsync(f => f.DiscordGuildId == guild.Id);

        existingGuild.Name = guild.Name;

        if (existingGuild.SpecialGuild == true)
        {
            existingGuild.SpecialGuild = false;
        }
        else
        {
            existingGuild.SpecialGuild = true;
        }

        db.Entry(existingGuild).State = EntityState.Modified;

        await db.SaveChangesAsync();

        return existingGuild.SpecialGuild;
    }
}
