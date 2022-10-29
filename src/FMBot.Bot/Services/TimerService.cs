using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using Image = Discord.Image;

namespace FMBot.Bot.Services;

public class TimerService
{
    private readonly Timer _featuredTimer;
    private readonly Timer _pickNewFeaturedTimer;
    private readonly Timer _internalStatsTimer;
    private readonly Timer _shardReconnectTimer;
    private readonly Timer _userUpdateTimer;
    private readonly Timer _purgeCacheTimer;
    private readonly Timer _checkNewSupporterTimer;
    private readonly Timer _updateSupporterTimer;
    private readonly Timer _userIndexTimer;
    private readonly UserService _userService;
    private readonly IUpdateService _updateService;
    private readonly IIndexService _indexService;
    private readonly GuildService _guildService;
    private readonly DiscordShardedClient _client;
    private readonly WebhookService _webhookService;
    private readonly BotSettings _botSettings;
    private readonly FeaturedService _featuredService;
    private readonly SupporterService _supporterService;
    private readonly IMemoryCache _cache;

    private bool _timerEnabled;

    public FeaturedLog _currentFeatured = null;

    public TimerService(DiscordShardedClient client,
        IUpdateService updateService,
        UserService userService,
        IIndexService indexService,
        GuildService guildService,
        WebhookService webhookService,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        IMemoryCache cache,
        SupporterService supporterService)
    {
        this._client = client;
        this._userService = userService;
        this._indexService = indexService;
        this._guildService = guildService;
        this._webhookService = webhookService;
        this._featuredService = featuredService;
        this._cache = cache;
        this._supporterService = supporterService;
        this._updateService = updateService;
        this._botSettings = botSettings.Value;

        this._currentFeatured = this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow).Result;

        this._featuredTimer = new Timer(async _ =>
            {
                try
                {
                    var newFeatured = await this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow);

                    if (newFeatured == null)
                    {
                        Log.Warning("Featured: No new featured ready");
                        return;
                    }

                    if (newFeatured.DateTime == this._currentFeatured?.DateTime && newFeatured.HasFeatured)
                    {
                        return;
                    }

                    var cached = (string)this._cache.Get("avatar");

                    if (this._botSettings.Bot.MainInstance == true && cached != newFeatured.ImageUrl)
                    {
                        try
                        {
                            await ChangeToNewAvatar(client, newFeatured.ImageUrl);
                            this._cache.Set("avatar", newFeatured.ImageUrl, TimeSpan.FromMinutes(30));
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    if (this._botSettings.Bot.FeaturedMaster == true && !newFeatured.HasFeatured && newFeatured.NoUpdate != true)
                    {
                        Log.Warning("Featured: Posting new featured to webhooks");

                        var botType = BotTypeExtension.GetBotType(client.CurrentUser.Id);
                        await this._webhookService.PostFeatured(newFeatured, client);
                        await this._featuredService.SetFeatured(newFeatured);
                        await this._webhookService.SendFeaturedWebhooks(botType, newFeatured);

                        if (newFeatured.FeaturedMode == FeaturedMode.RecentPlays)
                        {
                            await this._featuredService.ScrobbleTrack(client.CurrentUser.Id, newFeatured);
                        }
                    }

                    this._currentFeatured = await this._featuredService.GetFeaturedForDateTime(DateTime.UtcNow);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Featured: Error in featuredTimer");
                    Console.WriteLine(e);
                }
            },
            null,
            TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds + this._botSettings.Bot.FeaturedTimerStartupDelayInSeconds),
            TimeSpan.FromMinutes(1));

