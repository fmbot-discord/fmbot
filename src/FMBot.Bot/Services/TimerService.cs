using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Handlers;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Google.Protobuf.WellKnownTypes;
using Hangfire;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using Web.InternalApi;

namespace FMBot.Bot.Services;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public class TimerService
{
    private readonly HttpClient _httpClient;
    private readonly UserService _userService;
    private readonly UpdateService _updateService;
    private readonly IndexService _indexService;
    private readonly GuildService _guildService;
    private readonly DiscordShardedClient _client;
    private readonly WebhookService _webhookService;
    private readonly BotSettings _botSettings;
    private readonly FeaturedService _featuredService;
    private readonly SupporterService _supporterService;
    private readonly IMemoryCache _cache;
    private readonly DiscogsService _discogsService;
    private readonly WhoKnowsFilterService _whoKnowsFilterService;
    private readonly StatusHandler.StatusHandlerClient _statusHandler;
    private readonly BotListService _botListService;
    private readonly EurovisionService _eurovisionService;
    private readonly UpdateQueueHandler _updateQueueHandler;

    public FeaturedLog CurrentFeatured;
    private CancellationTokenSource _updateQueueCancellationToken;

    public TimerService(DiscordShardedClient client,
        UpdateService updateService,
        UserService userService,
        IndexService indexService,
        GuildService guildService,
        WebhookService webhookService,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        IMemoryCache cache,
        SupporterService supporterService,
        DiscogsService discogsService,
        WhoKnowsFilterService whoKnowsFilterService,
        StatusHandler.StatusHandlerClient statusHandler,
        HttpClient httpClient,
        BotListService botListService,
        EurovisionService eurovisionService)
    {
        this._client = client;
        this._userService = userService;
        this._indexService = indexService;
        this._guildService = guildService;
        this._webhookService = webhookService;
        this._featuredService = featuredService;
        this._cache = cache;
        this._supporterService = supporterService;
        this._discogsService = discogsService;
        this._whoKnowsFilterService = whoKnowsFilterService;
        this._statusHandler = statusHandler;
        this._httpClient = httpClient;
        this._botListService = botListService;
        this._eurovisionService = eurovisionService;
        this._updateService = updateService;
        this._botSettings = botSettings.Value;

        this.CurrentFeatured = this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow).Result;
        this._updateQueueHandler = new UpdateQueueHandler(this._updateService, TimeSpan.FromMilliseconds(100));
    }

    public void QueueJobs()
    {
        Log.Information($"RecurringJob: Adding {nameof(CheckForNewFeatured)}");
        RecurringJob.AddOrUpdate(nameof(CheckForNewFeatured), () => CheckForNewFeatured(), "*/2 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(UpdateStatus)}");
        RecurringJob.AddOrUpdate(nameof(UpdateStatus), () => UpdateStatus(), "* * * * *");

        Log.Information($"RecurringJob: Adding {nameof(UpdateHealthCheck)}");
        RecurringJob.AddOrUpdate(nameof(UpdateHealthCheck), () => UpdateHealthCheck(), "*/20 * * * * *");

        Log.Information($"RecurringJob: Adding {nameof(CheckIfShardsNeedReconnect)}");
        RecurringJob.AddOrUpdate(nameof(CheckIfShardsNeedReconnect), () => CheckIfShardsNeedReconnect(), "*/2 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(ClearUserCache)}");
        RecurringJob.AddOrUpdate(nameof(ClearUserCache), () => ClearUserCache(), "30 */2 * * *");

        Log.Information($"RecurringJob: Adding {nameof(ClearInternalLogs)}");
        RecurringJob.AddOrUpdate(nameof(ClearInternalLogs), () => ClearInternalLogs(), "0 8 * * *");

        if (this._botSettings.LastFm.UserIndexFrequencyInDays != null &&
            this._botSettings.LastFm.UserIndexFrequencyInDays != 0 &&
            (ConfigData.Data.Shards == null ||
             ConfigData.Data.Shards.MainInstance == true))
        {
            Log.Information($"RecurringJob: Adding {nameof(AddUsersToIndexQueue)}");
            RecurringJob.AddOrUpdate(nameof(AddUsersToIndexQueue), () => AddUsersToIndexQueue(), "0 8 * * *");
        }
        else
        {
            Log.Warning($"No {nameof(this._botSettings.LastFm.UserIndexFrequencyInDays)} set in config, not queuing user index job");
        }

        if (this._botSettings.LastFm.UserUpdateFrequencyInHours != null &&
            this._botSettings.LastFm.UserUpdateFrequencyInHours != 0 &&
            (ConfigData.Data.Shards == null ||
             ConfigData.Data.Shards.MainInstance == true))
        {
            Log.Information($"RecurringJob: Adding {nameof(AddUsersToUpdateQueue)}");
            RecurringJob.AddOrUpdate(nameof(AddUsersToUpdateQueue), () => AddUsersToUpdateQueue(), "0 8,13 * * *");
        }
        else
        {
            Log.Warning($"No {nameof(this._botSettings.LastFm.UserUpdateFrequencyInHours)} set in config, not queuing user update job");
        }

        if (this._client.CurrentUser.Id == Constants.BotBetaId &&
            (ConfigData.Data.Shards == null ||
             ConfigData.Data.Shards.MainInstance == true))
        {
            Log.Information($"RecurringJob: Adding {nameof(UpdateGlobalWhoKnowsFilters)}");
            RecurringJob.AddOrUpdate(nameof(UpdateGlobalWhoKnowsFilters), () => UpdateGlobalWhoKnowsFilters(), "0 10 * * *");
        }

        var mainGuildConnected = this._client.Guilds.Any(a => a.Id == ConfigData.Data.Bot.BaseServerId);
        if (this._client.CurrentUser.Id == Constants.BotProductionId && mainGuildConnected)
        {
            QueueMasterJobs();
        }
        else
        {
            Log.Warning("Main guild not connected, not queuing master jobs");
            BackgroundJob.Schedule(() => MakeSureMasterJobsAreQueued(), TimeSpan.FromMinutes(2));
        }

        BackgroundJob.Schedule(() => UpdateEurovisionData(), TimeSpan.FromSeconds(1));

        if (DateTime.Today.Month == 5 || DateTime.Today.Month == 4)
        {
            Log.Information($"RecurringJob: Adding {nameof(UpdateEurovisionData)}");
            RecurringJob.AddOrUpdate(nameof(UpdateEurovisionData), () => UpdateEurovisionData(), "30 */2 * * *");
        }
    }

    public void MakeSureMasterJobsAreQueued()
    {
        var mainGuildConnected = this._client.Guilds.Any(a => a.Id == ConfigData.Data.Bot.BaseServerId);
        if (this._client.CurrentUser.Id == Constants.BotProductionId && mainGuildConnected)
        {
            QueueMasterJobs();
        }
    }

    public void QueueMasterJobs()
    {
        Log.Warning("Queueing master jobs on instance {instance}", ConfigData.Data.Shards?.InstanceName);

        Log.Information($"RecurringJob: Adding {nameof(UpdateMetrics)}");
        RecurringJob.AddOrUpdate(nameof(UpdateMetrics), () => UpdateMetrics(), "* * * * *");

        Log.Information($"RecurringJob: Adding {nameof(AddLatestDiscordSupporters)}");
        RecurringJob.AddOrUpdate(nameof(AddLatestDiscordSupporters), () => AddLatestDiscordSupporters(), "*/4 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(CheckExpiredDiscordSupporters)}");
        RecurringJob.AddOrUpdate(nameof(CheckExpiredDiscordSupporters), () => CheckExpiredDiscordSupporters(), "0 8,18 * * *");

        Log.Information($"RecurringJob: Adding {nameof(PickNewFeatureds)}");
        RecurringJob.AddOrUpdate(nameof(PickNewFeatureds), () => PickNewFeatureds(), "0 12 * * *");

        Log.Information($"RecurringJob: Adding {nameof(CheckForNewOcSupporters)}");
        RecurringJob.AddOrUpdate(nameof(CheckForNewOcSupporters), () => CheckForNewOcSupporters(), "*/3 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(UpdateExistingOcSupporters)}");
        RecurringJob.AddOrUpdate(nameof(UpdateExistingOcSupporters), () => UpdateExistingOcSupporters(), "0 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(CheckDiscordSupportersUserType)}");
        RecurringJob.AddOrUpdate(nameof(CheckDiscordSupportersUserType), () => CheckDiscordSupportersUserType(), "*/10 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(UpdateBotLists)}");
        RecurringJob.AddOrUpdate(nameof(UpdateBotLists), () => UpdateBotLists(), "*/10 * * * *");

        Log.Information($"RecurringJob: Adding {nameof(UpdateDiscogsUsers)}");
        RecurringJob.AddOrUpdate(nameof(UpdateDiscogsUsers), () => UpdateDiscogsUsers(), "0 12 * * *");
    }

    public async Task UpdateStatus()
    {
        Log.Information($"Running {nameof(UpdateStatus)}");

        try
        {
            if (this.CurrentFeatured?.Status != null)
            {
                await this._client.SetCustomStatusAsync(this.CurrentFeatured.Status);
                return;
            }

            if (this._client?.Guilds?.Count == null)
            {
                Log.Information($"Client guild count is null, cancelling {nameof(UpdateStatus)}");
                return;
            }

            Statistics.ConnectedShards.Set(this._client.Shards.Count(c => c.ConnectionState == ConnectionState.Connected));
            Statistics.ConnectedDiscordServerCount.Set(this._client.Guilds.Count);

            if (string.IsNullOrEmpty(this._botSettings.Bot.Status) &&
                !string.IsNullOrWhiteSpace(this._botSettings.ApiConfig?.InternalEndpoint))
            {
                if (!PublicProperties.IssuesAtLastFm)
                {
                    var overview = await this._statusHandler.GetOverviewAsync(new Empty());
                    await this._client.SetCustomStatusAsync(
                        $"{this._botSettings.Bot.Prefix}fm — fmbot.xyz — {overview.TotalGuilds} servers");
                }
                else
                {
                    await this._client.SetCustomStatusAsync(
                        $"⚠️ Last.fm is currently experiencing issues -> twitter.com/lastfmstatus");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, nameof(UpdateStatus));
            throw;
        }
    }

    public async Task UpdateMetrics()
    {
        Log.Information($"Running {nameof(UpdateMetrics)}");

        Statistics.RegisteredUserCount.Set(await this._userService.GetTotalUserCountAsync());
        Statistics.AuthorizedUserCount.Set(await this._userService.GetTotalAuthorizedUserCountAsync());
        Statistics.UniqueUserCount.Set(await this._userService.GetTotalGroupedLastfmUserCountAsync());

        Statistics.RegisteredGuildCount.Set(await this._guildService.GetTotalGuildCountAsync());

        Statistics.ActiveSupporterCount.Set(await this._supporterService.GetActiveSupporterCountAsync());
        Statistics.ActiveDiscordSupporterCount.Set(await this._supporterService.GetActiveDiscordSupporterCountAsync());
        Statistics.ActiveStripeSupporterCount.Set(await this._supporterService.GetActiveStripeSupporterCountAsync());

        Statistics.OneDayActiveUserCount.Set(await this._userService.GetTotalActiveUserCountAsync(1));
        Statistics.SevenDayActiveUserCount.Set(await this._userService.GetTotalActiveUserCountAsync(7));
        Statistics.ThirtyDayActiveUserCount.Set(await this._userService.GetTotalActiveUserCountAsync(30));

        if (!string.IsNullOrWhiteSpace(this._botSettings.ApiConfig?.InternalEndpoint))
        {
            var overview = await this._statusHandler.GetOverviewAsync(new Empty());
            Statistics.TotalDiscordServerCount.Set(overview.TotalGuilds);
        }

        try
        {
            if (this._client?.Guilds?.Count == null)
            {
                Log.Information($"Client guild count is null, cancelling {nameof(UpdateMetrics)}");
                return;
            }

            Statistics.ConnectedShards.Set(this._client.Shards.Count(c => c.ConnectionState == ConnectionState.Connected));
            Statistics.ConnectedDiscordServerCount.Set(this._client.Guilds.Count);
        }
        catch (Exception e)
        {
            Log.Error(e, nameof(UpdateMetrics));
            throw;
        }
    }

    public async Task UpdateHealthCheck()
    {
        Log.Debug($"Running {nameof(UpdateHealthCheck)}");

        try
        {
            Statistics.UpdateQueueSize.Set(this._updateQueueHandler.GetQueueSize());

            var allShardsConnected = this._client.Shards.All(shard => shard.ConnectionState != ConnectionState.Disconnected) &&
                                this._client.Shards.All(shard => shard.ConnectionState != ConnectionState.Disconnecting) &&
                                this._client.Shards.All(shard => shard.ConnectionState != ConnectionState.Connecting);

            if (allShardsConnected)
            {
                const string path = "healthcheck";
                await using var fileStream = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }

            var currentProcess = Process.GetCurrentProcess();
            var currentMemoryUsage = currentProcess.WorkingSet64;

            if (!string.IsNullOrWhiteSpace(this._botSettings.ApiConfig?.InternalEndpoint))
            {
                await this._statusHandler.SendHeartbeatAsync(new InstanceHeartbeat
                {
                    InstanceName = ConfigData.Data.Shards?.InstanceName ?? "unknown",
                    ConnectedGuilds = this._client?.Guilds?.Count(c => c.IsConnected) ?? 0,
                    TotalGuilds = this._client?.Guilds?.Count ?? 0,
                    ConnectedShards = this._client?.Shards?.Count(c => c.ConnectionState == ConnectionState.Connected) ?? 0,
                    TotalShards = this._client?.Shards?.Count ?? 0,
                    MemoryBytesUsed = currentMemoryUsage
                });
            }
        }
        catch (Exception e)
        {
            Log.Error(e, nameof(UpdateHealthCheck));
            throw;
        }
    }

    public async Task AddUsersToIndexQueue()
    {
        if (PublicProperties.IssuesAtLastFm)
        {
            Log.Information($"Skipping {nameof(AddUsersToIndexQueue)} - issues at Last.fm");
            return;
        }

        Log.Information("Getting users to index");
        var timeToIndex = DateTime.UtcNow.AddDays(-120);

        var usersToIndex = (await this._indexService.GetOutdatedUsers(timeToIndex))
            .Take(2000)
            .ToList();

        Log.Information($"Found {usersToIndex.Count} outdated users, adding them to index queue");

        var indexDelay = DateTime.UtcNow;
        var indexCount = 1;

        foreach (var userToUpdate in usersToIndex)
        {
            var updateUserQueueItem = new IndexUserQueueItem
            {
                UserId = userToUpdate.UserId,
                IndexQueue = true
            };

            BackgroundJob.Schedule(() => this._indexService.IndexUser(updateUserQueueItem), indexDelay);
            indexDelay = indexDelay.AddSeconds(20);
            indexCount++;
        }

        Log.Information("Found {usersToIndexCount} outdated users, adding them to index queue - end time {endTime}", indexCount, indexDelay);
    }

    public async Task AddUsersToUpdateQueue()
    {
        Log.Information("Getting users to update");
        var authorizedTimeToUpdate = DateTime.UtcNow.AddHours(-this._botSettings.LastFm.UserUpdateFrequencyInHours.Value);
        var unauthorizedTimeToUpdate = DateTime.UtcNow.AddHours(-(this._botSettings.LastFm.UserUpdateFrequencyInHours.Value + 48));

        var usersToUpdate = await this._updateService.GetOutdatedUsers(authorizedTimeToUpdate, unauthorizedTimeToUpdate);
        Log.Information("Found {usersToUpdateCount} outdated users - adding them to update queue", usersToUpdate.Count);

        Statistics.UpdateOutdatedUsers.Set(usersToUpdate.Count);

        var indexDelay = DateTime.UtcNow;

        var updateCount = 1;
        var indexCount = 1;

        foreach (var userToUpdate in usersToUpdate)
        {
            if (userToUpdate.LastUpdated < DateTime.UtcNow.AddMonths(-3))
            {
                var updateUserQueueItem = new IndexUserQueueItem
                {
                    UserId = userToUpdate.UserId,
                    IndexQueue = true
                };

                BackgroundJob.Schedule(() => this._indexService.IndexUser(updateUserQueueItem), indexDelay);
                indexDelay = indexDelay.AddSeconds(20);
                indexCount++;
            }
            else
            {
                var updateUserQueueItem = new UpdateUserQueueItem
                {
                    UserId = userToUpdate.UserId,
                    UpdateQueue = true,
                    GetAccurateTotalPlaycount = false
                };

                this._updateQueueHandler.EnqueueUser(updateUserQueueItem);
                updateCount++;
            }
        }

        if (this._updateQueueCancellationToken != null)
        {
            await this._updateQueueCancellationToken.CancelAsync();
            Log.Information("Cancelled previous update queue");
        }

        this._updateQueueCancellationToken = new CancellationTokenSource();
        _ = StartProcessingUpdateQueue(this._updateQueueCancellationToken.Token);

        Log.Information("Found {usersToIndexCount} outdated users to index - added all to queue - end time {endTime}", indexCount, indexDelay);
        Log.Information("Found {usersToUpdateCount} outdated users to update - added all to queue", updateCount);
    }

    public Task StartProcessingUpdateQueue(CancellationToken cancellationToken)
    {
        return _updateQueueHandler.ProcessQueueAsync(cancellationToken);
    }

    public async Task CheckForNewFeatured()
    {
        var newFeatured = await this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow);

        if (newFeatured == null)
        {
            Log.Warning("Featured: No new featured ready");
            return;
        }

        if (newFeatured.DateTime == this.CurrentFeatured?.DateTime && newFeatured.HasFeatured)
        {
            return;
        }

        var cached = (string)this._cache.Get("avatar");

        var mainGuildConnected = this._client.Guilds.Any(a => a.Id == ConfigData.Data.Bot.BaseServerId);
        if (mainGuildConnected && cached != newFeatured.ImageUrl)
        {
            try
            {
                await this._webhookService.ChangeToNewAvatar(this._client, newFeatured.ImageUrl);
                this._cache.Set("avatar", newFeatured.ImageUrl, TimeSpan.FromMinutes(30));
            }
            catch
            {
                // ignored
            }
        }

        if (this._client.CurrentUser.Id == Constants.BotProductionId && mainGuildConnected && !newFeatured.HasFeatured && newFeatured.NoUpdate != true)
        {
            Log.Information("Featured: Posting new featured to webhooks");

            await this._webhookService.PostFeatured(newFeatured, this._client);
            await this._featuredService.SetFeatured(newFeatured);

            if (newFeatured.FeaturedMode == FeaturedMode.RecentPlays)
            {
                await this._featuredService.ScrobbleTrack(this._client.CurrentUser.Id, newFeatured);
            }

            _ = this._webhookService.SendFeaturedWebhooks(newFeatured);
        }

        Log.Information($"{nameof(CheckForNewFeatured)}: Setting new featured in bot");
        this.CurrentFeatured = await this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow);
    }

    public async Task CheckForNewOcSupporters()
    {
        await this._supporterService.CheckForNewOcSupporters();
    }

    public async Task UpdateExistingOcSupporters()
    {
        await this._supporterService.UpdateExistingOpenCollectiveSupporters();
    }

    public async Task AddLatestDiscordSupporters()
    {
        await this._supporterService.AddLatestDiscordSupporters();
    }

    public async Task CheckExpiredDiscordSupporters()
    {
        await this._supporterService.CheckExpiredDiscordSupporters();
    }

    public async Task CheckDiscordSupportersUserType()
    {
        await this._supporterService.CheckIfDiscordSupportersHaveCorrectUserType();
    }

    public async Task UpdateDiscogsUsers()
    {
        var usersToUpdate = await this._discogsService.GetOutdatedDiscogsUsers();
        await this._discogsService.UpdateDiscogsUsers(usersToUpdate);
    }

    public async Task PickNewFeatureds()
    {
        for (var i = 0; i <= 32; i++)
        {
            var dateTime = DateTime.UtcNow.AddHours(i);
            var featuredDateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, Constants.FeaturedMinute, 0);
            var hourFeatured = await this._featuredService.GetFeaturedForDateTime(featuredDateTime);

            if (hourFeatured == null)
            {
                var newFeatured = await this._featuredService.NewFeatured(featuredDateTime);

                if (newFeatured == null)
                {
                    Log.Warning("Featured: Could not pick a new one for {dateTime}", featuredDateTime);
                    return;
                }

                await this._featuredService.AddFeatured(newFeatured);
                Log.Information("Featured: Added future feature for {dateTime}", featuredDateTime);

                if (!string.IsNullOrWhiteSpace(this._botSettings.Bot.FeaturedPreviewWebhookUrl))
                {
                    await this._webhookService.SendFeaturedPreview(newFeatured, this._botSettings.Bot.FeaturedPreviewWebhookUrl);
                }
            }
        }
    }

    public void CheckIfShardsNeedReconnect()
    {
        Log.Debug("ShardReconnectTimer: Running shard reconnect timer");

        var currentProcess = Process.GetCurrentProcess();
        var startTime = DateTime.Now - currentProcess.StartTime;

        if (startTime.Minutes <= 15)
        {
            Log.Information($"Skipping {nameof(CheckIfShardsNeedReconnect)} because bot only just started");
            return;
        }

        var shards = this._client.Shards;

        foreach (var shard in shards.Where(w => w.ConnectionState == ConnectionState.Disconnected))
        {
            var cacheKey = $"{shard.ShardId}-shard-disconnected";
            if (this._cache.TryGetValue(cacheKey, out int shardDisconnected))
            {
                if (shardDisconnected > 7 && !this._cache.TryGetValue("shard-connecting", out _))
                {
                    this._cache.Set("shard-connecting", 1, TimeSpan.FromSeconds(15));
                    Log.Information("ShardReconnectTimer: Reconnecting shard #{shardId}", shard.ShardId);
                    _ = shard.StartAsync();
                    this._cache.Remove(cacheKey);
                }
                else
                {
                    shardDisconnected++;
                    this._cache.Set(cacheKey, shardDisconnected, TimeSpan.FromMinutes(25 - shardDisconnected));
                    Log.Information("ShardReconnectTimer: Shard #{shardId} has been disconnected {shardDisconnected} times", shard.ShardId, shardDisconnected);
                }
            }
            else
            {
                Log.Information("ShardReconnectTimer: Shard #{shardId} has been disconnected one time", shard.ShardId);
                this._cache.Set(cacheKey, 1, TimeSpan.FromMinutes(25));
            }
        }
    }

    public async Task UpdateGlobalWhoKnowsFilters()
    {
        var filteredUsers = await this._whoKnowsFilterService.GetNewGlobalFilteredUsers();
        await this._whoKnowsFilterService.AddFilteredUsersToDatabase(filteredUsers);
    }

    public async Task UpdateEurovisionData()
    {
        await this._eurovisionService.UpdateEurovisionData();
    }

    public void ClearUserCache()
    {
        var clients = this._client.Shards;
        foreach (var socketClient in clients)
        {
            socketClient.PurgeUserCache();
        }
        Log.Information("Purged discord caches");
    }

    public void ClearInternalLogs()
    {
        PublicProperties.UsedCommandsResponses = new ConcurrentDictionary<ulong, CommandResponse>();
        PublicProperties.UsedCommandsResponseMessageId = new ConcurrentDictionary<ulong, ulong>();
        PublicProperties.UsedCommandsResponseContextId = new ConcurrentDictionary<ulong, ulong>();
        PublicProperties.UsedCommandsErrorReferences = new ConcurrentDictionary<ulong, string>();
        PublicProperties.UsedCommandDiscordUserIds = new ConcurrentDictionary<ulong, ulong>();
        PublicProperties.UsedCommandsArtists = new ConcurrentDictionary<ulong, string>();
        PublicProperties.UsedCommandsAlbums = new ConcurrentDictionary<ulong, string>();
        PublicProperties.UsedCommandsTracks = new ConcurrentDictionary<ulong, string>();
        PublicProperties.UsedCommandsReferencedMusic = new ConcurrentDictionary<ulong, ReferencedMusic>();

        Log.Information("Cleared internal logs");
    }

    public async Task UpdateBotLists()
    {
        await this._botListService.UpdateBotLists();
    }
}
