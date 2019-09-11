using Discord;
using Discord.WebSocket;
using FMBot.Services;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using DiscordBotsList.Api;
using static FMBot.Bot.FMBotUtil;
using Bot.Logger.Interfaces;

namespace FMBot.Bot
{
    public static class FMBotModules
    {
        #region Reliability Service
        public class ReliabilityService
        {
            /*
            First off, before we go into the code, you might notice 4 syntax errors 
            if you are using Visual Studio 2015 or below.
            These syntax errors are features introduced in C# 7, which VS 2015 does not support.
            If you are viewing this in 2017, you will be fine.

            If you are using VS 2015 or lower, everything will compile using the compilers from
            the NuGet package "Microsoft.Net.Compilers".

            -Bitl
            */

            // Credit to foxbot for this. Modified from the original so we can use LoggerService.

            // This service requires that your bot is being run by a daemon that handles
            // Exit Code 1 (or any exit code) as a restart.
            //
            // If you do not have your bot setup to run in a daemon, this service will just
            // terminate the process and the bot will not restart.
            // 
            // Links to daemons:
            // [Powershell (Windows+Unix)] https://gitlab.com/snippets/21444
            // [Bash (Unix)] https://stackoverflow.com/a/697064

            // --- Begin Configuration Section ---
            // How long should we wait on the client to reconnect before resetting?
            private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(120);

            // Should we attempt to reset the client? Set this to false if your client is still locking up.
            private static readonly bool _attemptReset = true;

            // Change log levels if desired:

            // --- End Configuration Section ---

            private readonly DiscordShardedClient _discord;
            private readonly ILogger _logger;


            private CancellationTokenSource _cts;

            public ReliabilityService(DiscordShardedClient discord, ILogger logger)
            {
                _cts = new CancellationTokenSource();
                _discord = discord;
                _logger = logger;

                _discord.ShardConnected += ConnectedAsync;
                _discord.ShardDisconnected += DisconnectedAsync;
            }

            public Task ConnectedAsync(DiscordSocketClient client)
            {
                // Cancel all previous state checks and reset the CancelToken - client is back online
                _logger.Log("Client reconnected, resetting cancel tokens...");
                _cts.Cancel();
                _cts = new CancellationTokenSource();
                _logger.Log("Client reconnected, cancel tokens reset.");

                return Task.CompletedTask;
            }

            public Task DisconnectedAsync(Exception _e, DiscordSocketClient client)
            {
                // Check the state after <timeout> to see if we reconnected
                _logger.Log("Client disconnected, starting timeout task...");
                _ = Task.Delay(_timeout, _cts.Token).ContinueWith(async _ =>
                {
                    _logger.Log("Timeout expired, continuing to check client state...");
                    await CheckStateAsync(client);
                    _logger.Log("State came back okay");
                });

                return Task.CompletedTask;
            }

            private async Task CheckStateAsync(DiscordSocketClient client)
            {
                // Client reconnected, no need to reset
                if (client.ConnectionState == ConnectionState.Connected)
                {
                    return;
                }

                if (_attemptReset)
                {
                    _logger.Log("Attempting to reset the client");

                    Task timeout = Task.Delay(_timeout);
                    Task connect = _discord.StartAsync();
                    Task task = await Task.WhenAny(timeout, connect);

                    if (task == timeout)
                    {
                        _logger.LogError("Client reset timed out (task deadlocked?), killing process");
                        FailFast();
                    }
                    else if (connect.IsFaulted)
                    {
                        _logger.LogError("Critical Error: Client reset faulted, killing process \n" + connect.Exception);
                        FailFast();
                    }
                    else
                    {
                        _logger.Log("Client reset succesfully!");
                    }
                }

                _logger.LogError("Critical Error: Client did not reconnect in time, killing process");
                FailFast();
            }

            private void FailFast()
            {
                Environment.Exit(1);
            }
        }

        #endregion

        #region Timer Service

