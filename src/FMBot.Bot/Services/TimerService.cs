using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using IF.Lastfm.Core.Api.Enums;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Services
{
    public class TimerService
    {
        private readonly Logger.Logger _logger;
        private readonly Timer _timer;
        private readonly LastFMService _lastFMService = new LastFMService();
        private readonly UserService _userService = new UserService();

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
                            randomAvatarModeDesc = "Recent Listens";
                            break;
                        case 2:
                            randomAvatarModeDesc = "Weekly Albums";
                            break;
                        case 3:
                            randomAvatarModeDesc = "Monthly Albums";
                            break;
                        case 4:
                            randomAvatarModeDesc = "Overall Albums";
                            break;
                    }

                    try
                    {
                        var lastFMUserName = await this._userService.GetRandomLastFMUserAsync();

                        switch (randomAvatarMode)
                        {
                            // Recent listens
                            case 1:
                                var tracks = await this._lastFMService.GetRecentScrobblesAsync(lastFMUserName, 25);
                                var trackList = tracks
                                    .Select(s => new LastFMModels.Album(s.ArtistName, s.AlbumName))
                                    .Where(w => !string.IsNullOrEmpty(w.AlbumName) && !GlobalVars.CensoredAlbums.Contains(w));

                                var currentTrack = trackList.First();

                                var albumImages = await this._lastFMService.GetAlbumImagesAsync(currentTrack.ArtistName, currentTrack.AlbumName);

                                this._trackString = $"Album: {currentTrack.AlbumName} \n" +
                                                   $"by **{currentTrack.ArtistName}** \n \n" +
                                                   $"User: {lastFMUserName} ({randomAvatarModeDesc})";

                                this._logger.Log("Changing avatar to: " + this._trackString);

                                if (albumImages?.Large != null)
                                {
                                    ChangeToNewAvatar(client, albumImages.Large.AbsoluteUri);
                                }
                                else
                                {
                                    goto case 2;
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

                                var albums = await this._lastFMService.GetTopAlbumsAsync(lastFMUserName, timespan, 6);
                                var albumList = albums
                                    .Select(s => new LastFMModels.Album(s.ArtistName, s.Name))
                                    .Where(w => !GlobalVars.CensoredAlbums.Contains(w))
                                    .ToList();

                                var currentAlbum = albumList[random.Next(0, albumList.Count - 1)];

                                var albumImage = await this._lastFMService.GetAlbumImagesAsync(currentAlbum.ArtistName, currentAlbum.AlbumName);

                                this._trackString = $"Album: {currentAlbum.AlbumName} \n" +
                                                   $"by **{currentAlbum.ArtistName}** \n \n" +
                                                   $"User: {lastFMUserName} ({randomAvatarModeDesc})";

                                if (albumImage?.Large != null)
                                {
                                    this._logger.Log("Changing avatar to: " + this._trackString);

                                    ChangeToNewAvatar(client, albumImage.Large.AbsoluteUri);
                                }
                                else
                                {
                                    this._logger.Log("Featured album had no image, switching to alternative album");

                                    var alternativeAlbum = albumList[albumList.Count];
                                    var alternativeAlbumImage = await this._lastFMService.GetAlbumImagesAsync(alternativeAlbum.ArtistName, alternativeAlbum.AlbumName);

                                    this._trackString = $"Album: {alternativeAlbum.AlbumName} \n" +
                                                       $"by **{alternativeAlbum.ArtistName}** \n \n" +
                                                       $"User: {lastFMUserName} ({randomAvatarModeDesc})";

                                    this._logger.Log("Changing avatar to: " + this._trackString);

                                    ChangeToNewAvatar(client, alternativeAlbumImage.Large.AbsoluteUri);
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
                TimeSpan.FromSeconds(
                    Convert.ToDouble(ConfigData.Data.TimerInit)), // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(
                    Convert.ToDouble(ConfigData.Data.TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

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
