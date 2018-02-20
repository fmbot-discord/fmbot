using Discord;
using Discord.WebSocket;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotModules
    {
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

            // Change log levels if desired:
            private static readonly LogSeverity _debug = LogSeverity.Debug;
            private static readonly LogSeverity _info = LogSeverity.Info;
            private static readonly LogSeverity _critical = LogSeverity.Critical;

            // --- End Configuration Section ---

            private readonly DiscordSocketClient _discord;
            private static Func<LogMessage, Task> _logger;

            private CancellationTokenSource _cts;

            public ReliabilityService(DiscordSocketClient discord, Func<LogMessage, Task> logger = null)
            {
                _cts = new CancellationTokenSource();
                _discord = discord;
                _logger = logger ?? (_ => Task.CompletedTask);

                _discord.Connected += ConnectedAsync;
                _discord.Disconnected += DisconnectedAsync;
            }

            public Task ConnectedAsync()
            {
                // Cancel all previous state checks and reset the CancelToken - client is back online
                _ = DebugAsync("Client reconnected, resetting cancel tokens...");
                _cts.Cancel();
                _cts = new CancellationTokenSource();
                _ = DebugAsync("Client reconnected, cancel tokens reset.");

                return Task.CompletedTask;
            }

            public Task DisconnectedAsync(Exception _e)
            {
                // Check the state after <timeout> to see if we reconnected
                _ = InfoAsync("Client disconnected, starting timeout task...");
                _ = Task.Delay(_timeout, _cts.Token).ContinueWith(async _ =>
                {
                    await DebugAsync("Timeout expired, continuing to check client state...");
                    await CheckStateAsync();
                    await DebugAsync("State came back okay");
                });

                return Task.CompletedTask;
            }

            private async Task CheckStateAsync()
            {
                // Client reconnected, no need to reset
                if (_discord.ConnectionState == ConnectionState.Connected) return;
                if (_attemptReset)
                {
                    await InfoAsync("Attempting to reset the client");

                    var timeout = Task.Delay(_timeout);
                    var connect = _discord.StartAsync();
                    var task = await Task.WhenAny(timeout, connect);

                    if (task == timeout)
                    {
                        await CriticalAsync("Client reset timed out (task deadlocked?), killing process");
                        FailFast();
                    }
                    else if (connect.IsFaulted)
                    {
                        await CriticalAsync("Client reset faulted, killing process", connect.Exception);
                        FailFast();
                    }
                    else
                    {
                        await InfoAsync("Client reset succesfully!");
                    }
                }

                await CriticalAsync("Client did not reconnect in time, killing process");
                FailFast();
            }

            private void FailFast()
                => Environment.Exit(1);

            private const string LogSource = "Reliability";

            public static Task DebugAsync(string message)
                => _logger.Invoke(new LogMessage(_debug, LogSource, message));
            public static Task InfoAsync(string message)
                => _logger.Invoke(new LogMessage(_info, LogSource, message));
            public static Task CriticalAsync(string message, Exception error = null)
                => _logger.Invoke(new LogMessage(_critical, LogSource, message, error));
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

            private bool timerEnabled = false;

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
                                Random random = new Random();
                                int randavmode = random.Next(1, 5);

                                try
                                {
                                    if (randavmode == 1)
                                    {
                                        var tracks = await fmclient.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                                        LastTrack currentTrack = tracks.Content.ElementAt(0);

                                        string nulltext = "";

                                        string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                        string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                        string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                        var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                                        var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                        var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                        string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                        try
                                        {
                                            ulong DiscordID = DBase.GetIDForName(LastFMName);
                                            SocketUser FeaturedUser = client.GetUser(DiscordID);
                                            trackString = ArtistName + " - " + AlbumName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                            Console.WriteLine("Changed avatar to: " + trackString);
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                                Console.WriteLine("Changed avatar to: " + trackString);
                                            }
                                            catch (Exception)
                                            {
                                                try
                                                {
                                                    trackString = ArtistName + " - " + AlbumName;
                                                    Console.WriteLine("Changed avatar to: " + trackString);
                                                }
                                                catch (Exception)
                                                {
                                                    trackString = "Unable to get information for this album cover avatar.";
                                                    Console.WriteLine("Unable to get information for this album cover avatar.");
                                                }
                                            }
                                        }

                                        ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                                    }
                                    else if (randavmode == 2)
                                    {
                                        var artists = await fmclient.User.GetTopArtists(LastFMName, LastStatsTimeSpan.Week, 1);
                                        LastArtist currentArtist = artists.Content.ElementAt(random.Next(artists.Count()));

                                        string nulltext = "";

                                        string ArtistName = string.IsNullOrWhiteSpace(currentArtist.Name) ? nulltext : currentArtist.Name;

                                        var ArtistInfo = await fmclient.Artist.GetInfoAsync(ArtistName);
                                        var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                        var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                        string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                        try
                                        {
                                            ulong DiscordID = DBase.GetIDForName(LastFMName);
                                            SocketUser FeaturedUser = client.GetUser(DiscordID);
                                            trackString = ArtistName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                            Console.WriteLine("Changed avatar to: " + trackString);
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + Environment.NewLine + LastFMName;
                                                Console.WriteLine("Changed avatar to: " + trackString);
                                            }
                                            catch (Exception)
                                            {
                                                try
                                                {
                                                    trackString = ArtistName;
                                                    Console.WriteLine("Changed avatar to: " + trackString);
                                                }
                                                catch (Exception)
                                                {
                                                    trackString = "Unable to get information for this artist avatar.";
                                                    Console.WriteLine("Unable to get information for this artist avatar.");
                                                }
                                            }
                                        }

                                        ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                                    }
                                    else if (randavmode == 3)
                                    {
                                        var artists = await fmclient.User.GetTopArtists(LastFMName, LastStatsTimeSpan.Overall, 1);
                                        LastArtist currentArtist = artists.Content.ElementAt(random.Next(artists.Count()));

                                        string nulltext = "";

                                        string ArtistName = string.IsNullOrWhiteSpace(currentArtist.Name) ? nulltext : currentArtist.Name;

                                        var ArtistInfo = await fmclient.Artist.GetInfoAsync(ArtistName);
                                        var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                        var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                        string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                        try
                                        {
                                            ulong DiscordID = DBase.GetIDForName(LastFMName);
                                            SocketUser FeaturedUser = client.GetUser(DiscordID);
                                            trackString = ArtistName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                            Console.WriteLine("Changed avatar to: " + trackString);
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + Environment.NewLine + LastFMName;
                                                Console.WriteLine("Changed avatar to: " + trackString);
                                            }
                                            catch (Exception)
                                            {
                                                try
                                                {
                                                    trackString = ArtistName;
                                                    Console.WriteLine("Changed avatar to: " + trackString);
                                                }
                                                catch (Exception)
                                                {
                                                    trackString = "Unable to get information for this artist avatar.";
                                                    Console.WriteLine("Unable to get information for this artist avatar.");
                                                }
                                            }
                                        }

                                        ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                                    }
                                    else if (randavmode == 4)
                                    {
                                        var albums = await fmclient.User.GetTopAlbums(LastFMName, LastStatsTimeSpan.Week, 1);
                                        LastAlbum currentAlbum = albums.Content.ElementAt(random.Next(albums.Count()));

                                        string nulltext = "";

                                        string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                                        string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                                        var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                                        var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                        var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                        string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                        try
                                        {
                                            ulong DiscordID = DBase.GetIDForName(LastFMName);
                                            SocketUser FeaturedUser = client.GetUser(DiscordID);
                                            trackString = ArtistName + " - " + AlbumName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                            Console.WriteLine("Changed avatar to: " + trackString);
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                                Console.WriteLine("Changed avatar to: " + trackString);
                                            }
                                            catch (Exception)
                                            {
                                                try
                                                {
                                                    trackString = ArtistName + " - " + AlbumName;
                                                    Console.WriteLine("Changed avatar to: " + trackString);
                                                }
                                                catch (Exception)
                                                {
                                                    trackString = "Unable to get information for this album cover avatar.";
                                                    Console.WriteLine("Unable to get information for this album cover avatar.");
                                                }
                                            }
                                        }

                                        ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                                    }
                                    else if (randavmode == 5)
                                    {
                                        var albums = await fmclient.User.GetTopAlbums(LastFMName, LastStatsTimeSpan.Overall, 1);
                                        LastAlbum currentAlbum = albums.Content.ElementAt(random.Next(albums.Count()));

                                        string nulltext = "";

                                        string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                                        string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                                        var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                                        var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                        var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                        string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                        try
                                        {
                                            ulong DiscordID = DBase.GetIDForName(LastFMName);
                                            SocketUser FeaturedUser = client.GetUser(DiscordID);
                                            trackString = ArtistName + " - " + AlbumName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";
                                            Console.WriteLine("Changed avatar to: " + trackString);
                                        }
                                        catch (Exception)
                                        {
                                            try
                                            {
                                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + LastFMName;
                                                Console.WriteLine("Changed avatar to: " + trackString);
                                            }
                                            catch (Exception)
                                            {
                                                try
                                                {
                                                    trackString = ArtistName + " - " + AlbumName;
                                                    Console.WriteLine("Changed avatar to: " + trackString);
                                                }
                                                catch (Exception)
                                                {
                                                    trackString = "Unable to get information for this album cover avatar.";
                                                    Console.WriteLine("Unable to get information for this album cover avatar.");
                                                }
                                            }
                                        }

                                        ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
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

                timerEnabled = true;
            }

            public void Stop() // 6) Example to make the timer stop running
            {
                if (IsTimerActive() == true)
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    timerEnabled = false;
                }
            }

            public void Restart() // 7) Example to restart the timer
            {
                if (IsTimerActive() == false)
                {
                    var cfgjson = JsonCfg.GetJSONData();
                    _timer.Change(TimeSpan.FromSeconds(Convert.ToDouble(cfgjson.TimerInit)), TimeSpan.FromMinutes(Convert.ToDouble(cfgjson.TimerRepeat)));
                    timerEnabled = true;
                }
            }

            public async void ChangeToNewAvatar(DiscordSocketClient client, JsonCfg.ConfigJson cfgjson, string thumbnail)
            {
                WebRequest request = WebRequest.Create(thumbnail);
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
                    builder.AddInlineField("Featured:", trackString);

                    await channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception)
                {
                }
            }

            private async void UseDefaultAvatar(DiscordSocketClient client)
            {
                trackString = "Unable to get information for this avatar.";
                Console.WriteLine("Unable to get information for this avatar.");
                var fileStream = new FileStream(GlobalVars.BasePath + "avatar.png", FileMode.Open);
                var image = new Discord.Image(fileStream);
                await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                fileStream.Close();
            }

            public async void UseCustomAvatar(DiscordSocketClient client, string fmquery, string desc, bool artistonly, bool important)
            {
                if (important == true && IsTimerActive() == true)
                {
                    Stop();
                }
                else if (important == false && IsTimerActive() == false)
                {
                    Restart();
                }

                var cfgjson = await JsonCfg.GetJSONDataAsync();
                var fmclient = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                try
                {
                    if (artistonly == true)
                    {
                        var artists = await fmclient.Artist.SearchAsync(fmquery, 1, 2);
                        LastArtist currentArtist = artists.Content.ElementAt(0);

                        string nulltext = "";

                        try
                        {
                            string ArtistName = string.IsNullOrWhiteSpace(currentArtist.Name) ? nulltext : currentArtist.Name;

                            var ArtistInfo = await fmclient.Artist.GetInfoAsync(ArtistName);
                            var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                            var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                            string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                            try
                            {
                                trackString = ArtistName + Environment.NewLine + desc;
                                Console.WriteLine("Changed avatar to: " + trackString);
                            }
                            catch (Exception)
                            {
                                try
                                {
                                    trackString = desc;
                                    Console.WriteLine("Changed avatar to: " + trackString);
                                }
                                catch (Exception)
                                {
                                    trackString = "Unable to get information for this artist avatar.";
                                    Console.WriteLine("Unable to get information for this artist avatar.");
                                }
                            }

                            ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                        }
                        catch (Exception)
                        {
                        }
                    }
                    else
                    {
                        var albums = await fmclient.Album.SearchAsync(fmquery, 1, 2);
                        LastAlbum currentAlbum = albums.Content.ElementAt(0);

                        string nulltext = "";

                        string ArtistName = string.IsNullOrWhiteSpace(currentAlbum.ArtistName) ? nulltext : currentAlbum.ArtistName;
                        string AlbumName = string.IsNullOrWhiteSpace(currentAlbum.Name) ? nulltext : currentAlbum.Name;

                        try
                        {
                            var AlbumInfo = await fmclient.Album.GetInfoAsync(ArtistName, AlbumName);
                            var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                            var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                            string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                            try
                            {
                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + desc;
                                Console.WriteLine("Changed avatar to: " + trackString);
                            }
                            catch (Exception)
                            {
                                try
                                {
                                    trackString = desc;
                                    Console.WriteLine("Changed avatar to: " + trackString);
                                }
                                catch (Exception)
                                {
                                    trackString = "Unable to get information for this album cover avatar.";
                                    Console.WriteLine("Unable to get information for this album cover avatar.");
                                }
                            }

                            ChangeToNewAvatar(client, cfgjson, ThumbnailImage);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                catch (Exception)
                {
                    UseDefaultAvatar(client);
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
    }
}
