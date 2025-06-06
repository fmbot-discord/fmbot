using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Owner commands")]
[Summary(".fmbot Owners Only")]
[ExcludeFromHelp]
public class OwnerCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly UserService _userService;
    private readonly IMemoryCache _cache;
    private readonly DiscordShardedClient _client;

    public OwnerCommands(
        AdminService adminService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        IMemoryCache cache,
        DiscordShardedClient client) : base(botSettings)
    {
        this._adminService = adminService;
        this._userService = userService;
        this._cache = cache;
        this._client = client;
    }

    [Command("say"), Summary("Says something")]
    [UsernameSetRequired]
    public async Task SayAsync([Remainder] string say)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync(say);
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("botrestart")]
    [Summary("Reboots the bot.")]
    [Alias("restart")]
    public async Task BotRestartAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            await ReplyAsync("Restarting bot...");
            this.Context.LogCommandUsed();
            Environment.Exit(1);
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can restart the bot.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("setusertype"), Summary("Sets usertype for other users")]
    [Alias("setperms")]
    [UsernameSetRequired]
    public async Task SetUserTypeAsync(string userId = null, string userType = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            if (userId == null || userType == null || userId == "help")
            {
                await ReplyAsync(
                    "Please format your command like this: `.fmsetusertype 'discord id' 'User/Admin/Owner'`");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!Enum.TryParse(userType, true, out UserType userTypeEnum))
            {
                await ReplyAsync("Invalid usertype. Please use 'User', 'Contributor', 'Admin', or 'Owner'.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (await this._adminService.SetUserTypeAsync(ulong.Parse(userId), userTypeEnum))
            {
                await ReplyAsync("You got it. User perms changed.");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Setting user failed. Are you sure the user exists?");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can change your usertype.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("storagecheck"), Summary("Checks how much storage is left on the server.")]
    [Alias("checkstorage", "storage")]
    [UsernameSetRequired]
    public async Task StorageCheckAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                var drives = DriveInfo.GetDrives();

                var builder = new EmbedBuilder();
                builder.WithDescription("Server Drive Info");

                foreach (var drive in drives.Where(w => w.IsReady && w.TotalSize > 10000))
                {
                    builder.AddField(drive.Name + " - " + drive.VolumeLabel + ":",
                        drive.AvailableFreeSpace.ToFormattedByteString() + " free of " +
                        drive.TotalSize.ToFormattedByteString());
                }

                await this.Context.Channel.SendMessageAsync("", false, builder.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Only .fmbot admins or owners can execute this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("serverlist"),
     Summary("Displays a list showing information related to every server the bot has joined.")]
    public async Task ServerListAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            var client = this.Context.Client as DiscordShardedClient;
            string desc = null;

            foreach (var guild in client.Guilds.OrderByDescending(o => o.MemberCount).Take(100))
            {
                desc += $"{guild.Name} - Users: {guild.MemberCount}, Owner: {guild.Owner}\n";
            }

            if (!string.IsNullOrWhiteSpace(desc))
            {
                string[] descChunks = desc.SplitByMessageLength().ToArray();
                foreach (string chunk in descChunks)
                {
                    await this.Context.User.SendMessageAsync(chunk);
                }
            }

            await this.Context.Channel.SendMessageAsync("Check your DMs!");
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Only .fmbot owners can execute this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("deleteinactiveusers")]
    [Summary("Removes users who have deleted their Last.fm account from .fmbot")]
    public async Task TimerStatusAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync($"Starting removed Last.fm user deleter.");
                var deletedUsers = await this._userService.DeleteInactiveUsers();
                await ReplyAsync($"Deleted {deletedUsers} users from the database with deleted Last.fm");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot owners can remove deleted users.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("deleteoldduplicateusers")]
    [Summary("Removes duplicate users and moves their data to their new account")]
    public async Task DeleteOldDuplicateUsersAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync($"Starting inactive user deleter / de-duplicater.");
                var deletedUsers = await this._userService.DeleteOldDuplicateUsers();
                await ReplyAsync($"Deleted {deletedUsers} inactive users from the database");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot owners can remove deleted users.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("togglespecialguild", RunMode = RunMode.Async)]
    [Summary("Makes the server a special server")]
    [GuildOnly]
    public async Task ToggleSpecialGuildAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            var specialGuild = await this._adminService.ToggleSpecialGuildAsync(this.Context.Guild);

            if (specialGuild == true)
            {
                await ReplyAsync("This is now a special guild!!1!");
            }
            else
            {
                await ReplyAsync($"Not a special guild anymore :(");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Only .fmbot owners can execute this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("memorydiag", RunMode = RunMode.Async)]
    [Summary("Displays detailed memory diagnostics.")]
    public async Task MemoryDiagnosticsAsync()
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(DiscordConstants.InformationColorBlue)
            .WithTitle("Detailed Memory Diagnostics");

        var process = Process.GetCurrentProcess();
        var processInfo = new StringBuilder();
        processInfo.AppendLine($"**Working Set:** `{process.WorkingSet64.ToFormattedByteString()}`");
        processInfo.AppendLine($"**Private Memory:** `{process.PrivateMemorySize64.ToFormattedByteString()}`");
        processInfo.AppendLine($"**Virtual Memory:** `{process.VirtualMemorySize64.ToFormattedByteString()}`");
        processInfo.AppendLine($"**Paged Memory:** `{process.PagedMemorySize64.ToFormattedByteString()}`");
        processInfo.AppendLine($"**Thread Count:** `{process.Threads.Count}`");
        embed.AddField("Process Memory", processInfo.ToString());

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);

        var gcDetails = new StringBuilder();
        gcDetails.AppendLine($"**Total Memory:** `{GC.GetTotalMemory(false).ToFormattedByteString()}`");


        var gcInfo = GC.GetGCMemoryInfo();
        gcDetails.AppendLine($"**Heap Size:** `{gcInfo.HeapSizeBytes.ToFormattedByteString()}`");
        gcDetails.AppendLine($"**Committed:** `{gcInfo.TotalCommittedBytes.ToFormattedByteString()}`");
        gcDetails.AppendLine($"**Fragmented:** `{gcInfo.FragmentedBytes.ToFormattedByteString()}`");
        gcDetails.AppendLine($"**Memory Load:** `{gcInfo.MemoryLoadBytes.ToFormattedByteString()}`");
        gcDetails.AppendLine(
            $"**High Memory Threshold:** `{gcInfo.HighMemoryLoadThresholdBytes.ToFormattedByteString()}`");

        if (gcInfo.PauseTimePercentage > 0)
        {
            gcDetails.AppendLine($"**GC Pause %:** `{gcInfo.PauseTimePercentage:F2}%`");
        }

        gcDetails.AppendLine($"**Gen0 Collections:** `{GC.CollectionCount(0)}`");
        gcDetails.AppendLine($"**Gen1 Collections:** `{GC.CollectionCount(1)}`");
        gcDetails.AppendLine($"**Gen2 Collections:** `{GC.CollectionCount(2)}`");

        gcDetails.AppendLine($"**Total Allocated:** `{GC.GetTotalAllocatedBytes(true).ToFormattedByteString()}`");

        gcDetails.AppendLine($"**Collections:** Gen0: `{gen0}` | Gen1: `{gen1}` | Gen2: `{gen2}`");
        gcDetails.AppendLine($"**Pause Mode:** `{GCSettings.LatencyMode}`");
        gcDetails.AppendLine($"**Server GC:** `{GCSettings.IsServerGC}`");

        embed.AddField("GC Information", gcDetails.ToString());

        var perfInfo = new StringBuilder();
        var totalCollections = gen0 + gen1 + gen2;
        if (totalCollections > 0)
        {
            var gen2Percentage = (gen2 * 100.0 / totalCollections);
            perfInfo.AppendLine(
                $"**Gen2 Collection Rate:** `{gen2Percentage:F1}%` {(gen2Percentage > 10 ? "⚠️" : "✅")}");
        }

        var memoryPressure = (gcInfo.MemoryLoadBytes * 100.0 / gcInfo.HighMemoryLoadThresholdBytes);
        perfInfo.AppendLine($"**Memory Pressure:** `{memoryPressure:F1}%` {(memoryPressure > 80 ? "⚠️" : "✅")}");

        var fragmentation = (gcInfo.FragmentedBytes * 100.0 / gcInfo.HeapSizeBytes);
        perfInfo.AppendLine($"**Fragmentation:** `{fragmentation:F1}%` {(fragmentation > 20 ? "⚠️" : "✅")}");
        embed.AddField("Performance", perfInfo.ToString());

        var cacheStats = GetCacheStatistics();
        cacheStats += $"\n**Downloaded members**: {this._client.Guilds.Sum(s => s.DownloadedMemberCount)}";
        if (!string.IsNullOrEmpty(cacheStats))
        {
            embed.AddField("Cache Statistics", cacheStats);
        }

        var chromeStats = await GetChromeStatistics();
        if (!string.IsNullOrEmpty(chromeStats))
        {
            embed.AddField("Chrome/Puppeteer", chromeStats);
        }

        await this.Context.Channel.SendMessageAsync("", false, embed.Build());
        this.Context.LogCommandUsed();
    }

    private string GetCacheStatistics()
    {
        var sb = new StringBuilder();

        try
        {
            if (_cache is MemoryCache memoryCache)
            {
                var stats = memoryCache.GetCurrentStatistics();
                if (stats != null)
                {
                    sb.AppendLine($"**Cache Entries:** `{stats.CurrentEntryCount:N0}`");
                    sb.AppendLine(
                        $"**Cache Size:** `{stats.CurrentEstimatedSize?.ToFormattedByteString() ?? "Unknown"}`");
                    sb.AppendLine(
                        $"**Hit Ratio:** `{(stats.TotalHits > 0 ? (stats.TotalHits * 100.0 / (stats.TotalHits + stats.TotalMisses)) : 0):F1}%`");
                    sb.AppendLine($"**Total Hits:** `{stats.TotalHits:N0}`");
                    sb.AppendLine($"**Total Misses:** `{stats.TotalMisses:N0}`");
                }
                else
                {
                    sb.AppendLine($"**Cache Type:** `{_cache.GetType().Name}`");
                    sb.AppendLine("**Statistics:** Not available (enable MemoryCacheOptions.TrackStatistics)");
                }
            }
            else
            {
                sb.AppendLine($"**Cache Type:** `{_cache.GetType().Name}`");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"**Cache Stats Error:** `{ex.Message}`");
        }

        sb.AppendLine();
        sb.AppendLine("**Global Collections:**");
        sb.AppendLine($"**Slash Commands:** `{PublicProperties.SlashCommands.Count:N0}`");
        sb.AppendLine($"**Premium Servers:** `{PublicProperties.PremiumServers.Count:N0}`");
        sb.AppendLine($"**Registered Users:** `{PublicProperties.RegisteredUsers.Count:N0}`");

        sb.AppendLine();
        sb.AppendLine("**Command Tracking:**");
        sb.AppendLine($"**Command Responses:** `{PublicProperties.UsedCommandsResponses.Count:N0}`");
        sb.AppendLine($"**Response Messages:** `{PublicProperties.UsedCommandsResponseMessageId.Count:N0}`");
        sb.AppendLine($"**Response Contexts:** `{PublicProperties.UsedCommandsResponseContextId.Count:N0}`");
        sb.AppendLine($"**Error References:** `{PublicProperties.UsedCommandsErrorReferences.Count:N0}`");
        sb.AppendLine($"**Discord User IDs:** `{PublicProperties.UsedCommandDiscordUserIds.Count:N0}`");
        sb.AppendLine($"**Hints Shown:** `{PublicProperties.UsedCommandsHintShown.Count:N0}`");

        sb.AppendLine();
        sb.AppendLine("**Music References:**");
        sb.AppendLine($"**Artists:** `{PublicProperties.UsedCommandsArtists.Count:N0}`");
        sb.AppendLine($"**Albums:** `{PublicProperties.UsedCommandsAlbums.Count:N0}`");
        sb.AppendLine($"**Tracks:** `{PublicProperties.UsedCommandsTracks.Count:N0}`");
        sb.AppendLine($"**Referenced Music:** `{PublicProperties.UsedCommandsReferencedMusic.Count:N0}`");

            var estimatedSize = EstimateCollectionMemory();
            sb.AppendLine();
            sb.AppendLine($"**Estimated Collections Memory:** `{estimatedSize.ToFormattedByteString()}`");

        return sb.ToString();
    }

    private static async Task<string> GetChromeStatistics()
    {
        var sb = new StringBuilder();

        try
        {
            var chromeProcesses = Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase))
                .ToList();

            sb.AppendLine($"**Chrome Processes:** `{chromeProcesses.Count}`");

            var totalChromeMem = chromeProcesses.Sum(p => p.WorkingSet64);
            sb.AppendLine($"**Chrome Memory:** `{totalChromeMem.ToFormattedByteString()}`");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error getting Chrome stats: {ex.Message}");
        }

        return await Task.FromResult(sb.ToString());
    }

    private static long EstimateCollectionMemory()
    {
        long totalSize = 0;

        totalSize += EstimateDictionarySize(PublicProperties.SlashCommands.Count, 20, 18);
        totalSize += EstimateDictionarySize(PublicProperties.PremiumServers.Count, 8, 10);
        totalSize += EstimateDictionarySize(PublicProperties.RegisteredUsers.Count, 18, 10);

        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsResponses.Count, 18, 5);

        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsResponseMessageId.Count, 18, 18);
        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsResponseContextId.Count, 18, 18);
        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsErrorReferences.Count, 18, 18);
        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandDiscordUserIds.Count, 18, 18);

        totalSize += PublicProperties.UsedCommandsHintShown.Count * 16;

        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsArtists.Count, 18, 20);
        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsAlbums.Count, 18, 30);
        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsTracks.Count, 18, 20);

        totalSize += EstimateDictionarySize(PublicProperties.UsedCommandsReferencedMusic.Count, 18, 100);

        return totalSize;
    }

    private static long EstimateDictionarySize(int count, int keySize, int valueSize)
    {
        const int baseOverhead = 400;

        return baseOverhead + (count * (keySize + valueSize));
    }
}
