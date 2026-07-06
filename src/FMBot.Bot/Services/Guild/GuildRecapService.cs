using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Serilog;

namespace FMBot.Bot.Services.Guild;

public class GuildRecapService(
    IDbContextFactory<FMBotDbContext> contextFactory,
    PlayService playService,
    IServiceProvider serviceProvider,
    ShardedGatewayClient client)
{
    public const int PostDelayHours = 18;

    public async Task RunScheduledServerRecaps()
    {
        if (PublicProperties.PremiumServers.IsEmpty)
        {
            return;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var guilds = await db.Guilds
            .AsNoTracking()
            .Where(w => w.RecapSchedule != null &&
                        w.RecapChannelId != null)
            .ToListAsync();

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        foreach (var guild in guilds.Where(w => PublicProperties.PremiumServers.ContainsKey(w.DiscordGuildId)))
        {
            if (!client.Any(shard => shard.Cache.Guilds.ContainsKey(guild.DiscordGuildId)))
            {
                continue;
            }

            var currentPeriodStart = GetCurrentPeriodStart(guild.RecapSchedule.Value, now);

            if (now < currentPeriodStart.AddHours(PostDelayHours))
            {
                continue;
            }

            if (guild.LastRecap.HasValue && guild.LastRecap.Value >= currentPeriodStart)
            {
                continue;
            }

            try
            {
                var lastRecap = guild.LastRecap;
                var claimed = await db.Guilds
                    .Where(w => w.GuildId == guild.GuildId && w.LastRecap == lastRecap)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRecap, now));

                if (claimed == 0)
                {
                    continue;
                }

                var result = await PostServerRecap(guild, guild.RecapSchedule.Value, now);

                if (result == GuildRecapPostResult.Failed)
                {
                    await db.Guilds
                        .Where(w => w.GuildId == guild.GuildId && w.LastRecap == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRecap, lastRecap));
                }

                Log.Information("ServerRecap: {recapResult} for guild {guildId} - {schedule}",
                    result, guild.GuildId, guild.RecapSchedule);
            }
            catch (Exception e)
            {
                Log.Error(e, "ServerRecap: Failed to post scheduled recap for guild {guildId}", guild.GuildId);

                try
                {
                    await db.Guilds
                        .Where(w => w.GuildId == guild.GuildId && w.LastRecap == now)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRecap, guild.LastRecap));
                }
                catch (Exception rollbackException)
                {
                    Log.Error(rollbackException,
                        "ServerRecap: Failed to release recap claim for guild {guildId}", guild.GuildId);
                }
            }
        }
    }

    public async Task<GuildRecapPostResult> PostServerRecapNow(Persistence.Domain.Models.Guild guild)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        var schedule = guild.RecapSchedule ?? ServerRecapSchedule.Weekly;

        var result = await PostServerRecap(guild, schedule, now);

        if (result == GuildRecapPostResult.Posted)
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            await db.Guilds
                .Where(w => w.GuildId == guild.GuildId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.LastRecap, now));
        }

        return result;
    }

    private async Task<GuildRecapPostResult> PostServerRecap(Persistence.Domain.Models.Guild guild,
        ServerRecapSchedule schedule, DateTime now)
    {
        if (!guild.RecapChannelId.HasValue)
        {
            return GuildRecapPostResult.NoChannel;
        }

        var periodEnd = GetCurrentPeriodStart(schedule, now);
        var periodStart = schedule == ServerRecapSchedule.Weekly
            ? periodEnd.AddDays(-7)
            : periodEnd.AddMonths(-1);

        var stats = await playService.GetGuildPlayStats(guild.GuildId, periodStart, periodEnd);

        if (stats == null || stats.TotalPlaycount == 0)
        {
            return GuildRecapPostResult.NoData;
        }

        var topArtists = await playService.GetGuildTopArtistsPlays(guild.GuildId, periodStart,
            OrderType.Listeners, periodEnd);
        var topAlbums = await playService.GetGuildTopAlbumsPlays(guild.GuildId, periodStart,
            OrderType.Listeners, null, periodEnd);
        var topTracks = await playService.GetGuildTopTracksPlays(guild.GuildId, periodStart,
            OrderType.Listeners, null, periodEnd);
        var albumService = serviceProvider.GetRequiredService<AlbumService>();
        var newReleases = await albumService.FilterAlbumsToReleasePeriod(topAlbums, periodStart, periodEnd);

        if (topArtists.Count == 0)
        {
            return GuildRecapPostResult.NoData;
        }

        DateTime? nextRecap = guild.RecapSchedule.HasValue
            ? (guild.RecapSchedule == ServerRecapSchedule.Weekly ? periodEnd.AddDays(7) : periodEnd.AddMonths(1))
                .AddHours(PostDelayHours)
            : null;

        var container = BuildRecapContainer(guild.Name, schedule, periodStart, periodEnd, stats,
            topArtists, topAlbums, topTracks, newReleases, nextRecap);

        try
        {
            await client.Rest.SendMessageAsync(guild.RecapChannelId.Value, new MessageProperties()
                .WithComponents([container])
                .WithFlags(MessageFlags.IsComponentsV2)
                .WithAllowedMentions(AllowedMentionsProperties.None));

            return GuildRecapPostResult.Posted;
        }
        catch (Exception e)
        {
            if (e.Message.Contains("Unknown Channel", StringComparison.OrdinalIgnoreCase))
            {
                await using var db = await contextFactory.CreateDbContextAsync();
                await db.Guilds
                    .Where(w => w.GuildId == guild.GuildId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.RecapChannelId, (ulong?)null));

                Log.Information("ServerRecap: Removed recap channel for guild {guildId}", guild.GuildId);
            }
            else
            {
                Log.Error(e, "ServerRecap: Error posting recap for guild {guildId}", guild.GuildId);
            }

            return GuildRecapPostResult.Failed;
        }
    }

    private static ComponentContainerProperties BuildRecapContainer(string guildName, ServerRecapSchedule schedule,
        DateTime periodStart, DateTime periodEnd, GuildPlayStats stats, List<GuildArtist> topArtists,
        List<GuildAlbum> topAlbums, List<GuildTrack> topTracks, List<GuildAlbum> newReleases, DateTime? nextRecap)
    {
        var container = new ComponentContainerProperties
        {
            AccentColor = DiscordConstants.InformationColorBlue
        };

        var periodDisplay = schedule == ServerRecapSchedule.Weekly
            ? $"{periodStart.ToString("MMMM d", CultureInfo.InvariantCulture)} to {periodEnd.AddDays(-1).ToString("MMMM d", CultureInfo.InvariantCulture)}"
            : periodStart.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        var header = new StringBuilder();
        header.AppendLine(schedule == ServerRecapSchedule.Weekly
            ? $"## 📊 Weekly recap for {StringExtensions.Sanitize(guildName)}"
            : $"## 📊 Monthly recap for {StringExtensions.Sanitize(guildName)}");
        header.Append($"{periodDisplay} · **{stats.TotalPlaycount:n0}** {StringExtensions.GetPlaysString(stats.TotalPlaycount)} " +
                      $"from **{stats.ListenerCount:n0}** {StringExtensions.GetListenersString(stats.ListenerCount)}");

        container.AddComponent(new TextDisplayProperties(header.ToString()));
        container.AddComponent(new ComponentSeparatorProperties());

        var artists = new StringBuilder();
        artists.AppendLine("**Top artists**");
        foreach (var (artist, index) in topArtists.Take(10).Select((value, index) => (value, index)))
        {
            artists.AppendLine($"{index + 1}. **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(artist.ArtistName, 90))}** · " +
                               $"*{artist.ListenerCount:n0} {StringExtensions.GetListenersString(artist.ListenerCount)}* · " +
                               $"{artist.TotalPlaycount:n0} {StringExtensions.GetPlaysString(artist.TotalPlaycount)}");
        }

        container.AddComponent(new TextDisplayProperties(artists.ToString()));

        if (topAlbums.Count > 0)
        {
            container.AddComponent(new ComponentSeparatorProperties());

            var albums = new StringBuilder();
            albums.AppendLine("**Top albums**");
            foreach (var (album, index) in topAlbums.Take(5).Select((value, index) => (value, index)))
            {
                albums.AppendLine($"{index + 1}. **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.AlbumName, 60))}** " +
                                  $"by {StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.ArtistName, 40))} · " +
                                  $"*{album.ListenerCount:n0} {StringExtensions.GetListenersString(album.ListenerCount)}*");
            }

            container.AddComponent(new TextDisplayProperties(albums.ToString()));
        }

        if (newReleases.Count > 0)
        {
            container.AddComponent(new ComponentSeparatorProperties());

            var releases = new StringBuilder();
            releases.AppendLine("**Popular new releases**");
            foreach (var (album, index) in newReleases.Take(5).Select((value, index) => (value, index)))
            {
                releases.AppendLine($"{index + 1}. **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.AlbumName, 60))}** " +
                                    $"by {StringExtensions.Sanitize(StringExtensions.TruncateLongString(album.ArtistName, 40))} · " +
                                    $"*{album.ListenerCount:n0} {StringExtensions.GetListenersString(album.ListenerCount)}*");
            }

            container.AddComponent(new TextDisplayProperties(releases.ToString()));
        }

        if (topTracks.Count > 0)
        {
            container.AddComponent(new ComponentSeparatorProperties());

            var tracks = new StringBuilder();
            tracks.AppendLine("**Top tracks**");
            foreach (var (track, index) in topTracks.Take(5).Select((value, index) => (value, index)))
            {
                tracks.AppendLine($"{index + 1}. **{StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.TrackName, 60))}** " +
                                  $"by {StringExtensions.Sanitize(StringExtensions.TruncateLongString(track.ArtistName, 40))} · " +
                                  $"*{track.ListenerCount:n0} {StringExtensions.GetListenersString(track.ListenerCount)}*");
            }

            container.AddComponent(new TextDisplayProperties(tracks.ToString()));
        }

        if (nextRecap.HasValue)
        {
            container.AddComponent(new ComponentSeparatorProperties());
            container.AddComponent(new TextDisplayProperties(
                $"-# ✨ Next server recap <t:{((DateTimeOffset)nextRecap.Value).ToUnixTimeSeconds()}:R>"));
        }

        return container;
    }

    private static DateTime GetCurrentPeriodStart(ServerRecapSchedule schedule, DateTime now)
    {
        return schedule switch
        {
            ServerRecapSchedule.Weekly => now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7)),
            ServerRecapSchedule.Monthly => new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => now.Date
        };
    }
}