        this._pickNewFeaturedTimer = new Timer(async _ =>
            {
                try
                {
                    if (this._botSettings.Bot.FeaturedMaster != true)
                    {
                        Log.Warning("Featured: FeaturedMaster is not true, cancelling pickNewFeaturedTimer");
                        this._pickNewFeaturedTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }

                    for (var i = 0; i <= 28; i++)
                    {
                        var dateTime = DateTime.UtcNow.AddHours(i);
                        var featuredDateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, Constants.FeaturedMinute, 0);
                        var hourFeatured = await this._featuredService.GetFeaturedForDateTime(featuredDateTime);

                        if (hourFeatured == null)
                        {
                            var newFeatured = await this._featuredService.NewFeatured(client.CurrentUser.Id, featuredDateTime);
                            await this._featuredService.AddFeatured(newFeatured);
                            Log.Information("Featured: Added future feature for {dateTime}", featuredDateTime);

                            if (!string.IsNullOrWhiteSpace(this._botSettings.Bot.FeaturedPreviewWebhookUrl))
                            {
                                await WebhookService.SendFeaturedPreview(newFeatured, this._botSettings.Bot.FeaturedPreviewWebhookUrl);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Featured: Error in pickNewFeaturedTimer");
                    Console.WriteLine(e);
                }
            },
            null,
            TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds),
            TimeSpan.FromHours(12));

        this._checkNewSupporterTimer = new Timer(async _ =>
            {
                try
                {
                    if (this._botSettings.Bot.FeaturedMaster != true)
                    {
                        Log.Warning($"Featured: FeaturedMaster is not true, cancelling {nameof(this._checkNewSupporterTimer)}");
                        this._checkNewSupporterTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }

                    await this._supporterService.CheckForNewSupporters();
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Featured: Error in {nameof(this._checkNewSupporterTimer)}");
                    Console.WriteLine(e);
                }
            },
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2));

        this._updateSupporterTimer = new Timer(async _ =>
            {
                try
                {
                    if (this._botSettings.Bot.FeaturedMaster != true)
                    {
                        Log.Warning($"Featured: FeaturedMaster is not true, cancelling {nameof(this._updateSupporterTimer)}");
                        this._updateSupporterTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }

                    await this._supporterService.UpdateExistingOpenCollectiveSupporters();
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Featured: Error in {nameof(this._updateSupporterTimer)}");
                    Console.WriteLine(e);
                }
            },
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromHours(3));

