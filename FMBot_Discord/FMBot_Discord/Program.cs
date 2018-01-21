using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Discord;
using Discord.WebSocket;
using Discord.Net.Providers.WS4Net;
using Discord.Commands;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Objects;
using IF.Lastfm.Core.Api.Enums;
using YoutubeSearch;
using System.Collections.Generic;
using System.Net;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;

namespace FMBot_Discord
{
    class Program
    {
        private CommandService commands;
        private DiscordSocketClient client;
        private IServiceProvider services;
        private readonly IServiceCollection map = new ServiceCollection();
        private string prefix;

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            // first, let's load our configuration file
            Console.WriteLine("[FMBot] Loading Configuration");
            var json = "";
            using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            // next, let's load the values from that file
            // to our client's configuration
            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);

            if (!Directory.Exists(GlobalVars.UsersFolder))
            {
                Directory.CreateDirectory(GlobalVars.UsersFolder);
            }

            Console.WriteLine("[FMBot] Initalizing Discord");
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                WebSocketProvider = WS4NetProvider.Instance,
                LogLevel = LogSeverity.Verbose
            });

            client.Log += Log;

            prefix = cfgjson.CommandPrefix;

            await client.SetGameAsync("🎶 Say " + prefix + "fmhelp to use 🎶");
            await client.SetStatusAsync(UserStatus.DoNotDisturb);

            Console.WriteLine("[FMBot] Registering Commands");
            commands = new CommandService();

            string token = cfgjson.Token; // Remember to keep this private!

            await InstallCommands();

            Console.WriteLine("[FMBot] Logging In");
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();

            // Block this task until the program is closed.
            await Task.Delay(-1);
        }

        private Task Log(LogMessage arg)
        {
            Console.WriteLine(arg);

            return Task.CompletedTask;
        }

        public async Task InstallCommands()
        {
            map.AddSingleton(new TimerService(client));
            services = map.BuildServiceProvider();

            await commands.AddModulesAsync(Assembly.GetEntryAssembly());

            client.MessageReceived += HandleCommand_MessageReceived;
            client.MessageUpdated += HandleCommand_MessageEdited;
        }

        public async Task HandleCommand_MessageReceived(SocketMessage messageParam)
        {
            await HandleCommand(messageParam);
        }

        public async Task HandleCommand_MessageEdited(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        {
            await HandleCommand(after);
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;

            var DiscordCaller = message.Author;

            // first, let's load our configuration file
            var json = "";
            using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            // next, let's load the values from that file
            // to our client's configuration
            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!message.HasCharPrefix(Convert.ToChar(prefix), ref argPos)) return;

            // Create a Command Context
            var context = new CommandContext(client, message);

            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await commands.ExecuteAsync(context, argPos, services);
            if (!result.IsSuccess)
            {
                Console.WriteLine("[FMBot]: Error - " + result.Error + ": " + result.ErrorReason);
            }
        }
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

        public TimerService(DiscordSocketClient client)
        {
            _timer = new Timer(async _ =>
            {
                try
                {
                    // first, let's load our configuration file
                    Console.WriteLine("[FMBot] Loading Configuration");
                    var json = "";
                    using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                    using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                        json = await sr.ReadToEndAsync();

                    // next, let's load the values from that file
                    // to our client's configuration
                    var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
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

                                ulong DiscordID = DBase.GetIDForName(LastFMName);
                                SocketUser FeaturedUser = client.GetUser(DiscordID);

                                trackString = ArtistName + " - " + AlbumName + Environment.NewLine + FeaturedUser.Username + " (" + LastFMName + ")";

                                Console.WriteLine("[FMBot] Changed to: " + trackString);
                            }
                            catch (Exception)
                            {
                            }
                        }
                        catch (Exception)
                        {
                        }
                    }
                }
                catch (Exception)
                {
                    var fileStream = new FileStream(GlobalVars.BasePath + "avatar.png", FileMode.Open);
                    var image = new Discord.Image(fileStream);
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = image);
                    fileStream.Close();
                }
            },
            null,
            TimeSpan.FromSeconds(15),  // 4) Time that message should fire after the timer is created
            TimeSpan.FromMinutes(120)); // 5) Time after which message should repeat (use `Timeout.Infinite` for no repeat)
        }

        public string GetTrackString()
        {
            return trackString;
        }
    }

    public class FMCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly TimerService _timer;

        public FMCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }

        [Command("fm")]
        public async Task fmAsync(IUser user = null)
        {
            try
            {
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                int LastFMMode = DBase.GetModeIntForID(DiscordUser.Id.ToString());
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    // first, let's load our configuration file
                    Console.WriteLine("[FMBot] Loading Configuration");
                    var json = "";
                    using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                    using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                        json = await sr.ReadToEndAsync();

                    // next, let's load the values from that file
                    // to our client's configuration
                    var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                        LastTrack currentTrack = tracks.Content.ElementAt(0);
                        LastTrack lastTrack = tracks.Content.ElementAt(1);
                        if (LastFMMode == 0)
                        {
                            EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                            eab.IconUrl = DiscordUser.GetAvatarUrl();
                            if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                            {
                                eab.Name = DiscordUser.Username;
                            }
                            else
                            {
                                eab.Name = DiscordUser.Nickname;
                            }

                            var builder = new EmbedBuilder();
                            builder.WithAuthor(eab);
                            string URI = "https://www.last.fm/user/" + LastFMName;
                            builder.WithUrl(URI);
                            bool Admin = AdminCommands.IsAdmin(DiscordUser);
                            if (Admin)
                            {
                                builder.WithTitle(LastFMName + ", FMBot Admin");
                            }
                            else
                            {
                                builder.WithTitle(LastFMName);
                            }
                            builder.WithDescription("Recently Played");

                            string nulltext = "[undefined]";

                            string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                            try
                            {
                                var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                {
                                    builder.WithThumbnailUrl(ThumbnailImage);
                                }
                            }
                            catch (Exception)
                            {
                            }

                            //builder.AddInlineField("Recent Track", TrackName);
                            //builder.AddInlineField(AlbumName, ArtistName);

                            builder.AddField("Recent Track: " + TrackName, ArtistName + " | " + AlbumName);

                            EmbedFooterBuilder efb = new EmbedFooterBuilder();

                            var userinfo = await client.User.GetInfoAsync(LastFMName);
                            var playcount = userinfo.Content.Playcount;

                            efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                            builder.WithFooter(efb);

                            await Context.Channel.SendMessageAsync("", false, builder.Build());
                        }
                        else if (LastFMMode == 1)
                        {
                            try
                            {
                                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                                eab.IconUrl = DiscordUser.GetAvatarUrl();
                                if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                                {
                                    eab.Name = DiscordUser.Username;
                                }
                                else
                                {
                                    eab.Name = DiscordUser.Nickname;
                                }

                                var builder = new EmbedBuilder();
                                builder.WithAuthor(eab);
                                string URI = "https://www.last.fm/user/" + LastFMName;
                                builder.WithUrl(URI);
                                bool Admin = AdminCommands.IsAdmin(DiscordUser);
                                if (Admin)
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Admin");
                                }
                                else
                                {
                                    builder.WithTitle(LastFMName);
                                }
                                builder.WithDescription("Recently Played");

                                string nulltext = "[undefined]";

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                                string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                                string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                                try
                                {
                                    var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                                catch (Exception)
                                {
                                }

                                //builder.AddInlineField("Recent Track", TrackName);
                                //builder.AddInlineField(AlbumName, ArtistName);
                                //builder.AddInlineField("Previous Track", LastTrackName);
                                //builder.AddInlineField(LastAlbumName, LastArtistName);

                                builder.AddField("Recent Track: " + TrackName, ArtistName + " | " + AlbumName);
                                builder.AddField("Previous Track: " + LastTrackName, LastArtistName + " | " + LastAlbumName);

                                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                                var userinfo = await client.User.GetInfoAsync(LastFMName);
                                var playcount = userinfo.Content.Playcount;

                                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                                builder.WithFooter(efb);

                                await Context.Channel.SendMessageAsync("", false, builder.Build());
                            }
                            catch (Exception)
                            {
                                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                                eab.IconUrl = DiscordUser.GetAvatarUrl();
                                if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                                {
                                    eab.Name = DiscordUser.Username;
                                }
                                else
                                {
                                    eab.Name = DiscordUser.Nickname;
                                }

                                var builder = new EmbedBuilder();
                                builder.WithAuthor(eab);
                                string URI = "https://www.last.fm/user/" + LastFMName;
                                builder.WithUrl(URI);
                                bool Admin = AdminCommands.IsAdmin(DiscordUser);
                                if (Admin)
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Admin");
                                }
                                else
                                {
                                    builder.WithTitle(LastFMName);
                                }
                                builder.WithDescription("Recently Played");

                                string nulltext = "[undefined]";

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                try
                                {
                                    var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                                catch (Exception)
                                {
                                }

                                //builder.AddInlineField("Recent Track", TrackName);
                                //builder.AddInlineField(AlbumName, ArtistName);

                                builder.AddField("Recent Track: " + TrackName, ArtistName + " | " + AlbumName);

                                EmbedFooterBuilder efb = new EmbedFooterBuilder();

                                var userinfo = await client.User.GetInfoAsync(LastFMName);
                                var playcount = userinfo.Content.Playcount;

                                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                                builder.WithFooter(efb);

                                await Context.Channel.SendMessageAsync("", false, builder.Build());
                            }
                        }
                        else if (LastFMMode == 2)
                        {
                            try
                            {
                                string nulltext = "[undefined]";

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                                string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                                string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                                var userinfo = await client.User.GetInfoAsync(LastFMName);
                                var playcount = userinfo.Content.Playcount;

                                bool Admin = AdminCommands.IsAdmin(DiscordUser);
                                if (Admin)
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else
                                {
                                    await Context.Channel.SendMessageAsync("**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                            }
                            catch (Exception)
                            {
                                string nulltext = "[undefined]";

                                string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                                string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                                string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                                string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                                string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                                var userinfo = await client.User.GetInfoAsync(LastFMName);
                                var playcount = userinfo.Content.Playcount;

                                bool Admin = AdminCommands.IsAdmin(DiscordUser);
                                if (Admin)
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else
                                {
                                    await Context.Channel.SendMessageAsync("**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                            }
                        }
                        else if (LastFMMode == 3)
                        {
                            string nulltext = "[undefined]";

                            string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? nulltext : currentTrack.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? nulltext : currentTrack.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? nulltext : currentTrack.AlbumName;

                            string LastTrackName = string.IsNullOrWhiteSpace(lastTrack.Name) ? nulltext : lastTrack.Name;
                            string LastArtistName = string.IsNullOrWhiteSpace(lastTrack.ArtistName) ? nulltext : lastTrack.ArtistName;
                            string LastAlbumName = string.IsNullOrWhiteSpace(lastTrack.AlbumName) ? nulltext : lastTrack.AlbumName;

                            var userinfo = await client.User.GetInfoAsync(LastFMName);
                            var playcount = userinfo.Content.Playcount;

                            bool Admin = AdminCommands.IsAdmin(DiscordUser);
                            if (Admin)
                            {
                                await Context.Channel.SendMessageAsync("FMBot Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                            }
                            else
                            {
                                await Context.Channel.SendMessageAsync("**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                            }
                        }
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("Your have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using .fm again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmyt")]
        [Alias("fmyoutube")]
        public async Task fmytAsync(IUser user = null)
        {
            var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                try
                {
                    var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                    LastTrack currentTrack = tracks.Content.ElementAt(0);

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                    try
                    {
                        string querystring = TrackName + " - " + ArtistName + " " + AlbumName;
                        var items = new VideoSearch();
                        var item = items.SearchQuery(querystring, 1).ElementAt(0);

                        await Context.Channel.SendMessageAsync(item.Url);
                    }
                    catch (Exception)
                    {
                    }

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname;
                    }

                    var builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    bool Admin = AdminCommands.IsAdmin(DiscordUser);
                    if (Admin)
                    {
                        builder.WithTitle(LastFMName + ", FMBot Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }
                    builder.WithDescription("Recently Played on YouTube");

                    string nulltext = "[undefined]";

                    string TrackName2 = string.IsNullOrWhiteSpace(TrackName) ? nulltext : TrackName;
                    string ArtistName2 = string.IsNullOrWhiteSpace(ArtistName) ? nulltext : ArtistName;
                    string AlbumName2 = string.IsNullOrWhiteSpace(AlbumName) ? nulltext : AlbumName;

                    builder.AddField(TrackName2, ArtistName2 + " | " + AlbumName2);

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    var userinfo = await client.User.GetInfoAsync(LastFMName);
                    var playcount = userinfo.Content.Playcount;

                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
                catch (Exception)
                {
                    await ReplyAsync("Your have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using .fmyt again!");
                }
            }
        }

        [Command("fmrecent")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(IUser user = null)
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                int num = int.Parse(cfgjson.Listnum);

                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                        {
                            eab.Name = DiscordUser.Username;
                        }
                        else
                        {
                            eab.Name = DiscordUser.Nickname;
                        }

                        var builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
                        bool Admin = AdminCommands.IsAdmin(DiscordUser);
                        if (Admin)
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }
                        builder.WithDescription("Top " + num + " Recent Track List");

                        string nulltext = "[undefined]";
                        int indexval = (num - 1);
                        for (int i = 0; i <= indexval; i++)
                        {
                            LastTrack track = tracks.Content.ElementAt(i);

                            string TrackName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(track.AlbumName) ? nulltext : track.AlbumName;

                            try
                            {
                                if (i == 0)
                                {
                                    var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Track #" + correctnum.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        var userinfo = await client.User.GetInfoAsync(LastFMName);
                        var playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("Your have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using .fmrecent again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmchart")]
        public async Task fmchartAsync(string time = "weekly", IUser user = null)
        {
            var loadingText = "Loading your FMBot chart...";
            var loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    int max = int.Parse(cfgjson.ChartAlbums);
                    int rows = int.Parse(cfgjson.ChartRows);

                    List<Bitmap> images = new List<Bitmap>();

                    try
                    {
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Week;

                        if (time.Equals("weekly"))
                        {
                            timespan = LastStatsTimeSpan.Week;
                        }
                        else if (time.Equals("monthly"))
                        {
                            timespan = LastStatsTimeSpan.Month;
                        }
                        else if (time.Equals("yearly"))
                        {
                            timespan = LastStatsTimeSpan.Year;
                        }
                        else if (time.Equals("overall"))
                        {
                            timespan = LastStatsTimeSpan.Overall;
                        }

                        var tracks = await client.User.GetTopAlbums(LastFMName, timespan, 1, max);

                        string nulltext = "[undefined]";
                        for (int al = 0; al < max; ++al)
                        {
                            LastAlbum track = tracks.Content.ElementAt(al);

                            string ArtistName = string.IsNullOrWhiteSpace(track.ArtistName) ? nulltext : track.ArtistName;
                            string AlbumName = string.IsNullOrWhiteSpace(track.Name) ? nulltext : track.Name;

                            try
                            {
                                var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                WebRequest request = WebRequest.Create(ThumbnailImage);
                                WebResponse response = request.GetResponse();
                                Stream responseStream = response.GetResponseStream();
                                Bitmap cover = new Bitmap(responseStream);
                                images.Add(cover);
                            }
                            catch (Exception)
                            {
                                Bitmap cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                                images.Add(cover);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        List<List<Bitmap>> ImageLists = GlobalVars.splitBitmapList(images, rows);

                        List<Bitmap> BitmapList = new List<Bitmap>();

                        foreach (List<Bitmap> list in ImageLists.ToArray())
                        {
                            //combine them into one image
                            Bitmap stitchedRow = GlobalVars.Combine(list);
                            BitmapList.Add(stitchedRow);
                        }

                        Bitmap stitchedImage = GlobalVars.Combine(BitmapList, true);

                        stitchedImage.Save(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                        await Context.Channel.SendFileAsync(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.jpg");
                    }

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname;
                    }

                    var builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    bool Admin = AdminCommands.IsAdmin(DiscordUser);
                    if (Admin)
                    {
                        builder.WithTitle(LastFMName + ", FMBot Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }

                    if (time.Equals("weekly"))
                    {
                        builder.WithDescription("Last.FM Weekly Chart for " + LastFMName);
                    }
                    else if (time.Equals("monthly"))
                    {
                        builder.WithDescription("Last.FM Monthly Chart for " + LastFMName);
                    }
                    else if (time.Equals("yearly"))
                    {
                        builder.WithDescription("Last.FM Yearly Chart for " + LastFMName);
                    }
                    else if (time.Equals("overall"))
                    {
                        builder.WithDescription("Last.FM Overall Chart for " + LastFMName);
                    }

                    var userinfo = await client.User.GetInfoAsync(LastFMName);
                    EmbedFooterBuilder efb = new EmbedFooterBuilder();
                    var playcount = userinfo.Content.Playcount;
                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Error: Cannot generate chart. You may not have scrobbled anything this time period or your Last.FM name cannot be found.");
            }

            await loadingmsg.DeleteAsync();
        }

        [Command("fmartistchart")]
        public async Task fmartistchartAsync(string time = "weekly", IUser user = null)
        {
            var loadingText = "Loading your FMBot artist chart...";
            var loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    int max = int.Parse(cfgjson.ChartAlbums);
                    int rows = int.Parse(cfgjson.ChartRows);

                    List<Bitmap> images = new List<Bitmap>();

                    try
                    {
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Week;

                        if (time.Equals("weekly"))
                        {
                            timespan = LastStatsTimeSpan.Week;
                        }
                        else if (time.Equals("monthly"))
                        {
                            timespan = LastStatsTimeSpan.Month;
                        }
                        else if (time.Equals("yearly"))
                        {
                            timespan = LastStatsTimeSpan.Year;
                        }
                        else if (time.Equals("overall"))
                        {
                            timespan = LastStatsTimeSpan.Overall;
                        }

                        var artists = await client.User.GetTopArtists(LastFMName, timespan, 1, max);

                        string nulltext = "[undefined]";
                        for (int al = 0; al < max; ++al)
                        {
                            LastArtist artist = artists.Content.ElementAt(al);

                            string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                            try
                            {
                                var ArtistInfo = await client.Artist.GetInfoAsync(ArtistName);
                                var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                WebRequest request = WebRequest.Create(ThumbnailImage);
                                WebResponse response = request.GetResponse();
                                Stream responseStream = response.GetResponseStream();
                                Bitmap cover = new Bitmap(responseStream);
                                images.Add(cover);
                            }
                            catch (Exception)
                            {
                                Bitmap cover = new Bitmap(GlobalVars.BasePath + "unknown.png");
                                images.Add(cover);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                    finally
                    {
                        List<List<Bitmap>> ImageLists = GlobalVars.splitBitmapList(images, rows);

                        List<Bitmap> BitmapList = new List<Bitmap>();

                        foreach (List<Bitmap> list in ImageLists.ToArray())
                        {
                            //combine them into one image
                            Bitmap stitchedRow = GlobalVars.Combine(list);
                            BitmapList.Add(stitchedRow);
                        }

                        Bitmap stitchedImage = GlobalVars.Combine(BitmapList, true);

                        stitchedImage.Save(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
                        await Context.Channel.SendFileAsync(GlobalVars.UsersFolder + DiscordUser.Id + "-chart.jpg");
                    }

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname;
                    }

                    var builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    bool Admin = AdminCommands.IsAdmin(DiscordUser);
                    if (Admin)
                    {
                        builder.WithTitle(LastFMName + ", FMBot Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }

                    if (time.Equals("weekly"))
                    {
                        builder.WithDescription("Last.FM Weekly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("monthly"))
                    {
                        builder.WithDescription("Last.FM Monthly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("yearly"))
                    {
                        builder.WithDescription("Last.FM Yearly Artist Chart for " + LastFMName);
                    }
                    else if (time.Equals("overall"))
                    {
                        builder.WithDescription("Last.FM Overall Artist Chart for " + LastFMName);
                    }

                    var userinfo = await client.User.GetInfoAsync(LastFMName);
                    EmbedFooterBuilder efb = new EmbedFooterBuilder();
                    var playcount = userinfo.Content.Playcount;
                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Error: Cannot generate chart. You may not have scrobbled anything this time period or your Last.FM name cannot be found.");
            }

            await loadingmsg.DeleteAsync();
        }

        [Command("fmfriends")]
        [Alias("fmrecentfriends", "fmfriendsrecent")]
        public async Task fmfriendsrecentAsync(IUser user = null)
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string[] LastFMFriends = DBase.GetFriendsForID(DiscordUser.Id.ToString());
                if (LastFMFriends == null || !LastFMFriends.Any())
                {
                    await ReplyAsync("Your LastFM friends were unable to be found. Please use fmsetfriends.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        bool Admin = AdminCommands.IsAdmin(DiscordUser);

                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        eab.Name = DiscordUser.Username;

                        var builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        if (Admin)
                        {
                            eab.Name = DiscordUser.Username + ", FMBot Admin";
                        }
                        else
                        {
                            eab.Name = DiscordUser.Username;
                        }

                        var amountOfScrobbles = "Amount of scrobbles of all your friends together: ";

                        if (LastFMFriends.Count() > 1)
                        {
                            builder.WithDescription("Songs from " + LastFMFriends.Count() + " friends");
                        }
                        else
                        {
                            builder.WithDescription("Songs from your friend");
                            amountOfScrobbles = "Amount of scrobbles from your friend: ";
                        }

                        string nulltext = "[undefined]";
                        int indexval = (LastFMFriends.Count() - 1);
                        int playcount = 0;

                        try
                        {
                            foreach (var friend in LastFMFriends)
                            {
                                var tracks = await client.User.GetRecentScrobbles(friend, null, 1, 1);

                                string TrackName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().Name) ? nulltext : tracks.FirstOrDefault().Name;
                                string ArtistName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().ArtistName) ? nulltext : tracks.FirstOrDefault().ArtistName;
                                string AlbumName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().AlbumName) ? nulltext : tracks.FirstOrDefault().AlbumName;

                                builder.AddField(friend.ToString() + ":", TrackName + " - " + ArtistName + " | " + AlbumName);

                                // count how many scrobbles everyone has together (if the bot is too slow, consider removing this?)
                                var userinfo = await client.User.GetInfoAsync(friend);
                                playcount = playcount + userinfo.Content.Playcount;
                            }
                        }
                        catch (Exception)
                        {

                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        efb.Text = amountOfScrobbles + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("Your friends have no scrobbles on their Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using fmrecent again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your friends could not be found. Please set your friends using fmsetfriends.");
            }
        }

        [Command("fmartists")]
        public async Task fmartistsAsync(IUser user = null)
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                int num = int.Parse(cfgjson.Listnum);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        var artists = await client.User.GetTopArtists(LastFMName, LastStatsTimeSpan.Overall, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                        {
                            eab.Name = DiscordUser.Username;
                        }
                        else
                        {
                            eab.Name = DiscordUser.Nickname;
                        }

                        var builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
                        bool Admin = AdminCommands.IsAdmin(DiscordUser);
                        if (Admin)
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }
                        builder.WithDescription("Top " + num + " Artist List");

                        string nulltext = "[undefined]";
                        int indexval = (num - 1);
                        for (int i = 0; i <= indexval; i++)
                        {
                            LastArtist artist = artists.Content.ElementAt(i);

                            string ArtistName = string.IsNullOrWhiteSpace(artist.Name) ? nulltext : artist.Name;

                            try
                            {
                                if (i == 0)
                                {
                                    var ArtistInfo = await client.Artist.GetInfoAsync(ArtistName);
                                    var ArtistImages = (ArtistInfo.Content.MainImage != null) ? ArtistInfo.Content.MainImage : null;
                                    var ArtistThumbnail = (ArtistImages != null) ? ArtistImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (ArtistThumbnail != null) ? ArtistThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Artist #" + correctnum.ToString() + ":", ArtistName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        var userinfo = await client.User.GetInfoAsync(LastFMName);
                        var playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("Your have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using .fmartists again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmalbums")]
        public async Task fmalbumsAsync(IUser user = null)
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                int num = int.Parse(cfgjson.Listnum);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    try
                    {
                        var albums = await client.User.GetTopAlbums(LastFMName, LastStatsTimeSpan.Overall, 1, num);

                        EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                        eab.IconUrl = DiscordUser.GetAvatarUrl();
                        if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                        {
                            eab.Name = DiscordUser.Username;
                        }
                        else
                        {
                            eab.Name = DiscordUser.Nickname;
                        }

                        var builder = new EmbedBuilder();
                        builder.WithAuthor(eab);
                        string URI = "https://www.last.fm/user/" + LastFMName;
                        builder.WithUrl(URI);
                        bool Admin = AdminCommands.IsAdmin(DiscordUser);
                        if (Admin)
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }
                        builder.WithDescription("Top " + num + " Album List");

                        string nulltext = "[undefined]";
                        int indexval = (num - 1);
                        for (int i = 0; i <= indexval; i++)
                        {
                            LastAlbum album = albums.Content.ElementAt(i);

                            string AlbumName = string.IsNullOrWhiteSpace(album.Name) ? nulltext : album.Name;
                            string ArtistName = string.IsNullOrWhiteSpace(album.ArtistName) ? nulltext : album.ArtistName;

                            try
                            {
                                if (i == 0)
                                {
                                    var AlbumInfo = await client.Album.GetInfoAsync(ArtistName, AlbumName);
                                    var AlbumImages = (AlbumInfo.Content.Images != null) ? AlbumInfo.Content.Images : null;
                                    var AlbumThumbnail = (AlbumImages != null) ? AlbumImages.Large.AbsoluteUri : null;
                                    string ThumbnailImage = (AlbumThumbnail != null) ? AlbumThumbnail.ToString() : null;

                                    if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                                    {
                                        builder.WithThumbnailUrl(ThumbnailImage);
                                    }
                                }
                            }
                            catch (Exception)
                            {
                            }

                            int correctnum = (i + 1);
                            builder.AddField("Album #" + correctnum.ToString() + ":", AlbumName + " | " + ArtistName);
                        }

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        var userinfo = await client.User.GetInfoAsync(LastFMName);
                        var playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("Your have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then try using .fmalbums again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmstats")]
        [Alias("fminfo")]
        public async Task fmstatsAsync(IUser user = null)
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname;
                    }

                    var builder = new EmbedBuilder();
                    builder.WithAuthor(eab);
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    bool Admin = AdminCommands.IsAdmin(DiscordUser);
                    if (Admin)
                    {
                        builder.WithTitle(LastFMName + ", FMBot Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }
                    builder.WithDescription("Last.FM Statistics for " + LastFMName);

                    var userinfo = await client.User.GetInfoAsync(LastFMName);

                    try
                    {
                        var userinfoImages = (userinfo.Content.Avatar != null) ? userinfo.Content.Avatar : null;
                        var userinfoThumbnail = (userinfoImages != null) ? userinfoImages.Large.AbsoluteUri : null;
                        string ThumbnailImage = (userinfoThumbnail != null) ? userinfoThumbnail.ToString() : null;

                        if (!string.IsNullOrWhiteSpace(ThumbnailImage))
                        {
                            builder.WithThumbnailUrl(ThumbnailImage);
                        }
                    }
                    catch (Exception)
                    {
                    }

                    var playcount = userinfo.Content.Playcount;
                    var usertype = userinfo.Content.Type;
                    var playlists = userinfo.Content.Playlists;
                    var premium = userinfo.Content.IsSubscriber;

                    string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(DiscordUser.Id.ToString()));

                    builder.AddInlineField("Last.FM Name: ", LastFMName);
                    builder.AddInlineField("FMBot Mode: ", LastFMMode);
                    builder.AddInlineField("User Type: ", usertype.ToString());
                    builder.AddInlineField("Total Tracks: ", playcount.ToString());
                    builder.AddInlineField("Total Playlists: ", playlists.ToString());
                    builder.AddInlineField("Has Premium? ", premium.ToString());
                    builder.AddInlineField("Is FMBot Admin? ", Admin.ToString());

                    EmbedFooterBuilder efb = new EmbedFooterBuilder();

                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmfeatured")]
        [Alias("fmavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task fmfeaturedAsync()
        {
            try
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var DiscordUser = (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                    EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                    eab.IconUrl = DiscordUser.GetAvatarUrl();
                    if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                    {
                        eab.Name = DiscordUser.Username;
                    }
                    else
                    {
                        eab.Name = DiscordUser.Nickname;
                    }

                    var builder = new EmbedBuilder();
                    string URI = "https://www.last.fm/user/" + LastFMName;
                    builder.WithUrl(URI);
                    bool Admin = AdminCommands.IsAdmin(DiscordUser);
                    if (Admin)
                    {
                        builder.WithTitle(LastFMName + ", FMBot Admin");
                    }
                    else
                    {
                        builder.WithTitle(LastFMName);
                    }
                    builder.WithDescription("FMBot Featured Album");

                    var SelfUser = Context.Client.CurrentUser;
                    builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                    builder.AddInlineField("Featured Album:", _timer.GetTrackString());

                    var userinfo = await client.User.GetInfoAsync(LastFMName);
                    var playcount = userinfo.Content.Playcount;
                    EmbedFooterBuilder efb = new EmbedFooterBuilder();
                    efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                    builder.WithFooter(efb);

                    await Context.Channel.SendMessageAsync("", false, builder.Build());
                }
            }
            catch (Exception)
            {
                await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
            }
        }

        [Command("fmset"), Summary("Sets your Last.FM name.")]
        [Alias("fmsetname")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string name, [Summary("The mode you want to use.")] string mode = "embedmini")
        {
            string SelfID = Context.Message.Author.Id.ToString();
            int modeint = DBase.GetIntForModeName(mode);
            if (modeint == 4)
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }
            DBase.WriteEntry(SelfID, name, modeint);
            string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(SelfID));
            await ReplyAsync("Your Last.FM name has been set to '" + name + "' and your FMBot mode has been set to '" + LastFMMode + "'.");
        }

        [Command("fmsetfriends"), Summary("Sets your friends' Last.FM names.")]
        [Alias("fmfriendsset")]
        public async Task fmfriendssetAsync([Summary("Friend names")] params string[] friends)
        {
            string SelfID = Context.Message.Author.Id.ToString();
            DBase.WriteFriendsEntry(SelfID, friends);

            if (friends.Count() > 1)
            {
                await ReplyAsync("Succesfully set " + friends.Count() + " friends.");
            }
            else
            {
                await ReplyAsync("Succesfully set a friend.");
            }
        }

        [Command("fmaddfriends"), Summary("Adds your friends' Last.FM names.")]
        [Alias("fmfriendsadd")]
        public async Task fmfriendsaddAsync([Summary("Friend names")] params string[] friends)
        {
            string SelfID = Context.Message.Author.Id.ToString();
            DBase.AddFriendsEntry(SelfID, friends);

            if (friends.Count() > 1)
            {
                await ReplyAsync("Succesfully added " + friends.Count() + " friends.");
            }
            else
            {
                await ReplyAsync("Succesfully added a friend.");
            }
        }

        [Command("fmremove"), Summary("Deletes your FMBot data.")]
        [Alias("fmdelete")]
        public async Task fmremoveAsync()
        {
            string SelfID = Context.Message.Author.Id.ToString();
            DBase.RemoveEntry(SelfID);
            await ReplyAsync("Your FMBot settings have been successfully deleted.");
        }

        [Command("fmunsetfriends"), Summary("Deletes your FMBot friend data.")]
        [Alias("fmremovefriends", "fmfmdeletefriends")]
        public async Task fmunsetfriendsAsync()
        {
            string SelfID = Context.Message.Author.Id.ToString();
            DBase.RemoveFriends(SelfID);
            await ReplyAsync("Your FMBot friend settings have been successfully deleted.");
        }

        [Command("fmhelp")]
        public async Task fmhelpAsync()
        {
            // first, let's load our configuration file
            Console.WriteLine("[FMBot] Loading Configuration");
            var json = "";
            using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            // next, let's load the values from that file
            // to our client's configuration
            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
            var SelfName = Context.Client.CurrentUser;
            string prefix = cfgjson.CommandPrefix;

            var DiscordUser = Context.Message.Author;

            EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
            eab.IconUrl = SelfName.GetAvatarUrl();
            eab.Name = SelfName.Username;

            var builder = new EmbedBuilder();
            builder.WithAuthor(eab);

            foreach (var module in _service.Modules)
            {
                if (module.Name.Equals("AdminCommands"))
                {
                    continue;
                }

                string description = null;
                foreach (var cmd in module.Commands)
                {
                    var result = await cmd.CheckPreconditionsAsync(Context);
                    if (result.IsSuccess)
                        description += $"{prefix}{cmd.Aliases.First()}\n";
                }

                if (!string.IsNullOrWhiteSpace(description))
                {
                    builder.AddField(x =>
                    {
                        x.Name = "FMBot Commands";
                        x.Value = description;
                        x.IsInline = false;
                    });
                }
            }

            builder.AddField("FMBot Modes for the fmset command:", "embedmini\nembedfull\ntextfull\ntextmini");

            builder.AddField("FMBot Time Periods for the fmchart and fmartistchart commands:", "weekly\nmonthly\nyearly\noverall");

            await ReplyAsync("", false, builder.Build());
        }

        [Command("fminvite"), Summary("Invites the bot to a server")]
        public async Task inviteAsync()
        {
            string SelfID = Context.Client.CurrentUser.Id.ToString();
            await ReplyAsync("https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=0");
        }

        [Command("fmdonate"), Summary("Please donate if you like this bot!")]
        public async Task donateAsync()
        {
            await ReplyAsync("Even though this bot is running for free, this bot needs funds in order to run for more than a year. If you like the bot and you would like to support its development, please donate to me at: https://www.paypal.me/Bitl");
        }

        [Command("fmgithub"), Summary("GitHub Page")]
        public async Task githubAsync()
        {
            await ReplyAsync("https://github.com/Bitl/FMBot_Discord");
        }

        [Command("fmbugs"), Summary("Report bugs here!")]
        public async Task bugsAsync()
        {
            await ReplyAsync("Report bugs here: https://github.com/Bitl/FMBot_Discord/issues");
        }
    }

    public class AdminCommands : ModuleBase
    {
        //OwnerIDs = Bitl, Tytygigas, Opus v84, [opti], GreystarMusic, Lemonadeaholic, LantaarnAppel

        private readonly CommandService _service;

        public AdminCommands(CommandService service)
        {
            _service = service;
        }

        public static bool IsAdmin(IUser user)
        {
            if (user.Id.Equals(184013824850919425) || user.Id.Equals(183730395836186624) || user.Id.Equals(205759116889554946) || user.Id.Equals(120954630388580355) || user.Id.Equals(205832344744099840) || user.Id.Equals(175357072035151872) || user.Id.Equals(125740103539621888))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool IsOwner(IUser user)
        {
            if (user.Id.Equals(184013824850919425))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        [Command("announce"), Summary("Sends an announcement to the main server.")]
        [Alias("fmannounce", "fmnews", "news", "fmannouncement", "announcement")]
        public async Task announceAsync(string message, string ThumbnailURL = null)
        {
            var DiscordUser = (IGuildUser)Context.Message.Author;
            var SelfUser = Context.Client.CurrentUser;
            ulong BroadcastChannelID = 369654929293574165;
            ITextChannel channel = await Context.Guild.GetTextChannelAsync(BroadcastChannelID);
            if (IsAdmin(DiscordUser))
            {
                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = DiscordUser.GetAvatarUrl();
                if (string.IsNullOrWhiteSpace(DiscordUser.Nickname))
                {
                    eab.Name = DiscordUser.Username;
                }
                else
                {
                    eab.Name = DiscordUser.Nickname + " (" + DiscordUser.Username + ")";
                }

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);

                try
                {
                    if (!string.IsNullOrWhiteSpace(ThumbnailURL))
                    {
                        builder.WithThumbnailUrl(ThumbnailURL);
                    }
                    else
                    {
                        builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                    }
                }
                catch (Exception)
                {
                }

                builder.AddField("Announcement", message);

                await channel.SendMessageAsync("", false, builder.Build());
            }
        }

        [Command("dbcheck"), Summary("Checks if an entry is in the database.")]
        public async Task dbcheckAsync(IUser user = null)
        {
            var DiscordUser = Context.Message.Author;
            if (IsAdmin(DiscordUser))
            {
                var ChosenUser = user ?? Context.Message.Author;
                string LastFMName = DBase.GetNameForID(ChosenUser.Id.ToString());
                string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(ChosenUser.Id.ToString()));
                if (!LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("The user's Last.FM name is '" + LastFMName + "'. Their mode is set to '" + LastFMMode + "'.");
                }
                else
                {
                    await ReplyAsync("The user's Last.FM name has not been set.");
                }
            }
        }

        [Command("fmserverreboot")]
        [Alias("fmreboot")]
        public async Task fmserverrebootAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (IsOwner(DiscordUser))
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                string Data = "SUBID=" + cfgjson.VultrSubID;
                string Reponse = "";
                StreamWriter Sw = null;
                StreamReader Sr = null;
                try
                {
                    await ReplyAsync("Rebooting server...");
                    HttpWebRequest Req = (HttpWebRequest)WebRequest.Create("https://api.vultr.com/v1/server/reboot");
                    Req.Method = "POST";
                    Req.ContentType = "application/x-www-form-urlencoded";
                    Req.Headers.Add("API-Key: " + cfgjson.VultrKey);
                    using (var sw = new StreamWriter(Req.GetRequestStream()))
                    {
                        sw.Write(Data);
                    }
                    Sr = new
                    StreamReader(((HttpWebResponse)Req.GetResponse()).GetResponseStream());
                    Reponse = Sr.ReadToEnd();
                    Sr.Close();
                }
                catch (Exception ex)
                {
                    if (Sw != null)
                        Sw.Close();
                    if (Sr != null)
                        Sr.Close();

                    await ReplyAsync("Error rebooting server. Look in bot console.");
                    Console.WriteLine("[FMBot] [VULTR API] Error: " + ex.Message);
                }
            }
        }

        [Command("fmadmin")]
        public async Task AdminAsync()
        {
            var DiscordUser = Context.Message.Author;
            if (IsAdmin(DiscordUser))
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(GlobalVars.ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var SelfName = Context.Client.CurrentUser;
                string prefix = cfgjson.CommandPrefix;

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = SelfName.GetAvatarUrl();
                eab.Name = SelfName.Username;

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);

                foreach (var module in _service.Modules)
                {
                    if (module.Name.Equals("FMCommands"))
                    {
                        continue;
                    }

                    string description = null;
                    foreach (var cmd in module.Commands)
                    {
                        var result = await cmd.CheckPreconditionsAsync(Context);
                        if (result.IsSuccess)
                            description += $"{prefix}{cmd.Aliases.First()}\n";
                    }

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        builder.AddField(x =>
                        {
                            x.Name = "Admin Commands";
                            x.Value = description;
                            x.IsInline = false;
                        });
                    }
                }

                await ReplyAsync("", false, builder.Build());
            }
        }
    }

    public class DBase
    {
        public static void WriteEntry(string id, string name, int fmval = 0)
        {
            File.WriteAllText(GlobalVars.UsersFolder + id + ".txt", name + Environment.NewLine + fmval.ToString());
            File.SetAttributes(GlobalVars.UsersFolder + id + ".txt", FileAttributes.Normal);
        }

        public static void WriteFriendsEntry(string id, params string[] stringArray)
        {
            string text = "";
            foreach (var friend in stringArray)
            {
                text += friend;
                text += Environment.NewLine;
            }
            File.WriteAllText(GlobalVars.UsersFolder + id + "-friends.txt", text);
            File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
        }

        public static void AddFriendsEntry(string id, params string[] stringArray)
        {
            string text = "";
            foreach (var friend in stringArray)
            {
                text += friend;
                text += Environment.NewLine;
            }
            File.AppendAllText(GlobalVars.UsersFolder + id + "-friends.txt", text);
            File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
        }

        public static void RemoveEntry(string id)
        {
            if (File.Exists(GlobalVars.UsersFolder + id + ".txt"))
            {
                File.SetAttributes(GlobalVars.UsersFolder + id + ".txt", FileAttributes.Normal);
                File.Delete(GlobalVars.UsersFolder + id + ".txt");
            }
            if (File.Exists(GlobalVars.UsersFolder + id + "-friends.txt"))
            {
                File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
                File.Delete(GlobalVars.UsersFolder + id + "-friends.txt");
            }
            if (File.Exists(GlobalVars.UsersFolder + id + "-chart.jpg"))
            {
                File.SetAttributes(GlobalVars.UsersFolder + id + "-chart.jpg", FileAttributes.Normal);
                File.Delete(GlobalVars.UsersFolder + id + "-chart.jpg");
            }
        }

        public static void RemoveFriends(string id)
        {
            if (File.Exists(GlobalVars.UsersFolder + id + "-friends.txt"))
            {
                File.SetAttributes(GlobalVars.UsersFolder + id + "-friends.txt", FileAttributes.Normal);
                File.Delete(GlobalVars.UsersFolder + id + "-friends.txt");
            }
        }

        public static string GetNameForID(string id)
        {
            string line;

            using (StreamReader file = new StreamReader(GlobalVars.UsersFolder + id + ".txt"))
            {
                while ((line = file.ReadLine()) != null)
                {
                    file.Close();
                    return line;
                }
            }

            return "NULL";
        }

        public static ulong GetIDForName(string name)
        {
            foreach (string file in Directory.GetFiles(GlobalVars.UsersFolder, "*.txt"))
            {
                if (File.ReadAllText(file).Contains(name))
                {
                    string nameConvert = Path.GetFileNameWithoutExtension(file);
                    ulong DiscordID = Convert.ToUInt64(nameConvert);
                    return DiscordID;
                }
            }

            return 0;
        }

        public static string GetRandFMName()
        {
            Random rand = new Random();
            List<string> files = Directory.GetFiles(GlobalVars.UsersFolder).Where(F => F.ToLower().EndsWith(".txt")).ToList();
            string randomFile = files[rand.Next(0, files.Count)];

            string line;

            using (StreamReader file = new StreamReader(randomFile))
            {
                while ((line = file.ReadLine()) != null)
                {
                    file.Close();
                    return line;
                }
            }

            return "NULL";
        }

        public static string[] GetFriendsForID(string id)
        {
            string[] lines = File.ReadAllLines(GlobalVars.UsersFolder + id + "-friends.txt");
            return lines;
        }

        public static int GetModeIntForID(string id)
        {
            string line;

            using (StreamReader file = new StreamReader(GlobalVars.UsersFolder + id + ".txt"))
            {
                while ((line = file.ReadLine()) != null)
                {
                    string nextline = file.ReadLine();
                    file.Close();
                    return Convert.ToInt32(nextline);
                }
            }

            return 4;
        }

        public static int GetIntForModeName(string mode)
        {
            if (mode.Equals("embedmini"))
            {
                return 0;
            }
            else if (mode.Equals("embedfull"))
            {
                return 1;
            }
            else if (mode.Equals("textfull"))
            {
                return 2;
            }
            else if (mode.Equals("textmini"))
            {
                return 3;
            }
            else
            {
                return 4;
            }
        }

        public static string GetNameForModeInt(int mode)
        {
            if (mode == 0)
            {
                return "embedmini";
            }
            else if (mode == 1)
            {
                return "embedfull";
            }
            else if (mode == 2)
            {
                return "textfull";
            }
            else if (mode == 3)
            {
                return "textmini";
            }
            else
            {
                return "NULL";
            }
        }
    }

    // this structure will hold data from config.json
    public struct ConfigJson
    {
        [JsonProperty("token")]
        public string Token { get; private set; }

        [JsonProperty("fmkey")]
        public string FMKey { get; private set; }

        [JsonProperty("fmsecret")]
        public string FMSecret { get; private set; }

        [JsonProperty("prefix")]
        public string CommandPrefix { get; private set; }

        [JsonProperty("listnum")]
        public string Listnum { get; private set; }

        [JsonProperty("chartalbums")]
        public string ChartAlbums { get; private set; }

        [JsonProperty("chartrows")]
        public string ChartRows { get; private set; }

        [JsonProperty("vultrkey")]
        public string VultrKey { get; private set; }

        [JsonProperty("vultrsubid")]
        public string VultrSubID { get; private set; }

        [JsonProperty("timerinittime")]
        public string TimerInitTime { get; private set; }

        [JsonProperty("timerepeattime")]
        public string TimeRepeatTime { get; private set; }
    }

    public class GlobalVars
    {
        public static string ConfigFileName = "config.json";
        public static string BasePath = AppDomain.CurrentDomain.BaseDirectory;
        public static string UsersFolder = BasePath + "users/";

        public static Bitmap Combine(List<Bitmap> images, bool vertical = false)
        {
            //read all images into memory
            Bitmap finalImage = null;

            try
            {
                int width = 0;
                int height = 0;

                foreach (Bitmap image in images.ToArray())
                {
                    //create a Bitmap from the file and add it to the list
                    Bitmap bitmap = image;

                    //update the size of the final bitmap
                    if (vertical == true)
                    {
                        width = bitmap.Width > width ? bitmap.Width : width;
                        height += bitmap.Height;
                    }
                    else
                    {
                        width += bitmap.Width;
                        height = bitmap.Height > height ? bitmap.Height : height;
                    }

                    images.Add(bitmap);
                }

                //create a bitmap to hold the combined image
                finalImage = new Bitmap(width, height);

                //get a graphics object from the image so we can draw on it
                using (Graphics g = Graphics.FromImage(finalImage))
                {
                    //set background color
                    g.Clear(System.Drawing.Color.Black);

                    //go through each image and draw it on the final image
                    int offset = 0;
                    foreach (Bitmap image in images)
                    {
                        if (vertical == true)
                        {
                            g.DrawImage(image, new Rectangle(0, offset, image.Width, image.Height));
                            offset += image.Height;
                        }
                        else
                        {
                            g.DrawImage(image, new Rectangle(offset, 0, image.Width, image.Height));
                            offset += image.Width;
                        }
                    }
                }

                return finalImage;
            }
            catch (Exception ex)
            {
                if (finalImage != null)
                    finalImage.Dispose();

                throw ex;
            }
            finally
            {
                //clean up memory
                foreach (Bitmap image in images)
                {
                    image.Dispose();
                }
            }
        }

        public static List<List<Bitmap>> splitBitmapList(List<Bitmap> locations, int nSize)
        {
            var list = new List<List<Bitmap>>();

            for (int i = 0; i < locations.Count; i += nSize)
            {
                list.Add(locations.GetRange(i, Math.Min(nSize, locations.Count - i)));
            }

            return list;
        }
    }
}