        public class TimerService
        {
            private readonly Timer _timer; // 2) Add a field like this
                                           // This example only concerns a single timer.
                                           // If you would like to have multiple independant timers,
                                           // you could use a collection such as List<Timer>,
                                           // or even a Dictionary<string, Timer> to quickly get
                                           // a specific Timer instance by name.

            private readonly ILogger _logger;

            private string trackString = "";

            private bool timerEnabled = false;
            private readonly UserService userService = new UserService();
            private readonly LastFMService lastFMService = new LastFMService();

            public TimerService(DiscordShardedClient client, ILogger logger)
            {
                JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();
                _logger = logger;


                _timer = new Timer(async _ =>
                {

                    string LastFMName = await userService.GetRandomLastFMUserAsync();
                    if (LastFMName != null)
                    {
                        Random random = new Random();
                        int randavmode = random.Next(1, 4);
                        string randmodestring = "";

                        if (randavmode == 1)
                        {
                            randmodestring = "1 - Recent Listens";
                        }
                        else if (randavmode == 2)
                        {
                            randmodestring = "2 - Weekly Albums";
                        }
                        else if (randavmode == 3)
                        {
                            randmodestring = "3 - Overall Albums";
                        }
                        else if (randavmode == 4)
                        {
                            randmodestring = "4 - Default Avatar";
                        }


                        _logger.Log("Changing avatar to mode " + randmodestring);

                        string nulltext = "";

                        try
                        {
                            if (randavmode == 1)
                            {
                                PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(LastFMName);
                                LastTrack currentTrack = tracks.Content[0];

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                LastImageSet AlbumImages = null;

                                if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                                {
                                    // use the censored cover.
                                    try
                                    {
                                        UseLocalAvatar(client, cfgjson, AlbumName, ArtistName, LastFMName);
                                        return;
                                    }
                                    catch (Exception)
                                    {
                                        UseDefaultAvatar(client);
                                    }
                                }
                                else
                                {
                                    AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                                    trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                    _logger.Log("Changed avatar to: " + trackString);

                                    if (AlbumImages?.Large != null)
                                    {
                                        ChangeToNewAvatar(client, cfgjson, AlbumImages.Large.AbsoluteUri);
                                    }
                                }

                            }
                            else if (randavmode == 2)
                            {
                                PageResponse<LastAlbum> albums = await lastFMService.GetTopAlbumsAsync(LastFMName, LastStatsTimeSpan.Week, 1);
                                LastAlbum currentAlbum = albums.Content[random.Next(albums.Count())];

                                string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                                LastImageSet AlbumImages = null;

                                if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                                {
                                    // use the censored cover.
                                    try
                                    {
                                        UseLocalAvatar(client, cfgjson, AlbumName, ArtistName, LastFMName);
                                        return;
                                    }
                                    catch (Exception)
                                    {
                                        UseDefaultAvatar(client);
                                    }
                                }
                                else
                                {
                                    AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                                    trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                    _logger.Log("Changed avatar to: " + trackString);

                                    if (AlbumImages?.Large != null)
                                    {
                                        ChangeToNewAvatar(client, cfgjson, AlbumImages.Large.AbsoluteUri);
                                    }
                                }
                            }
                            else if (randavmode == 3)
                            {
                                PageResponse<LastAlbum> albums = await lastFMService.GetTopAlbumsAsync(LastFMName, LastStatsTimeSpan.Overall, 1);
                                LastAlbum currentAlbum = albums.Content[random.Next(albums.Count())];

                                string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                                LastImageSet AlbumImages = null;

                                if (GlobalVars.CensoredAlbums.Contains(new KeyValuePair<string, string>(ArtistName, AlbumName)))
                                {
                                    // use the censored cover.
                                    try
                                    {
                                        UseLocalAvatar(client, cfgjson, AlbumName, ArtistName, LastFMName);
                                    }
                                    catch (Exception)
                                    {
                                        UseDefaultAvatar(client);
                                    }
                                }
                                else
                                {
                                    AlbumImages = await lastFMService.GetAlbumImagesAsync(ArtistName, AlbumName);

                                    trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                    _logger.Log("Changed avatar to: " + trackString);

                                    if (AlbumImages?.Large != null)
                                    {
                                        ChangeToNewAvatar(client, cfgjson, AlbumImages.Large.AbsoluteUri);
                                    }
                                }
                            }
                            else if (randavmode == 4)
                            {
                                UseDefaultAvatar(client);
                            }
                        }
                        catch (Exception e)
                        {
                            ExceptionReporter.ReportShardedException(client, e);
                        }

                        // Update DBL stat

                        try
                        {
                            AuthDiscordBotListApi DblApi =
                                new AuthDiscordBotListApi(client.CurrentUser.Id, cfgjson.DblApiToken);

                            var me = await DblApi.GetMeAsync();

                            await me.UpdateStatsAsync(client.Guilds.Count, client.Shards.Count, client.Shards.Select(s => s.ShardId).ToArray());
                        }
                        catch (Exception e)
                        {
                            ExceptionReporter.ReportShardedException(client, e);
                        }

                    }
                },
                null,
                TimeSpan.FromSeconds(Convert.ToDouble(cfgjson.TimerInit)),  // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(Convert.ToDouble(cfgjson.TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)

                timerEnabled = true;
            }

            public void Stop() // 6) Example to make the timer stop running
            {
                if (IsTimerActive())
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    timerEnabled = false;
                }
            }