        this._internalStatsTimer = new Timer(async _ =>
            {
                if (client?.Guilds?.Count == null)
                {
                    Log.Information("Client guild count is null, cancelling");
                    return;
                }

                Log.Information("Updating metrics");

                try
                {
                    var ticks = Stopwatch.GetTimestamp();
                    var upTime = (double)ticks / Stopwatch.Frequency;
                    var upTimeTimeSpan = TimeSpan.FromSeconds(upTime);

                    if (upTimeTimeSpan.Minutes > 10)
                    {
                        Statistics.DiscordServerCount.Set(client.Guilds.Count);
                    }

                    Statistics.RegisteredUserCount.Set(await this._userService.GetTotalUserCountAsync());
                    Statistics.AuthorizedUserCount.Set(await this._userService.GetTotalAuthorizedUserCountAsync());
                    Statistics.RegisteredGuildCount.Set(await this._guildService.GetTotalGuildCountAsync());
                }
                catch (Exception e)
                {
                    Log.Error(e, "UpdatingMetrics");
                    Console.WriteLine(e);
                }

                try
                {
                    if (string.IsNullOrEmpty(this._botSettings.Bot.Status))
                    {
                        Log.Information("Updating status");
                        if (!PublicProperties.IssuesAtLastFm)
                        {
                            await client.SetGameAsync(
                                $"{this._botSettings.Bot.Prefix}fm | {client.Guilds.Count} servers | fmbot.xyz");
                        }
                        else
                        {
                            await client.SetGameAsync(
                                $"⚠️ Last.fm is currently experiencing issues -> twitter.com/lastfmstatus");
                        }

                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "UpdatingMetrics");
                    Console.WriteLine(e);
                }

            },
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2));

        this._shardReconnectTimer = new Timer(async _ =>
            {
                Log.Debug("ShardReconnectTimer: Running shard reconnect timer");

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
            },
            null,
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(2));

        this._userUpdateTimer = new Timer(async _ =>
            {
                if (this._botSettings.LastFm.UserUpdateFrequencyInHours == null || this._botSettings.LastFm.UserUpdateFrequencyInHours == 0)
                {
                    Log.Warning("No user update frequency set, cancelling user update timer");
                    this._userUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                Log.Information("Getting users to update");
                var authorizedTimeToUpdate = DateTime.UtcNow.AddHours(-this._botSettings.LastFm.UserUpdateFrequencyInHours.Value);
                var unauthorizedTimeToUpdate = DateTime.UtcNow.AddHours(-(this._botSettings.LastFm.UserUpdateFrequencyInHours.Value + 48));

                var usersToUpdate = await this._updateService.GetOutdatedUsers(authorizedTimeToUpdate, unauthorizedTimeToUpdate);
                Log.Information($"Found {usersToUpdate.Count} outdated users, adding them to update queue");

                this._updateService.AddUsersToUpdateQueue(usersToUpdate);
            },
            null,
            TimeSpan.FromMinutes(3),
            TimeSpan.FromHours(8));

        this._purgeCacheTimer = new Timer(async _ =>
            {
                try
                {
                    var clients = client.Shards;
                    foreach (var socketClient in clients)
                    {
                        socketClient.PurgeUserCache();
                    }
                    Log.Information("Purged discord caches");
                }
                catch (Exception e)
                {
                    Log.Error(e, "Error while purging cache!");
                    throw;
                }
            },
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromHours(2));

        this._userIndexTimer = new Timer(async _ =>
            {
                if (this._botSettings.LastFm.UserIndexFrequencyInDays == null || this._botSettings.LastFm.UserIndexFrequencyInDays == 0)
                {
                    Log.Warning("No user index frequency set, cancelling user index timer");
                    this._userIndexTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                if (PublicProperties.IssuesAtLastFm)
                {
                    Log.Information("Skipping index timer - issues at Last.fm");
                    return;
                }

                Log.Information("Getting users to index");
                var timeToIndex = DateTime.UtcNow.AddDays(-this._botSettings.LastFm.UserIndexFrequencyInDays.Value);

                var usersToUpdate = (await this._indexService.GetOutdatedUsers(timeToIndex))
                    .Take(500)
                    .ToList();

                Log.Information($"Found {usersToUpdate.Count} outdated users, adding them to index queue");

                this._indexService.AddUsersToIndexQueue(usersToUpdate);
            },
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromHours(1));

        this._timerEnabled = true;
    }

    public void Stop() // 6) Example to make the timer stop running
    {
        if (IsTimerActive())
        {
            this._featuredTimer.Change(Timeout.Infinite, Timeout.Infinite);
            this._pickNewFeaturedTimer.Change(Timeout.Infinite, Timeout.Infinite);
            this._timerEnabled = false;
        }
    }

    public void Restart()
    {
        this._featuredTimer.Change(TimeSpan.FromSeconds(0), TimeSpan.FromMinutes(1));
        this._pickNewFeaturedTimer.Change(TimeSpan.FromSeconds(0), TimeSpan.FromHours(12));
        this._timerEnabled = true;
    }

    public async Task ChangeToNewAvatar(DiscordShardedClient client, string imageUrl)
    {
        Log.Information($"Updating avatar to {imageUrl}");
        try
        {
            var request = WebRequest.Create(imageUrl);
            var response = await request.GetResponseAsync();
            using (Stream output = File.Create(AppDomain.CurrentDomain.BaseDirectory + "newavatar.png"))
            using (var input = response.GetResponseStream())
            {
                input.CopyTo(output);
                if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + "newavatar.png"))
                {
                    File.SetAttributes(AppDomain.CurrentDomain.BaseDirectory + "newavatar.png", FileAttributes.Normal);
                }

                output.Close();
                Log.Information("New avatar downloaded");
            }

            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + "newavatar.png"))
            {
                var fileStream = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "newavatar.png", FileMode.Open);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream));
                fileStream.Close();
                Log.Information("Avatar successfully changed");
            }

            await Task.Delay(5000);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Featured: Error while attempting to change avatar");
            throw;
        }
    }

    public async void UseDefaultAvatar(DiscordShardedClient client)
    {
        try
        {
            this._currentFeatured = new FeaturedLog
            {
                Description = ".fmbot"
            };
            Log.Information("Changed avatar to default");
            var fileStream = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "avatar.png", FileMode.Open);
            var image = new Image(fileStream);
            await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
            fileStream.Close();
        }
        catch (Exception e)
        {
            Log.Error("UseDefaultAvatar", e);
        }
    }

    public bool IsTimerActive()
    {
        return this._timerEnabled;
    }
}
