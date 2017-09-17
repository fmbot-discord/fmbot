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

namespace FMBot_Discord
{
    class Program
    {
        private CommandService commands;
        private DiscordSocketClient client;
        private IServiceProvider services;
        private string prefix;
        private string DBFileName = "database.txt";
        private string ConfigFileName = "config.json";

        public static void Main(string[] args)
            => new Program().MainAsync().GetAwaiter().GetResult();

        public async Task MainAsync()
        {
            // first, let's load our configuration file
            Console.WriteLine("[FMBot] Loading Configuration");
            var json = "";
            using (var fs = File.OpenRead(ConfigFileName))
            using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                json = await sr.ReadToEndAsync();

            if (!File.Exists(DBFileName))
            {
                Console.WriteLine("[FMBot] Warning: DB not found. Creating empty DB file.");
                File.Create(DBFileName).Dispose();
                Console.WriteLine("[FMBot] DB File exists. Loading.");
            }
            else
            {
                Console.WriteLine("[FMBot] DB File exists. Loading.");
            }

            // next, let's load the values from that file
            // to our client's configuration
            var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);

            Console.WriteLine("[FMBot] Initalizing Discord");
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                WebSocketProvider = WS4NetProvider.Instance,
                LogLevel = LogSeverity.Verbose
            });

            client.Log += Log;

            prefix = cfgjson.CommandPrefix;

            Console.WriteLine("[FMBot] Registering Commands");
            commands = new CommandService();

            string token = cfgjson.Token; // Remember to keep this private!

            services = new ServiceCollection()
                .BuildServiceProvider();

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
            // Hook the MessageReceived Event into our Command Handler
            client.MessageReceived += HandleCommand;
            // Discover all of the commands in this assembly and load them.
            await commands.AddModulesAsync(Assembly.GetEntryAssembly());
        }

        public async Task HandleCommand(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;
            // Create a number to track where the prefix ends and the command begins
            int argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!(message.HasCharPrefix(Convert.ToChar(prefix), ref argPos) || message.HasMentionPrefix(client.CurrentUser, ref argPos))) return;
            // Create a Command Context
            var context = new CommandContext(client, message);
            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await commands.ExecuteAsync(context, argPos, services);
            if (!result.IsSuccess)
                Console.WriteLine("[FMBot]: Error - " + result.Error + ": " + result.ErrorReason);
        }
    }


    public class FMCommands : ModuleBase
    {
        private string DBFileName = "database.txt";
        private string ConfigFileName = "config.json";

        private readonly CommandService _service;

        public FMCommands(CommandService service)
        {
            _service = service;
        }

        [Command("fm")]
        public async Task fmAsync(IUser user = null)
        {
            var DiscordUser = user ?? Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DBFileName, DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                LastTrack currentTrack = tracks.Content.ElementAt(0);
                LastTrack lastTrack = tracks.Content.ElementAt(1);
                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = DiscordUser.GetAvatarUrl();
                eab.Name = DiscordUser.Username;
                eab.Url = "https://www.last.fm/user/" + LastFMName;

                var builder = new EmbedBuilder();

                builder.WithAuthor(eab);
                builder.WithDescription("Recently Played");

                string nulltext = "[unknown or corrupted]";

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

                builder.AddInlineField("Current Track", TrackName);
                builder.AddInlineField(AlbumName, ArtistName);
                builder.AddInlineField("Previous Track", LastTrackName);
                builder.AddInlineField(LastAlbumName, LastArtistName);

                EmbedFooterBuilder efb = new EmbedFooterBuilder();
                efb.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();

                var userinfo = await client.User.GetInfoAsync(LastFMName);
                var playcount = userinfo.Content.Playcount;

                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
        }

        [Command("fmrecent")]
        public async Task fmrecentAsync(IUser user = null, int num = 5)
        {
            var DiscordUser = user ?? Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DBFileName, DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, num);

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = DiscordUser.GetAvatarUrl();
                eab.Name = DiscordUser.Username;
                eab.Url = "https://www.last.fm/user/" + LastFMName;

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                builder.WithDescription("Top " + num + " Recent Tracks");

                string nulltext = "[unknown or corrupted]";
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
                efb.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();

                var userinfo = await client.User.GetInfoAsync(LastFMName);
                var playcount = userinfo.Content.Playcount;

                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
        }

        [Command("fmartists")]
        public async Task fmartistsAsync(IUser user = null, int num = 5)
        {
            var DiscordUser = user ?? Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DBFileName, DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                var artists = await client.User.GetTopArtists(LastFMName, LastStatsTimeSpan.Overall, 1, num);

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = DiscordUser.GetAvatarUrl();
                eab.Name = DiscordUser.Username;
                eab.Url = "https://www.last.fm/user/" + LastFMName;

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                builder.WithDescription("Top " + num + " Artists");

                string nulltext = "[unknown or corrupted]";
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
                efb.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();

                var userinfo = await client.User.GetInfoAsync(LastFMName);
                var playcount = userinfo.Content.Playcount;

                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
        }

        [Command("fmalbums")]
        public async Task fmalbumsAsync(IUser user = null, int num = 5)
        {
            var DiscordUser = user ?? Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DBFileName, DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                // first, let's load our configuration file
                Console.WriteLine("[FMBot] Loading Configuration");
                var json = "";
                using (var fs = File.OpenRead(ConfigFileName))
                using (var sr = new StreamReader(fs, new UTF8Encoding(false)))
                    json = await sr.ReadToEndAsync();

                // next, let's load the values from that file
                // to our client's configuration
                var cfgjson = JsonConvert.DeserializeObject<ConfigJson>(json);
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                var albums = await client.User.GetTopAlbums(LastFMName, LastStatsTimeSpan.Overall, 1, num);

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder();
                eab.IconUrl = DiscordUser.GetAvatarUrl();
                eab.Name = DiscordUser.Username;
                eab.Url = "https://www.last.fm/user/" + LastFMName;

                var builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                builder.WithDescription("Top " + num + " Albums");

                string nulltext = "[unknown or corrupted]";
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
                efb.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();

                var userinfo = await client.User.GetInfoAsync(LastFMName);
                var playcount = userinfo.Content.Playcount;

                efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                builder.WithFooter(efb);

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
        }

        [Command("fmset"), Summary("Sets your Last.FM name.")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string name)
        {
            string SelfID = Context.Message.Author.Id.ToString();
            string LastFMName = DBase.GetNameForID(DBFileName, SelfID);
            if (LastFMName.Equals("NULL"))
            {
                DBase.WriteEntry(DBFileName, SelfID, name);
                await ReplyAsync("Your Last.FM name has been set to '" + name + "'.");
            }
            else
            {
                await ReplyAsync("Your Last.FM Name is '" + LastFMName + "'.");
            }
        }

        [Command("help")]
        public async Task HelpAsync()
        {
            // first, let's load our configuration file
            Console.WriteLine("[FMBot] Loading Configuration");
            var json = "";
            using (var fs = File.OpenRead(ConfigFileName))
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
            builder.WithDescription("Commands for " + SelfName.Username.ToString());

            foreach (var module in _service.Modules)
            {
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
                        x.Name = module.Name;
                        x.Value = description;
                        x.IsInline = false;
                    });
                }
            }

            await ReplyAsync("", false, builder.Build());
        }

        [Command("invite"), Summary("Invites the bot to a server")]
        public async Task inviteAsync()
        {
            string SelfID = Context.Client.CurrentUser.Id.ToString();
            await ReplyAsync("https://discordapp.com/oauth2/authorize?client_id=" + SelfID + "&scope=bot&permissions=0");
        }

        [Command("donate"), Summary("Please donate if you like this bot!")]
        public async Task donateAsync()
        {
            await ReplyAsync("Even though this bot is running for free, this bot needs funds in order to run for more than a year. If you like the bot and you would like to support its development, please donate to me at: https://www.paypal.me/Bitl");
        }

        [Command("github"), Summary("GitHub Page")]
        public async Task githubAsync()
        {
            await ReplyAsync("https://github.com/Bitl/FMBot_Discord");
        }

        [Command("bugs"), Summary("Report bugs here!")]
        public async Task bugsAsync()
        {
            await ReplyAsync("Report bugs here: https://github.com/Bitl/FMBot_Discord/issues");
        }
    }

    public class DBase
    {
        public static void WriteEntry(string filename, string id, string name)
        {
            File.AppendAllText(filename, id + Environment.NewLine + name + Environment.NewLine);
        }

        public static string GetNameForID(string filename, string id)
        {
            string line;

            using (StreamReader file = new StreamReader(filename))
            {
                while ((line = file.ReadLine()) != null)
                {
                    if (line.Contains(id))
                    {
                        string nextline = file.ReadLine();
                        file.Close();
                        return nextline;
                    }
                    else
                    {
                        continue;
                    }
                }
            }

            return "NULL";
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
    }
}
