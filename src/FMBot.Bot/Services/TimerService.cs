using System;
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
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using Serilog;
using Image = Discord.Image;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Timer _timer;
        private readonly Timer _internalStatsTimer;
        private readonly Timer _userUpdateTimer;
        private readonly Timer _purgeCacheTimer;
        private readonly Timer _userIndexTimer;
        private readonly LastFmRepository _lastFmRepository;
        private readonly UserService _userService;
        private readonly CensorService _censorService;
        private readonly IUpdateService _updateService;
        private readonly IIndexService _indexService;
        private readonly GuildService _guildService;
        private readonly DiscordShardedClient _client;
        private readonly WebhookService _webhookService;
        private readonly BotSettings _botSettings;

        private bool _timerEnabled;

        private string _featuredTrackString = "No featured picked since last bot startup. Please wait for a new featured user to be chosen.";
        private int _featuredUserId = 0;

        public TimerService(DiscordShardedClient client,
            LastFmRepository lastFmRepository,
            IUpdateService updateService,
            UserService userService,
            IIndexService indexService,
            CensorService censorService,
            GuildService guildService,
            WebhookService webhookService,
            IOptions<BotSettings> botSettings)
        {
            this._client = client;
            this._lastFmRepository = lastFmRepository;
            this._userService = userService;
            this._indexService = indexService;
            this._censorService = censorService;
            this._guildService = guildService;
            this._webhookService = webhookService;
            this._updateService = updateService;
            this._botSettings = botSettings.Value;

            this._timer = new Timer(async _ =>
                {
                    var random = new Random();
                    var randomAvatarMode = random.Next(1, 4);

                    if (!Enum.TryParse(randomAvatarMode.ToString(), out FeaturedMode featuredMode))
                    {
                        return;
                    }

                    var randomAvatarModeDesc = "";

                    switch (featuredMode)
                    {
                        case FeaturedMode.RecentPlays:
                            randomAvatarModeDesc = "Recent listens";
                            break;
                        case FeaturedMode.TopAlbumsWeekly:
                            randomAvatarModeDesc = "Weekly albums";
                            break;
                        case FeaturedMode.TopAlbumsMonthly:
                            randomAvatarModeDesc = "Monthly albums";
                            break;
                        case FeaturedMode.TopAlbumsAllTime:
                            randomAvatarModeDesc = "Overall albums";
                            break;
                    }

                    Log.Information($"Featured: Picked mode {randomAvatarMode} - {randomAvatarModeDesc}");

                    try
                    {
                        var user = await this._userService.GetUserToFeatureAsync();
                        Log.Information($"Featured: Picked user {user.UserId} / {user.UserNameLastFM}");

                        switch (featuredMode)
                        {
                            // Recent listens
                            case FeaturedMode.RecentPlays:
                                var tracks = await this._lastFmRepository.GetRecentTracksAsync(user.UserNameLastFM, 50, sessionKey: user.SessionKeyLastFm);

                                if (!tracks.Success || tracks.Content == null)
                                {
                                    Log.Information($"Featured: User {user.UserNameLastFM} had no recent tracks, switching to alternative avatar mode");
                                    goto case FeaturedMode.TopAlbumsWeekly;
                                }

                                RecentTrack trackToFeature = null;
                                for (var j = 0; j < tracks.Content.RecentTracks.Count; j++)
                                {
                                    var track = tracks.Content.RecentTracks[j];
                                    if (!string.IsNullOrEmpty(track.AlbumName) && await this._censorService.AlbumIsSafe(track.AlbumName, track.ArtistName))
                                    {
                                        trackToFeature = track;
                                    }
                                }

                                if (trackToFeature == null)
                                {
                                    Log.Information("Featured: No albums or nsfw filtered, switching to alternative avatar mode");
                                    goto case FeaturedMode.TopAlbumsMonthly;
                                }

                                var albumImages = await this._lastFmRepository.GetAlbumImagesAsync(trackToFeature.ArtistName, trackToFeature.AlbumName);

                                this._featuredTrackString = $"[{trackToFeature.AlbumName}]({trackToFeature.TrackUrl}) \n" +
                                                   $"by [{trackToFeature.ArtistName}]({trackToFeature.ArtistUrl}) \n \n" +
                                                   $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
                                this._featuredUserId = user.UserId;

                                Log.Information("Featured: Changing avatar to: " + this._featuredTrackString);

                                if (albumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, albumImages.Large.AbsoluteUri);
                                    LogFeaturedTrack(client, trackToFeature, user.UserId, featuredMode, this._featuredTrackString);
                                }
                                else
                                {
                                    Log.Information("Featured: Recent listen had no image, switching to alternative avatar mode");
                                    goto case FeaturedMode.TopAlbumsAllTime;
                                }

                                break;
                            case FeaturedMode.TopAlbumsWeekly:
                            case FeaturedMode.TopAlbumsMonthly:
                            case FeaturedMode.TopAlbumsAllTime:
                                var timespan = TimePeriod.Weekly;
                                switch (featuredMode)
                                {
                                    case FeaturedMode.TopAlbumsMonthly:
                                        timespan = TimePeriod.Monthly;
                                        break;
                                    case FeaturedMode.TopAlbumsAllTime:
                                        timespan = TimePeriod.AllTime;
                                        break;
                                }

                                var albums = await this._lastFmRepository.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 10);

                                if (!albums.Success || !albums.Content.TopAlbums.Any())
                                {
                                    Log.Information($"Featured: User {user.UserNameLastFM} had no albums, switching to different user.");
                                    user = await this._userService.GetUserToFeatureAsync();
                                    albums = await this._lastFmRepository.GetTopAlbumsAsync(user.UserNameLastFM, timespan, 15);
                                }

                                var albumList = albums.Content.TopAlbums.ToList();

                                var albumFound = false;
                                var i = 0;

                                while (!albumFound)
                                {
                                    var currentAlbum = albumList[i];

                                    var albumImage = await this._lastFmRepository.GetAlbumImagesAsync(currentAlbum.ArtistName, currentAlbum.AlbumName);

                                    this._featuredTrackString = $"[{currentAlbum.AlbumName}]({currentAlbum.AlbumUrl}) \n" +
                                                        $"by {currentAlbum.ArtistName} \n \n" +
                                                        $"{randomAvatarModeDesc} from {user.UserNameLastFM}";
                                    this._featuredUserId = user.UserId;

                                    if (albumImage?.Large != null &&
                                        await this._censorService.AlbumIsSafe(currentAlbum.AlbumName, currentAlbum.ArtistName) &&
                                        await this._censorService.AlbumNotFeaturedRecently(currentAlbum.AlbumName, currentAlbum.ArtistName)
                                        )
                                    {
                                        Log.Information($"Featured: Album {i} success, changing avatar to: \n" +
                                                         $"{this._featuredTrackString}");

                                        ChangeToNewAvatar(client, albumImage.Large.AbsoluteUri);
                                        LogFeaturedAlbum(client, currentAlbum, user.UserId, featuredMode, this._featuredTrackString);
                                        albumFound = true;
                                    }
                                    else
                                    {
                                        Log.Information($"Featured: Album {i} had no image, recently featured or was nsfw, switching to alternative album");
                                        i++;
                                    }
                                }

                                break;
                            default:
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "ChangeFeaturedAvatar");
                    }
                },
                null,
                TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds + this._botSettings.Bot.FeaturedTimerStartupDelayInSeconds), // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(this._botSettings.Bot.FeaturedTimerRepeatInMinutes)); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

            this._internalStatsTimer = new Timer(async _ =>
                {
                    Log.Information("Updating metrics");
                    Statistics.DiscordServerCount.Set(client.Guilds.Count);

                    try
                    {
                        Statistics.RegisteredUserCount.Set(await this._userService.GetTotalUserCountAsync());
                        Statistics.AuthorizedUserCount.Set(await this._userService.GetTotalAuthorizedUserCountAsync());
                        Statistics.RegisteredGuildCount.Set(await this._guildService.GetTotalGuildCountAsync());
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "UpdatingMetrics");
                        Console.WriteLine(e);
                    }

                    if (string.IsNullOrEmpty(this._botSettings.Bot.Status))
                    {
                        Log.Information("Updating status");
                        if (!PublicProperties.IssuesAtLastFm)
                        {
                            await client.SetGameAsync($"{this._botSettings.Bot.Prefix}fm | {client.Guilds.Count} servers | fmbot.xyz");
                        }
                        else
                        {
                            await client.SetGameAsync($"⚠️ Last.fm is currently experiencing issues -> twitter.com/lastfmstatus");
                        }

                    }
                },
                null,
                TimeSpan.FromSeconds(this._botSettings.Bot.BotWarmupTimeInSeconds + 5),
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
                    var unauthorizedTimeToUpdate = DateTime.UtcNow.AddHours(-(this._botSettings.LastFm.UserUpdateFrequencyInHours.Value + 24));

                    var usersToUpdate = await this._updateService.GetOutdatedUsers(authorizedTimeToUpdate, unauthorizedTimeToUpdate);
                    Log.Information($"Found {usersToUpdate.Count} outdated users, adding them to update queue");

                    this._updateService.AddUsersToUpdateQueue(usersToUpdate);
                },
                null,
                TimeSpan.FromMinutes(5),
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
                        .Take(400)
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
                this._timer.Change(Timeout.Infinite, Timeout.Infinite);
                this._timerEnabled = false;
            }
        }

        public void Restart()
        {
            this._timer.Change(TimeSpan.FromSeconds(0),
                TimeSpan.FromMinutes(Convert.ToDouble(this._botSettings.Bot.FeaturedTimerRepeatInMinutes)));
            this._timerEnabled = true;
        }

        public async void ChangeToNewAvatar(DiscordShardedClient client, string imageUrl)
        {
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
                    Log.Information("Avatar succesfully changed");
                }

                await Task.Delay(7500);

                var builder = new EmbedBuilder();
                var selfUser = client.CurrentUser;
                builder.WithThumbnailUrl(imageUrl);
                builder.AddField("Featured:", this._featuredTrackString);

                var botType = BotTypeExtension.GetBotType(client.CurrentUser.Id);
                await this._webhookService.SendFeaturedWebhooks(botType, this._featuredTrackString, this._featuredUserId, imageUrl);

                if (this._botSettings.Bot.BaseServerId != 0 && this._botSettings.Bot.FeaturedChannelId != 0)
                {
                    var guild = client.GetGuild(this._botSettings.Bot.BaseServerId);
                    if (guild != null)
                    {
                        var channel = guild.GetTextChannel(this._botSettings.Bot.FeaturedChannelId);

                        await channel.SendMessageAsync("", false, builder.Build());
                    }
                }
                else
                {
                    Log.Warning("Featured channel not set, not sending featured message");
                }
            }
            catch (Exception exception)
            {
                Log.Error(exception, nameof(ChangeToNewAvatar));
            }
        }

        public async void LogFeaturedTrack(DiscordShardedClient client, RecentTrack track, int userId, FeaturedMode featuredMode, string description)
        {
            try
            {
                var botUser = await this._userService.GetUserAsync(client.CurrentUser.Id);

                if (botUser?.SessionKeyLastFm != null)
                {
                    await this._lastFmRepository.ScrobbleAsync(botUser, track.ArtistName, track.TrackName, track.AlbumName);
                }

                var botType = BotTypeExtension.GetBotType(client.CurrentUser.Id);

                await this._userService.LogFeatured(userId, featuredMode, botType, description, track.ArtistName, track.AlbumName, track.TrackName);
            }
            catch (Exception exception)
            {
                Log.Error(exception, nameof(LogFeaturedTrack));
            }
        }

        public async void LogFeaturedAlbum(DiscordShardedClient client, TopAlbum album, int userId, FeaturedMode featuredMode, string description)
        {
            try
            {
                var botType = BotTypeExtension.GetBotType(client.CurrentUser.Id);

                await this._userService.LogFeatured(userId, featuredMode, botType, description, album.ArtistName, album.AlbumName);
            }
            catch (Exception exception)
            {
                Log.Error(exception, nameof(LogFeaturedAlbum));
            }
        }

        public async void UseDefaultAvatar(DiscordShardedClient client)
        {
            try
            {
                this._featuredTrackString = "FMBot Default Avatar";
                Log.Information("Changed avatar to: " + this._featuredTrackString);
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

        public async void SetFeatured(string link, string desc, bool stopTimer)
        {
            if (stopTimer && IsTimerActive())
            {
                Stop();
            }
            else if (!stopTimer && !IsTimerActive())
            {
                Restart();
            }

            try
            {
                ChangeToNewAvatar(this._client, link);

                this._featuredTrackString = desc;
                this._featuredUserId = 0;
                Log.Information("Changed featured to: " + this._featuredTrackString);
            }
            catch (Exception e)
            {
                Log.Error("UseCustomAvatarFromLink", e);
            }
        }

        public string GetTrackString()
        {
            return this._featuredTrackString;
        }
        public int GetUserId()
        {
            return this._featuredUserId;
        }

        public bool IsTimerActive()
        {
            return this._timerEnabled;
        }
    }
}