            public void Restart() // 7) Example to restart the timer
            {
                if (!IsTimerActive())
                {
                    JsonCfg.ConfigJson cfgjson = JsonCfg.GetJSONData();
                    _timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(cfgjson.TimerInit)), TimeSpan.FromMinutes(Convert.ToDouble(cfgjson.TimerRepeat)));
                    timerEnabled = true;
                }
            }

            public async void ChangeToNewAvatar(DiscordShardedClient client, JsonCfg.ConfigJson cfgjson, string thumbnail)
            {
                try
                {
                    WebRequest request = WebRequest.Create(thumbnail);
                    WebResponse response = await request.GetResponseAsync();
                    using (Stream output = File.Create(GlobalVars.BasePath + "newavatar.png"))
                    using (Stream input = response.GetResponseStream())
                    {
                        input.CopyTo(output);
                        if (File.Exists(GlobalVars.BasePath + "newavatar.png"))
                        {
                            File.SetAttributes(GlobalVars.BasePath + "newavatar.png", FileAttributes.Normal);
                        }

                        output.Close();
                        input.Close();
                    }

                    if (File.Exists(GlobalVars.BasePath + "newavatar.png"))
                    {
                        FileStream fileStream = new FileStream(GlobalVars.BasePath + "newavatar.png", FileMode.Open);
                        await client.CurrentUser.ModifyAsync(u => u.Avatar = new Image(fileStream));
                        fileStream.Close();
                    }

                    await Task.Delay(5000);


                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.FeaturedChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    SocketSelfUser SelfUser = client.CurrentUser;
                    builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                    builder.AddField("Featured:", trackString);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportShardedException(client, e);
                }
            }

            public async void UseDefaultAvatar(DiscordShardedClient client)
            {
                try
                {
                    trackString = "FMBot Default Avatar";
                    _logger.Log("Changed avatar to: " + trackString);
                    FileStream fileStream = new FileStream(GlobalVars.BasePath + "avatar.png", FileMode.Open);
                    Image image = new Image(fileStream);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                    fileStream.Close();
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportShardedException(client, e);
                }
            }

