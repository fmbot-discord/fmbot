using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild.Renderers;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Serilog;

namespace FMBot.Bot.Services.Guild;

public class AutopostService(
    IDbContextFactory<FMBotDbContext> contextFactory,
    AutopostRendererRegistry rendererRegistry,
    GuildService guildService,
    ShardedGatewayClient client)
{
    public const int PostDelayHours = 18;
    public const int MaxAutopostsPerGuild = 10;
    public const int RunsToKeep = 52;

    public async Task RunScheduledAutoposts()
    {
        if (PublicProperties.PremiumServers.IsEmpty)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var autoposts = await db.GuildAutoposts
            .AsNoTracking()
            .Include(i => i.Guild)
            .Where(w => w.Enabled)
            .ToListAsync();

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        foreach (var autopost in autoposts.Where(w =>
                     PublicProperties.PremiumServers.ContainsKey(w.Guild.DiscordGuildId)))
        {
            if (!client.Any(shard => shard.Cache.Guilds.ContainsKey(autopost.Guild.DiscordGuildId)))
            {
                continue;
            }

            var currentPeriodStart = GetCurrentPeriodStart(autopost.Schedule, now);

            if (now < currentPeriodStart.AddHours(PostDelayHours))
            {
                continue;
            }

            if (autopost.LastPosted.HasValue && autopost.LastPosted.Value >= currentPeriodStart)
            {
                continue;
            }

            try
            {
                var lastPosted = autopost.LastPosted;
                var claimed = await db.GuildAutoposts
                    .Where(w => w.Id == autopost.Id && w.LastPosted == lastPosted)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastPosted, now));

                if (claimed == 0)
                {
                    continue;
                }

                var result = await PostAutopost(autopost, now);

                if (result == AutopostPostResult.Failed)
                {
                    await db.GuildAutoposts
                        .Where(w => w.Id == autopost.Id && w.LastPosted == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastPosted, lastPosted));
                }

                Log.Information("Autopost: {postResult} for autopost {autopostId} ({contentType}) in guild {guildId} - {schedule}",
                    result, autopost.Id, autopost.ContentType, autopost.GuildId, autopost.Schedule);
            }
            catch (Exception e)
            {
                Log.Error(e, "Autopost: Failed to post scheduled autopost {autopostId} for guild {guildId}",
                    autopost.Id, autopost.GuildId);

                try
                {
                    await db.GuildAutoposts
                        .Where(w => w.Id == autopost.Id && w.LastPosted == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastPosted, autopost.LastPosted));
                }
                catch (Exception rollbackException)
                {
                    Log.Error(rollbackException,
                        "Autopost: Failed to release autopost claim for autopost {autopostId}", autopost.Id);
                }
            }
        }
    }

    public async Task<AutopostPostResult> PostAutopostNow(int autopostId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var autopost = await db.GuildAutoposts
            .AsNoTracking()
            .Include(i => i.Guild)
            .FirstOrDefaultAsync(f => f.Id == autopostId);

        if (autopost == null)
        {
            return AutopostPostResult.Failed;
        }

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var result = await PostAutopost(autopost, now);

        if (result == AutopostPostResult.Posted)
        {
            await db.GuildAutoposts
                .Where(w => w.Id == autopost.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastPosted, now));
        }

        return result;
    }

    private async Task<AutopostPostResult> PostAutopost(GuildAutopost autopost, DateTime now)
    {
        var periodEnd = GetCurrentPeriodStart(autopost.Schedule, now);
        var periodStart = autopost.Schedule == AutopostSchedule.Monthly
            ? periodEnd.AddMonths(-1)
            : periodEnd.AddDays(-7);

        int[] roleUserIds = null;
        if (autopost.RoleIds is { Length: > 0 })
        {
            var guildUsers = await guildService.GetGuildUsers(autopost.Guild.DiscordGuildId);
            roleUserIds = guildUsers.Values
                .Where(w => w.Roles != null && w.Roles.Any(r => autopost.RoleIds.Contains(r)))
                .Select(s => s.UserId)
                .ToArray();

            if (roleUserIds.Length == 0)
            {
                return AutopostPostResult.NoData;
            }
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var previousRun = await db.GuildAutopostRuns
            .AsNoTracking()
            .Where(w => w.AutopostId == autopost.Id)
            .OrderByDescending(o => o.PostedAt)
            .FirstOrDefaultAsync();

        var renderContext = new AutopostRenderContext
        {
            Autopost = autopost,
            GuildName = autopost.Guild.Name,
            RoleUserIds = roleUserIds,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            NextPost = GetNextPost(autopost.Schedule, periodEnd),
            PreviousSnapshot = previousRun?.Snapshot
        };

        var renderer = rendererRegistry.Get(autopost.ContentType);
        var renderResult = await renderer.RenderAsync(renderContext);

        if (renderResult == null)
        {
            return AutopostPostResult.NoData;
        }

        renderResult.Snapshot.Version = AutopostSnapshot.CurrentVersion;

        var run = new GuildAutopostRun
        {
            AutopostId = autopost.Id,
            GuildId = autopost.GuildId,
            ContentType = autopost.ContentType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            PostedAt = now,
            Snapshot = renderResult.Snapshot
        };

        db.GuildAutopostRuns.Add(run);
        await db.SaveChangesAsync();

        if (renderResult.HasMoreEntries)
        {
            renderResult.Container.AddComponent(new ComponentSeparatorProperties());

            var deepDiveButton = new ButtonProperties($"{InteractionConstants.Autopost.DeepDive}:{run.Id}",
                "Full list", ButtonStyle.Secondary);

            if (!string.IsNullOrEmpty(renderResult.Footer))
            {
                renderResult.Container.AddComponent(new ComponentSectionProperties(deepDiveButton)
                {
                    Components = [new TextDisplayProperties(renderResult.Footer)]
                });
            }
            else
            {
                var deepDiveRow = new ActionRowProperties();
                deepDiveRow.AddComponents(deepDiveButton);
                renderResult.Container.AddComponent(deepDiveRow);
            }
        }
        else if (!string.IsNullOrEmpty(renderResult.Footer))
        {
            renderResult.Container.AddComponent(new ComponentSeparatorProperties());
            renderResult.Container.AddComponent(new TextDisplayProperties(renderResult.Footer));
        }

        try
        {
            var message = await client.Rest.SendMessageAsync(autopost.ChannelId, new MessageProperties()
                .WithComponents([renderResult.Container])
                .WithFlags(MessageFlags.IsComponentsV2)
                .WithAllowedMentions(AllowedMentionsProperties.None));

            run.MessageId = message.Id;
            await db.SaveChangesAsync();

            await db.GuildAutoposts
                .Where(w => w.Id == autopost.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastMessageId, message.Id));

            await PruneRuns(db, autopost.Id);

            return AutopostPostResult.Posted;
        }
        catch (Exception e)
        {
            db.GuildAutopostRuns.Remove(run);
            await db.SaveChangesAsync();

            if (e.Message.Contains("Unknown Channel", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Missing Access", StringComparison.OrdinalIgnoreCase))
            {
                await db.GuildAutoposts
                    .Where(w => w.Id == autopost.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Enabled, false));

                Log.Information("Autopost: Disabled autopost {autopostId} for guild {guildId} - channel unavailable",
                    autopost.Id, autopost.GuildId);
            }
            else
            {
                Log.Error(e, "Autopost: Error posting autopost {autopostId} for guild {guildId}",
                    autopost.Id, autopost.GuildId);
            }

            return AutopostPostResult.Failed;
        }
    }

    private static async Task PruneRuns(FMBotDbContext db, int autopostId)
    {
        var cutoff = await db.GuildAutopostRuns
            .AsNoTracking()
            .Where(w => w.AutopostId == autopostId)
            .OrderByDescending(o => o.PostedAt)
            .Select(s => s.PostedAt)
            .Skip(RunsToKeep - 1)
            .FirstOrDefaultAsync();

        if (cutoff != default)
        {
            await db.GuildAutopostRuns
                .Where(w => w.AutopostId == autopostId && w.PostedAt < cutoff)
                .ExecuteDeleteAsync();
        }
    }

    public static DateTime GetCurrentPeriodStart(AutopostSchedule schedule, DateTime now)
    {
        return schedule == AutopostSchedule.Monthly
            ? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            : now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
    }

    public static DateTime GetNextPost(AutopostSchedule schedule, DateTime periodEnd)
    {
        var nextPeriodEnd = schedule == AutopostSchedule.Monthly
            ? periodEnd.AddMonths(1)
            : periodEnd.AddDays(7);

        return nextPeriodEnd.AddHours(PostDelayHours);
    }

    public static DateTime GetNextScheduledPost(GuildAutopost autopost, DateTime now)
    {
        var currentPeriodStart = GetCurrentPeriodStart(autopost.Schedule, now);

        if (autopost.LastPosted.HasValue && autopost.LastPosted.Value >= currentPeriodStart)
        {
            return GetNextPost(autopost.Schedule, currentPeriodStart);
        }

        var due = currentPeriodStart.AddHours(PostDelayHours);
        if (due > now)
        {
            return due;
        }

        var nextTick = new DateTime(now.Year, now.Month, now.Day, now.Hour, 30, 0, DateTimeKind.Utc);
        if (nextTick <= now)
        {
            nextTick = nextTick.AddHours(1);
        }

        return nextTick;
    }

    public async Task<List<GuildAutopost>> GetAutopostsForGuild(int guildId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.GuildAutoposts
            .AsNoTracking()
            .Where(w => w.GuildId == guildId)
            .OrderBy(o => o.Id)
            .ToListAsync();
    }

    public async Task<GuildAutopost> GetAutopost(int autopostId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.GuildAutoposts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == autopostId);
    }

    public async Task<GuildAutopostRun> GetRun(long runId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.GuildAutopostRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == runId);
    }

    public async Task<GuildAutopostRun> GetPreviousRun(GuildAutopostRun run)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        return await db.GuildAutopostRuns
            .AsNoTracking()
            .Where(w => w.AutopostId == run.AutopostId && w.PostedAt < run.PostedAt)
            .OrderByDescending(o => o.PostedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<GuildAutopost> CreateAutopostAsync(int guildId, ulong channelId, ulong createdBy)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var existing = await db.GuildAutoposts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.ChannelId == channelId);

        if (existing != null)
        {
            return existing.GuildId == guildId ? existing : null;
        }

        var count = await db.GuildAutoposts.CountAsync(c => c.GuildId == guildId);
        if (count >= MaxAutopostsPerGuild)
        {
            return null;
        }

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var autopost = new GuildAutopost
        {
            GuildId = guildId,
            ChannelId = channelId,
            ContentType = AutopostType.ServerRecap,
            Schedule = AutopostSchedule.Weekly,
            ContentSize = AutopostSize.Standard,
            Enabled = true,
            CreatedBy = createdBy,
            Created = now,
            Modified = now
        };

        db.GuildAutoposts.Add(autopost);
        await db.SaveChangesAsync();

        return autopost;
    }

    public async Task<GuildAutopost> UpdateAutopostAsync(int autopostId, Action<GuildAutopost> update)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var autopost = await db.GuildAutoposts.FirstOrDefaultAsync(f => f.Id == autopostId);

        if (autopost == null)
        {
            return null;
        }

        update(autopost);
        autopost.Modified = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await db.SaveChangesAsync();

        return autopost;
    }

    public async Task RemoveAutopostAsync(int autopostId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        await db.GuildAutoposts
            .Where(w => w.Id == autopostId)
            .ExecuteDeleteAsync();
    }
}
