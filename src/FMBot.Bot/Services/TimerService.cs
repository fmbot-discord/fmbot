using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using DiscordBotsList.Api;
using FMBot.Bot.Configurations;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.LastFM.Services;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using Serilog;
using static FMBot.Bot.FMBotUtil;
using Image = Discord.Image;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Timer _timer;
        private readonly Timer _externalStatsTimer;
        private readonly Timer _internalStatsTimer;
        private readonly Timer _userUpdateTimer;
        private readonly Timer _userIndexTimer;
        private readonly LastFMService _lastFMService;
        private readonly UserService _userService;
        private readonly CensorService _censorService;
        private readonly IUpdateService _updateService;
        private readonly IIndexService _indexService;
        private readonly GuildService _guildService;
        private readonly DiscordShardedClient _client;

        private bool _timerEnabled;

        private string _trackString = "";

        public TimerService(DiscordShardedClient client,
            LastFMService lastFmService,
            IUpdateService updateService,
            UserService userService,
            IIndexService indexService,
            CensorService censorService)
        {
            this._client = client;
            this._lastFMService = lastFmService;
            this._userService = userService;
            this._indexService = indexService;
            this._censorService = censorService;
            this._guildService = new GuildService();
            this._updateService = updateService;

            this._timer = new Timer(async _ =>
                {
                    var random = new Random();
                    var randomAvatarMode = random.Next(1, 4);
                    var randomAvatarModeDesc = "";

                    switch (randomAvatarMode)
                    {
                        case 1:
                            randomAvatarModeDesc = "Recent listens";
                            break;
                        case 2:
                            randomAvatarModeDesc = "Weekly albums";
                            break;
                        case 3:
                            randomAvatarModeDesc = "Monthly albums";
                            break;
                        case 4:
                            randomAvatarModeDesc = "Overall albums";
                            break;
                    }

                    Log.Information($"Featured: Picked mode {randomAvatarMode} - {randomAvatarModeDesc}");

                    try
                    {
                        var lastFmUserName = await this._userService.GetRandomLastFMUserAsync();
                        Log.Information($"Featured: Picked user {lastFmUserName}");

                        switch (randomAvatarMode)
                        {
                            // Recent listens
                            case 1:
                                var tracks = await this._lastFMService.GetRecentScrobblesAsync(lastFmUserName, 50);

                                if (!tracks.Success || !tracks.Content.Any())
                                {
                                    Log.Information($"Featured: User {lastFmUserName} had no recent tracks, switching to alternative avatar mode");
                                    goto case 2;
                                }

                                LastTrack trackToFeature = null;
                                for (var j = 0; j < tracks.Content.Count; j++)
                                {
                                    var track = tracks.Content[j];
                                    if (!string.IsNullOrEmpty(track.AlbumName) && await this._censorService.AlbumIsSafe(track.AlbumName, track.ArtistName))
                                    {
                                        trackToFeature = track;
                                    }
                                }

                                if (trackToFeature == null)
                                {
                                    Log.Information("Featured: No albums or nsfw filtered, switching to alternative avatar mode");
                                    goto case 3;
                                }

                                var albumImages = await this._lastFMService.GetAlbumImagesAsync(trackToFeature.ArtistName, trackToFeature.AlbumName);

                                this._trackString = $"{trackToFeature.AlbumName} \n" +
                                                   $"by {trackToFeature.ArtistName} \n \n" +
                                                   $"{randomAvatarModeDesc} from {lastFmUserName}";

                                Log.Information("Featured: Changing avatar to: " + this._trackString);

                                if (albumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, albumImages.Large.AbsoluteUri);
                                }
                                else
                                {
                                    Log.Information("Featured: Album had no image, switching to alternative avatar mode");
                                    goto case 4;
                                }

                                break;
                            // Weekly albums
                            case 2:
                            case 3:
                            case 4:
                                var timespan = LastStatsTimeSpan.Week;
                                switch (randomAvatarMode)
                                {
                                    case 3:
                                        timespan = LastStatsTimeSpan.Month;
                                        break;
                                    case 4:
                                        timespan = LastStatsTimeSpan.Overall;
                                        break;
                                }

                                var albums = await this._lastFMService.GetTopAlbumsAsync(lastFmUserName, timespan, 10);

                                if (!albums.Any())
                                {
                                    Log.Information($"Featured: User {lastFmUserName} had no albums, switching to different user.");
                                    lastFmUserName = await this._userService.GetRandomLastFMUserAsync();
                                    albums = await this._lastFMService.GetTopAlbumsAsync(lastFmUserName, timespan, 10);
                                }

                                var albumList = albums
                                    .ToList();

                                var albumFound = false;
                                var i = 0;

                                while (!albumFound)
                                {
                                    var currentAlbum = albumList[i];

                                    var albumImage = await this._lastFMService.GetAlbumImagesAsync(currentAlbum.ArtistName, currentAlbum.Name);

                                    this._trackString = $"{currentAlbum.Name} \n" +
                                                        $"by {currentAlbum.ArtistName} \n \n" +
                                                        $"{randomAvatarModeDesc} from {lastFmUserName}";

                                    if (albumImage?.Large != null && await this._censorService.AlbumIsSafe(currentAlbum.Name, currentAlbum.ArtistName))
                                    {
                                        Log.Information($"Featured: Album {i} success, changing avatar to: \n" +
                                                         $"{this._trackString}");

                                        ChangeToNewAvatar(client, albumImage.Large.AbsoluteUri);
                                        albumFound = true;
                                    }
                                    else
                                    {
                                        Log.Information($"Featured: Album {i} had no image or was nsfw, switching to alternative album");
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
                        Log.Error("ChangeFeaturedAvatar", e);
                    }
                },
                null,
                TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds + ConfigData.Data.Bot.FeaturedTimerStartupDelayInSeconds), // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(ConfigData.Data.Bot.FeaturedTimerRepeatInMinutes)); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

            this._internalStatsTimer = new Timer(async _ =>
                {
                    Log.Information("Updating metrics");
                    Statistics.DiscordServerCount.Set(client.Guilds.Count);

                    try
                    {
                        Statistics.RegisteredUsers.Set(await this._userService.GetTotalUserCountAsync());
                        Statistics.RegisteredGuilds.Set(await this._guildService.GetTotalGuildCountAsync());
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }

                    if (string.IsNullOrEmpty(ConfigData.Data.Bot.Status))
                    {
                        Log.Information("Updating status");
                        if (!PublicProperties.IssuesAtLastFM)
                        {
                            await client.SetGameAsync($"{ConfigData.Data.Bot.Prefix} | {client.Guilds.Count} servers | fmbot.xyz");
                        }
                        else
                        {
                            await client.SetGameAsync($"⚠️ Last.fm is currently experiencing issues -> twitter.com/lastfmstatus");
                        }

                    }
                },
                null,
                TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds + 5),
                TimeSpan.FromMinutes(2));

            this._externalStatsTimer = new Timer(async _ =>
                {
                    if (client.CurrentUser.Id.Equals(Constants.BotProductionId) && ConfigData.Data.BotLists != null && !string.IsNullOrEmpty(ConfigData.Data.BotLists.TopGgApiToken))
                    {
                        Log.Information("Updating top.gg server count");
                        var dblApi = new AuthDiscordBotListApi(Constants.BotProductionId, ConfigData.Data.BotLists.TopGgApiToken);

                        try
                        {
                            var me = await dblApi.GetMeAsync();
                            await me.UpdateStatsAsync(client.Guilds.Count);
                        }
                        catch (Exception e)
                        {
                            Log.Error("Exception while updating top.gg count!", e);
                        }
                    }
                    else
                    {
                        Log.Information("Non-production bot found or top.gg token not entered, cancelling top.gg server count updater");
                        this._externalStatsTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                },
                null,
                TimeSpan.FromSeconds(ConfigData.Data.Bot.BotWarmupTimeInSeconds + 10),
                TimeSpan.FromMinutes(30));

            this._userUpdateTimer = new Timer(async _ =>
                {
                    if (ConfigData.Data.LastFm.UserUpdateFrequencyInHours == null || ConfigData.Data.LastFm.UserUpdateFrequencyInHours == 0)
                    {
                        Log.Warning("No user update frequency set, cancelling user update timer");
                        this._userUpdateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }

                    Log.Information("Getting users to update");
                    var timeToUpdate = DateTime.UtcNow.AddHours(-ConfigData.Data.LastFm.UserUpdateFrequencyInHours.Value);

                    var usersToUpdate = await this._updateService.GetOutdatedUsers(timeToUpdate);
                    Log.Information($"Found {usersToUpdate.Count} outdated users, adding them to queue");

                    this._updateService.AddUsersToUpdateQueue(usersToUpdate);
                },
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromHours(4));

            this._userIndexTimer = new Timer(async _ =>
                {
                    if (ConfigData.Data.LastFm.UserIndexFrequencyInDays == null || ConfigData.Data.LastFm.UserIndexFrequencyInDays == 0)
                    {
                        Log.Warning("No user index frequency set, cancelling user index timer");
                        this._userIndexTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        return;
                    }

                    if (DateTime.UtcNow.Hour <= 11 || DateTime.UtcNow.Hour >= 14)
                    {
                        Log.Information("Skipping index timer - peak hours detected");
                        return;
                    }

                    Log.Information("Getting users to index");
                    var timeToIndex = DateTime.UtcNow.AddDays(-ConfigData.Data.LastFm.UserIndexFrequencyInDays.Value);

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

        public void Restart() // 7) Example to restart the timer
        {
            if (!IsTimerActive())
            {
                this._timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(ConfigData.Data.Bot.FeaturedTimerStartupDelayInSeconds)),
                    TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.Bot.FeaturedTimerRepeatInMinutes)));
                this._timerEnabled = true;
            }
        }

        public async void ChangeToNewAvatar(DiscordShardedClient client, string imageUrl)
        {
            try
            {
                var request = WebRequest.Create(imageUrl);
                var response = await request.GetResponseAsync();
                using (Stream output = File.Create(GlobalVars.ImageFolder + "newavatar.png"))
                using (var input = response.GetResponseStream())
                {
                    input.CopyTo(output);
                    if (File.Exists(GlobalVars.ImageFolder + "newavatar.png"))
                    {
                        File.SetAttributes(GlobalVars.ImageFolder + "newavatar.png", FileAttributes.Normal);
                    }

                    output.Close();
                    Log.Information("New avatar downloaded");
                }

                if (File.Exists(GlobalVars.ImageFolder + "newavatar.png"))
                {
                    var fileStream = new FileStream(GlobalVars.ImageFolder + "newavatar.png", FileMode.Open);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream));
                    fileStream.Close();
                    Log.Information("Avatar succesfully changed");
                }

                await Task.Delay(2500);

                var builder = new EmbedBuilder();
                var selfUser = client.CurrentUser;
                builder.WithThumbnailUrl(selfUser.GetAvatarUrl());
                builder.AddField("Featured:", this._trackString);

                if (ConfigData.Data.Bot.BaseServerId != 0 && ConfigData.Data.Bot.FeaturedChannelId != 0)
                {
                    var guild = client.GetGuild(ConfigData.Data.Bot.BaseServerId);
                    var channel = guild.GetTextChannel(ConfigData.Data.Bot.FeaturedChannelId);

                    await channel.SendMessageAsync("", false, builder.Build());
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

        public async void UseDefaultAvatar(DiscordShardedClient client)
        {
            try
            {
                this._trackString = "FMBot Default Avatar";
                Log.Information("Changed avatar to: " + this._trackString);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "avatar.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();
            }
            catch (Exception e)
            {
                Log.Error("UseDefaultAvatar", e);
            }
        }

        public async void UseLocalAvatar(DiscordShardedClient client, string AlbumName, string ArtistName, string LastFMName)
        {
            try
            {
                this._trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                Log.Information("Changed avatar to: " + this._trackString);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "censored.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();

                await Task.Delay(5000);

                var guild = client.GetGuild(ConfigData.Data.Bot.BaseServerId);
                var channel = guild.GetTextChannel(ConfigData.Data.Bot.FeaturedChannelId);

                var builder = new EmbedBuilder();
                var SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", this._trackString);

                await channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                Log.Error("UseLocalAvatar", e);
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

                this._trackString = desc;
                Log.Information("Changed featured to: " + this._trackString);
            }
            catch (Exception e)
            {
                Log.Error("UseCustomAvatarFromLink", e);
            }
        }

        public string GetTrackString()
        {
            return this._trackString;
        }

        public bool IsTimerActive()
        {
            return this._timerEnabled;
        }
    }
}