            public async void UseLocalAvatar(DiscordShardedClient client, JsonCfg.ConfigJson cfgjson, string AlbumName, string ArtistName, string LastFMName)
            {
                try
                {
                    trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                    _logger.Log("Changed avatar to: " + trackString);
                    //FileStream fileStream = new FileStream(GlobalVars.CoversFolder + ArtistName + " - " + AlbumName + ".png", FileMode.Open);
                    FileStream fileStream = new FileStream(GlobalVars.BasePath + "censored.png", FileMode.Open);
                    Image image = new Image(fileStream);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                    fileStream.Close();

                    await Task.Delay(5000);

                    ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                    ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.FeaturedChannel);

                    SocketGuild guild = client.GetGuild(BroadcastServerID);
                    SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                    EmbedBuilder builder = new EmbedBuilder();
                    SocketSelfUser SelfUser = client.CurrentUser;
                    builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                    builder.AddField("Featured:", trackString);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportShardedException(client, e);
                }
            }

            public async void UseCustomAvatar(DiscordShardedClient client, string fmquery, string desc, bool artistonly, bool important)
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

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();
                LastfmClient fmclient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                try
                {
                    if (artistonly)
                    {
                        PageResponse<LastArtist> artists = await fmclient.Artist.SearchAsync(fmquery, 1, 2);
                        LastArtist currentArtist = artists.Content[0];

                        string nulltext = "";

                        try
                        {
                            string ArtistName = string.IsNullOrWhiteSpace(currentArtist.Name) ? nulltext : currentArtist.Name;

                            LastResponse<LastArtist> ArtistInfo = await fmclient.Artist.GetInfoAsync(ArtistName);
                            LastImageSet ArtistImages = ArtistInfo.Content.MainImage;
                            string ArtistThumbnail = ArtistImages?.Large.AbsoluteUri;
                            string ThumbnailImage = ArtistThumbnail?.ToString();

                            try
                            {
                                trackString = ArtistName + Environment.NewLine + desc;
                                _logger.Log("Changed avatar to: " + trackString);
                            }
                            catch (Exception)
                            {
                                try
                                {
                                    trackString = desc;
                                    _logger.Log("Changed avatar to: " + trackString);
                                }
                                catch (Exception e)
                                {
                                    ExceptionReporter.ReportShardedException(client, e);
                                    UseDefaultAvatar(client);
                                    trackString = "Unable to get information for this artist avatar.";
                                }
                            }

                            ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                        }
                        catch (Exception e)
                        {
                            ExceptionReporter.ReportShardedException(client, e);
                        }
                    }
                    else
                    {
                        PageResponse<LastAlbum> albums = await fmclient.Album.SearchAsync(fmquery, 1, 2);
                        LastAlbum currentAlbum = albums.Content[0];

                        const string nulltext = "";

                        string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                        string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                        try
                        {
                            LastResponse<LastAlbum> AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                            LastImageSet AlbumImages = AlbumInfo.Content.Images;
                            string AlbumThumbnail = AlbumImages?.Large.AbsoluteUri;
                            string ThumbnailImage = AlbumThumbnail;

                            try
                            {
                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + desc;
                                _logger.Log("Changed avatar to: " + trackString);
                            }
                            catch (Exception)
                            {
                                try
                                {
                                    trackString = desc;
                                    _logger.Log("Changed avatar to: " + trackString);
                                }
                                catch (Exception e)
                                {
                                    ExceptionReporter.ReportShardedException(client, e);
                                    UseDefaultAvatar(client);
                                    trackString = "Unable to get information for this album cover avatar.";
                                }
                            }

                            ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                        }
                        catch (Exception e)
                        {
                            ExceptionReporter.ReportShardedException(client, e);
                        }
                    }
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportShardedException(client, e);

                    UseDefaultAvatar(client);
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

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                try
                {
                    trackString = desc;
                    _logger.Log("Changed avatar to: " + trackString);

                    if (!string.IsNullOrWhiteSpace(link))
                    {
                        ChangeToNewAvatar(client, cfgjson, link);
                    }
                }
                catch (Exception e)
                {
                    ExceptionReporter.ReportShardedException(client, e);
                }
            }

            public string GetTrackString()
            {
                return trackString;
            }

            public bool IsTimerActive()
            {
                return timerEnabled;
            }
        }

        #endregion
    }
}
