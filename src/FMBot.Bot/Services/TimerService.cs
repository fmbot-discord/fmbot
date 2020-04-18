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
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using IF.Lastfm.Core.Api.Enums;
using static FMBot.Bot.FMBotUtil;
using Image = Discord.Image;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Logger.Logger _logger;
        private readonly Timer _timer;
        private readonly Timer _internalStatsTimer;
        private readonly Timer _externalStatsTimer;
        private readonly LastFMService _lastFMService = new LastFMService();
        private readonly UserService _userService = new UserService();
        private readonly GuildService _guildService = new GuildService();

        private bool _timerEnabled;

        private string _trackString = "";

        public TimerService(DiscordShardedClient client, Logger.Logger logger)
        {
            this._logger = logger;

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

                    this._logger.Log($"Featured: Picked mode {randomAvatarMode} - {randomAvatarModeDesc}");

                    try
                    {
                        var lastFMUserName = await this._userService.GetRandomLastFMUserAsync();
                        this._logger.Log($"Featured: Picked user {lastFMUserName}");

                        switch (randomAvatarMode)
                        {
                            // Recent listens
                            case 1:
                                var tracks = await this._lastFMService.GetRecentScrobblesAsync(lastFMUserName, 25);
                                var trackList = tracks
                                    .Select(s => new CensoredAlbum(s.ArtistName, s.AlbumName))
                                    .Where(w => !string.IsNullOrEmpty(w.AlbumName) &&
                                        !GlobalVars.CensoredAlbums.Select(s => s.ArtistName).Contains(w.ArtistName) &&
                                        !GlobalVars.CensoredAlbums.Select(s => s.AlbumName).Contains(w.AlbumName));

                                var currentTrack = trackList.First();

                                var albumImages = await this._lastFMService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                                this._trackString = $"{currentTrack.AlbumName} \n" +
                                                   $"by {currentTrack.ArtistName} \n \n" +
                                                   $"{randomAvatarModeDesc} from {lastFMUserName}";

                                this._logger.Log("Featured: Changing avatar to: " + this._trackString);

                                if (albumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, albumImages.Large.AbsoluteUri);
                                }
                                else
                                {
                                    this._logger.Log("Featured: Album had no image, switching to alternative avatar mode");
                                    goto case 3;
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

                                var albums = await this._lastFMService.GetTopAlbumsAsync(lastFMUserName, timespan, 10);

                                if (!albums.Any())
                                {
                                    this._logger.Log($"Featured: User {lastFMUserName} had no albums, switching to different user.");
                                    lastFMUserName = await this._userService.GetRandomLastFMUserAsync();
                                    albums = await this._lastFMService.GetTopAlbumsAsync(lastFMUserName, timespan, 10);
                                }

                                var albumList = albums
                                    .Select(s => new CensoredAlbum(s.ArtistName, s.Name))
                                    .Where(w => !GlobalVars.CensoredAlbums.Select(s => s.ArtistName).Contains(w.ArtistName) &&
                                                !GlobalVars.CensoredAlbums.Select(s => s.AlbumName).Contains(w.AlbumName))
                                    .ToList();

                                var albumFound = false;
                                var i = 0;

                                while (!albumFound)
                                {
                                    var currentAlbum = albumList[i];

                                    var albumImage = await this._lastFMService.GetAlbumImagesAsync(currentAlbum.ArtistName, currentAlbum.AlbumName);

                                    this._trackString = $"{currentAlbum.AlbumName} \n" +
                                                        $"by {currentAlbum.ArtistName} \n \n" +
                                                        $"{randomAvatarModeDesc} from {lastFMUserName}";

                                    if (albumImage?.Large != null)
                                    {
                                        this._logger.Log($"Featured: Album {i} success, changing avatar to: \n" +
                                                         $"{this._trackString}");

                                        ChangeToNewAvatar(client, albumImage.Large.AbsoluteUri);
                                        albumFound = true;
                                    }
                                    else
                                    {
                                        this._logger.Log($"Featured: Album {i} had no image, switching to alternative album");
                                        i++;
                                    }
                                }

                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        this._logger.LogException("ChangeFeaturedAvatar", e);
                    }
                },
                null,
                TimeSpan.FromSeconds(Convert.ToDouble(Constants.BotWarmupTimeInSeconds)), // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

            this._internalStatsTimer = new Timer(async _ =>
                { 
                    logger.Log("Updating metrics and status");
                    Statistics.DiscordServerCount.Set(client.Guilds.Count);
                    Statistics.RegisteredUsers.Set(await this._userService.GetTotalUserCountAsync());
                    Statistics.RegisteredGuilds.Set(await this._guildService.GetTotalGuildCountAsync());

                    await client.SetGameAsync($"{ConfigData.Data.CommandPrefix} | {client.Guilds.Count} servers | fmbot.xyz");
                },
                null,
                TimeSpan.FromSeconds(Constants.BotWarmupTimeInSeconds + 10),
                TimeSpan.FromMinutes(1));

            this._externalStatsTimer = new Timer(async _ =>
                {
                    if (client.CurrentUser.Id.Equals(Constants.BotProductionId) && !string.IsNullOrEmpty(ConfigData.Data.DblApiToken))
                    {
                        logger.Log("Updating top.gg server count");
                        var dblApi = new AuthDiscordBotListApi(Constants.BotProductionId, ConfigData.Data.DblApiToken);

                        try
                        {
                            var me = await dblApi.GetMeAsync();
                            await me.UpdateStatsAsync(client.Guilds.Count);
                        }
                        catch (Exception e)
                        {
                            logger.LogException("Exception while updating top.gg count!", e);
                        }
                    }
                    else
                    {
                        logger.Log("Non-production bot found or top.gg token not entered, cancelling top.gg server count updater");
                        this._externalStatsTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                },
                null,
                TimeSpan.FromSeconds(Constants.BotWarmupTimeInSeconds + 20),
                TimeSpan.FromMinutes(5));

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
                this._timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(ConfigData.Data.TimerInit)),
                    TimeSpan.FromMinutes(Convert.ToDouble(ConfigData.Data.TimerRepeat)));
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
                    this._logger.Log("New avatar downloaded");
                }

                if (File.Exists(GlobalVars.ImageFolder + "newavatar.png"))
                {
                    var fileStream = new FileStream(GlobalVars.ImageFolder + "newavatar.png", FileMode.Open);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream));
                    fileStream.Close();
                    this._logger.Log("Avatar succesfully changed");
                }

                await Task.Delay(2000);

                var broadcastServerId = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var broadcastChannelId = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                var guild = client.GetGuild(broadcastServerId);
                var channel = guild.GetTextChannel(broadcastChannelId);
                 
                var builder = new EmbedBuilder();
                var selfUser = client.CurrentUser;
                builder.WithThumbnailUrl(selfUser.GetAvatarUrl());
                builder.AddField("Featured:", this._trackString);

                await channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                this._logger.LogException("ChangeToNewAvatar", e);
            }
        }

        public async void UseDefaultAvatar(DiscordShardedClient client)
        {
            try
            {
                this._trackString = "FMBot Default Avatar";
                this._logger.Log("Changed avatar to: " + this._trackString);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "avatar.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();
            }
            catch (Exception e)
            {
                this._logger.LogException("UseDefaultAvatar", e);
            }
        }

        public async void UseLocalAvatar(DiscordShardedClient client, string AlbumName, string ArtistName, string LastFMName)
        {
            try
            {
                this._trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                this._logger.Log("Changed avatar to: " + this._trackString);
                //FileStream fileStream = new FileStream(GlobalVars.CoversFolder + ArtistName + " - " + AlbumName + ".png", FileMode.Open);
                var fileStream = new FileStream(GlobalVars.ImageFolder + "censored.png", FileMode.Open);
                var image = new Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();

                await Task.Delay(5000);

                var BroadcastServerID = Convert.ToUInt64(ConfigData.Data.BaseServer);
                var BroadcastChannelID = Convert.ToUInt64(ConfigData.Data.FeaturedChannel);

                var guild = client.GetGuild(BroadcastServerID);
                var channel = guild.GetTextChannel(BroadcastChannelID);

                var builder = new EmbedBuilder();
                var SelfUser = client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddField("Featured:", this._trackString);

                await channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception e)
            {
                this._logger.LogException("UseLocalAvatar", e);
            }
        }

        public async void UseCustomAvatarFromLink(DiscordShardedClient client, string link, string desc, bool important)
        {
            if (important && IsTimerActive())
            {
                Stop();
            }
            else if (!important && !IsTimerActive())
            {
                Restart();
            }

            GlobalVars.FeaturedUserID = "";

            try
            {
                this._trackString = desc;
                this._logger.Log("Changed avatar to: " + this._trackString);

                if (!string.IsNullOrWhiteSpace(link))
                {
                    ChangeToNewAvatar(client, link);
                }
            }
            catch (Exception e)
            {
                this._logger.LogException("UseCustomAvatarFromLink", e);
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
