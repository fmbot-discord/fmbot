

using Discord;
using Discord.WebSocket;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Objects;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotModules
    {
        public class LoggerService
        {
            // Change log levels if desired:
            private static readonly LogSeverity _debug = LogSeverity.Debug;
            private static readonly LogSeverity _info = LogSeverity.Info;
            private static readonly LogSeverity _critical = LogSeverity.Critical;

            private static Func<LogMessage, Task> _logger;
            private readonly DiscordSocketClient _discord;

            public LoggerService(DiscordSocketClient discord, Func<LogMessage, Task> logger = null)
            {
                _discord = discord;
                _logger = logger ?? (_ => Task.CompletedTask);
            }

            public static Task DebugAsync(string message, string logSource = "")
                => _logger.Invoke(new LogMessage(_debug, logSource, message));
            public static Task InfoAsync(string message, string logSource = "")
                => _logger.Invoke(new LogMessage(_info, logSource, message));
            public static Task CriticalAsync(string message, string logSource = "", Exception error = null)
                => _logger.Invoke(new LogMessage(_critical, logSource, message, error));
        }

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
            private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

            // Should we attempt to reset the client? Set this to false if your client is still locking up.
            private static readonly bool _attemptReset = true;

            // --- End Configuration Section ---

            private readonly DiscordSocketClient _discord;
            
            private CancellationTokenSource _cts;

            // for the LoggerService
            private const string LogSource = "Reliability";

            public ReliabilityService(DiscordSocketClient discord)
            {
                _cts = new CancellationTokenSource();
                _discord = discord;

                _discord.Connected += ConnectedAsync;
                _discord.Disconnected += DisconnectedAsync;
            }

            public Task ConnectedAsync()
            {
                // Cancel all previous state checks and reset the CancelToken - client is back online
                _ = LoggerService.DebugAsync("Client reconnected, resetting cancel tokens...", LogSource);
                _cts.Cancel();
                _cts = new CancellationTokenSource();
                _ = LoggerService.DebugAsync("Client reconnected, cancel tokens reset.", LogSource);

                return Task.CompletedTask;
            }

            public Task DisconnectedAsync(Exception _e)
            {
                // Check the state after <timeout> to see if we reconnected
                _ = LoggerService.InfoAsync("Client disconnected, starting timeout task...", LogSource);
                _ = Task.Delay(_timeout, _cts.Token).ContinueWith(async _ =>
                {
                    await LoggerService.DebugAsync("Timeout expired, continuing to check client state...", LogSource);
                    await CheckStateAsync();
                    await LoggerService.DebugAsync("State came back okay", LogSource);
                });

                return Task.CompletedTask;
            }

            private async Task CheckStateAsync()
            {
                // Client reconnected, no need to reset
                if (_discord.ConnectionState == ConnectionState.Connected) return;
                if (_attemptReset)
                {
                    await LoggerService.InfoAsync("Attempting to reset the client", LogSource);

                    var timeout = Task.Delay(_timeout);
                    var connect = _discord.StartAsync();
                    var task = await Task.WhenAny(timeout, connect);

                    if (task == timeout)
                    {
                        await LoggerService.CriticalAsync("Client reset timed out (task deadlocked?), killing process", LogSource);
                        FailFast();
                    }
                    else if (connect.IsFaulted)
                    {
                        await LoggerService.CriticalAsync("Client reset faulted, killing process", LogSource, connect.Exception);
                        FailFast();
                    }
                    else
                    {
                        await LoggerService.InfoAsync("Client reset succesfully!", LogSource);
                    }
                }

                await LoggerService.CriticalAsync("Client did not reconnect in time, killing process", LogSource);
                FailFast();
            }

            private void FailFast()
                => Environment.Exit(1);
        }

        public class TimerService
        {
            private readonly Timer _timer; // 2) Add a field like this
                                           // This example only concerns a single timer.
                                           // If you would like to have multiple independant timers,
                                           // you could use a collection such as List<Timer>,
                                           // or even a Dictionary<string, Timer> to quickly get
                                           // a specific Timer instance by name.

            private string trackString = "";

            private const string LogSource = "Timer";

            public TimerService(DiscordSocketClient client)
            {
                var cfgjson = JsonCfg.GetJSONData();

                _timer = new Timer(async _ =>
                {
                    try
                    {
                        string LastFMName = DBase.GetRandFMName();
                        if (!LastFMName.Equals("NULL"))
                        {
                            var fmclient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                            try
                            {
                                var tracks = await fmclient.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                                LastTrack currentTrack = tracks.Content.ElementAt(0);

                                string nulltext = "";

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                try
                                {
                                    var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                                    var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    try
                                    {
                                        ulong DiscordID = DBase.GetIDForName(LastFMName);
                                        SocketUser FeaturedUser = client.GetUser(DiscordID);
                                        trackString = ArtistName + " - " + AlbumName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                    }
                                    catch (Exception)
                                    {
                                        try
                                        {
                                            trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + " - " + AlbumName;
                                            }
                                            catch (Exception)
                                            {
                                                trackString = "Unable to get information for this album cover avatar.";
                                            }
                                        }
                                    }

                                    Console.WriteLine();

                                    await LoggerService.InfoAsync("Changed avatar to: " + trackString, LogSource);

                                    WebRequest request = WebRequest.Create(ThumbnailImage);
                                    WebResponse response = request.GetResponse();
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
                                        var fileStream = new FileStream(GlobalVars.BasePath + "newavatar.png", FileMode.Open);
                                        await client.CurrentUser.ModifyAsync(u => u.Avatar = new Discord.Image(fileStream));
                                        fileStream.Close();
                                    }

                                    await Task.Delay(5000);

                                    try
                                    {
                                        ulong BroadcastServerID = Convert.ToUInt64(cfgjson.BaseServer);
                                        ulong BroadcastChannelID = Convert.ToUInt64(cfgjson.FeaturedChannel);

                                        SocketGuild guild = client.GetGuild(BroadcastServerID);
                                        SocketTextChannel channel = guild.GetTextChannel(BroadcastChannelID);

                                        var builder = new EmbedBuilder();
                                        var SelfUser = client.CurrentUser;
                                        builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                                        builder.AddInlineField("Featured Album:", trackString);

                                        await channel.SendMessageAsync("", false, builder.Build());
                                    }
                                    catch (Exception)
                                    {
                                    }
                                }
                                catch (Exception)
                                {
                                }
                            }
                            catch (Exception)
                            {
                                UseDefaultAvatar(client);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        UseDefaultAvatar(client);
                    }
                },
                null,
                TimeSpan.FromSeconds(Convert.ToDouble(cfgjson.TimerInit)),  // 4) Time that message should fire after the timer is created
                TimeSpan.FromMinutes(Convert.ToDouble(cfgjson.TimerRepeat))); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)
            }

            private async void UseDefaultAvatar(DiscordSocketClient client)
            {
                trackString = "Unable to get information for this album cover avatar.";
                var fileStream = new FileStream(GlobalVars.BasePath + "avatar.png", FileMode.Open);
                var image = new Discord.Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();
            }

            public string GetTrackString()
            {
                return trackString;
            }
        }
    }
}
