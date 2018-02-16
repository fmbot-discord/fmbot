using Discord;
using Discord.Commands;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using IF.Lastfm.Core.Objects;
using SpotifyAPI.Web;
using SpotifyAPI.Web.Auth;
using SpotifyAPI.Web.Enums;
using SpotifyAPI.Web.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using YoutubeSearch;
using static FMBot_Discord.FMBotModules;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly TimerService _timer;

        public FMBotCommands(CommandService service, TimerService timer)
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
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use fmset to set your name.");
                }
                else
                {
                    var cfgjson = await JsonCfg.GetJSONDataAsync();

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

                            if (FMBotAdminUtil.IsOwner(DiscordUser))
                            {
                                builder.WithTitle(LastFMName + ", FMBot Owner");
                            }
                            else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                            {
                                builder.WithTitle(LastFMName + ", FMBot Super Admin");
                            }
                            else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                                if (FMBotAdminUtil.IsOwner(DiscordUser))
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Owner");
                                }
                                else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Super Admin");
                                }
                                else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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

                                if (FMBotAdminUtil.IsOwner(DiscordUser))
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Owner");
                                }
                                else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                                {
                                    builder.WithTitle(LastFMName + ", FMBot Super Admin");
                                }
                                else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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

                                if (FMBotAdminUtil.IsOwner(DiscordUser))
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Owner\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Super Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "**Previous** - " + LastArtistName + " - " + LastTrackName + " [" + LastAlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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

                                if (FMBotAdminUtil.IsOwner(DiscordUser))
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Owner\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                                {
                                    await Context.Channel.SendMessageAsync("FMBot Super Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                                }
                                else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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

                            if (FMBotAdminUtil.IsOwner(DiscordUser))
                            {
                                await Context.Channel.SendMessageAsync("FMBot Owner\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                            }
                            else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                            {
                                await Context.Channel.SendMessageAsync("FMBot Super Admin\n**Recent** - " + ArtistName + " - " + TrackName + " [" + AlbumName + "]" + "\n" + "https://www.last.fm/user/" + LastFMName + "\n" + LastFMName + "'s Total Tracks: " + playcount.ToString());
                            }
                            else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                        await ReplyAsync("You have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use .fm again!");
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
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                try
                {
                    var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                    LastTrack currentTrack = tracks.Content.ElementAt(0);

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                    string videotitle = "";
                    string videouploader = "";
                    string videoduration = "";

                    try
                    {
                        string querystring = TrackName + " - " + ArtistName + " " + AlbumName;
                        var items = new VideoSearch();
                        var item = items.SearchQuery(querystring, 1).ElementAt(0);

                        await Context.Channel.SendMessageAsync(item.Url);

                        videotitle = item.Title;
                        videouploader = item.Author;
                        videoduration = item.Duration;

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
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                        builder.AddField("Video: " + videotitle, "Uploaded by: " + videouploader + " | Duration: " + videoduration);

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        var userinfo = await client.User.GetInfoAsync(LastFMName);
                        var playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    catch (Exception)
                    {
                        await ReplyAsync("No results have been found for this track.");
                    }
                }
                catch (Exception)
                {
                    await ReplyAsync("You have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use .fmyt again!");
                }
            }
        }

        [Command("fmspotify")]
        public async Task fmspotifyAsync(IUser user = null)
        {
            var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
            string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
            if (LastFMName.Equals("NULL"))
            {
                await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
            }
            else
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();
                var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);

                try
                {
                    var tracks = await client.User.GetRecentScrobbles(LastFMName, null, 1, 2);
                    LastTrack currentTrack = tracks.Content.ElementAt(0);

                    string TrackName = string.IsNullOrWhiteSpace(currentTrack.Name) ? null : currentTrack.Name;
                    string ArtistName = string.IsNullOrWhiteSpace(currentTrack.ArtistName) ? null : currentTrack.ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(currentTrack.AlbumName) ? null : currentTrack.AlbumName;

                    int trackpopularity = 0;
                    bool trackexplicit = false;
                    int trackduration = 0;

                    //Create the auth object
                    var auth = new ClientCredentialsAuth()
                    {
                        ClientId = cfgjson.SpotifyKey,
                        ClientSecret = cfgjson.SpotifySecret,
                        Scope = Scope.None,
                    };
                    //With this token object, we now can make calls
                    Token token = auth.DoAuth();

                    var _spotify = new SpotifyWebAPI()
                    {
                        TokenType = token.TokenType,
                        AccessToken = token.AccessToken,
                        UseAuth = true
                    };

                    string querystring = TrackName + " - " + ArtistName + " " + AlbumName;

                    SearchItem item = _spotify.SearchItems(querystring, SearchType.Track);

                    if (item.Tracks.Items.Any())
                    {
                        FullTrack track = item.Tracks.Items.FirstOrDefault();
                        SimpleArtist trackArtist = track.Artists.FirstOrDefault();

                        await Context.Channel.SendMessageAsync(track.Uri);

                        trackpopularity = track.Popularity;
                        trackexplicit = track.Explicit;
                        trackduration = track.DurationMs;

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
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }
                        builder.WithDescription("Recently Played on Spotify");

                        string nulltext = "[undefined]";

                        string TrackName2 = string.IsNullOrWhiteSpace(TrackName) ? nulltext : TrackName;
                        string ArtistName2 = string.IsNullOrWhiteSpace(ArtistName) ? nulltext : ArtistName;
                        string AlbumName2 = string.IsNullOrWhiteSpace(AlbumName) ? nulltext : AlbumName;

                        TimeSpan t = TimeSpan.FromMilliseconds(trackduration);
                        string durationconv = string.Format("{1:D2}:{2:D2}", t.Minutes, t.Seconds);

                        builder.AddField(TrackName2, ArtistName2 + " | " + AlbumName2);
                        builder.AddField("Duration: " + durationconv, "Popularity: " + trackpopularity.ToString() + " | Explicit: " + trackexplicit.ToString());

                        EmbedFooterBuilder efb = new EmbedFooterBuilder();

                        var userinfo = await client.User.GetInfoAsync(LastFMName);
                        var playcount = userinfo.Content.Playcount;

                        efb.Text = LastFMName + "'s Total Tracks: " + playcount.ToString();

                        builder.WithFooter(efb);

                        await Context.Channel.SendMessageAsync("", false, builder.Build());
                    }
                    else
                    {
                        await ReplyAsync("No results have been found for this track.");
                    }
                }
                catch
                {
                    await ReplyAsync("You have no scrobbles on your Last.FM profile or the Spotify credentials may have not been set correctly. Try scrobbling a song with a Last.FM scrobbler and then use .fmspotify again!");
                }
            }
        }

        [Command("fmchart")]
        public async Task fmchartAsync(string time = "weekly", string chartalbums = "9", string chartrows = "3", IUser user = null)
        {
            var loadingText = "Loading your FMBot chart...";
            var loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    int max = int.Parse(chartalbums);
                    int rows = int.Parse(chartrows);

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
                    if (FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Owner");
                    }
                    else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Super Admin");
                    }
                    else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
        public async Task fmartistchartAsync(string time = "weekly", string chartalbums = "9", string chartrows = "3", IUser user = null)
        {
            var loadingText = "Loading your FMBot artist chart...";
            var loadingmsg = await Context.Channel.SendMessageAsync(loadingText);

            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
                    var client = new LastfmClient(cfgjson.FMKey, cfgjson.FMSecret);
                    int max = int.Parse(chartalbums);
                    int rows = int.Parse(chartrows);

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
                    if (FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Owner");
                    }
                    else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Super Admin");
                    }
                    else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                var DiscordUser = (IGuildUser)user ?? (IGuildUser)Context.Message.Author;
                string LastFMName = DBase.GetNameForID(DiscordUser.Id.ToString());
                if (LastFMName.Equals("NULL"))
                {
                    await ReplyAsync("Your Last.FM name was unable to be found. Please use .fmset to set your name.");
                }
                else
                {
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

                            if (FMBotAdminUtil.IsOwner(DiscordUser))
                            {
                                builder.WithTitle(LastFMName + ", FMBot Owner");
                            }
                            else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                            {
                                builder.WithTitle(LastFMName + ", FMBot Super Admin");
                            }
                            else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                            {
                                builder.WithTitle(LastFMName + ", FMBot Admin");
                            }
                            else
                            {
                                builder.WithTitle(LastFMName);
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
                                    if (LastFMFriends.Count() <= 8)
                                    {
                                        var userinfo = await client.User.GetInfoAsync(friend);
                                        playcount = playcount + userinfo.Content.Playcount;
                                    }
                                }
                            }
                            catch (Exception)
                            {

                            }

                            if (LastFMFriends.Count() <= 8)
                            {
                                EmbedFooterBuilder efb = new EmbedFooterBuilder();
                                efb.Text = amountOfScrobbles + playcount.ToString();
                                builder.WithFooter(efb);
                            }

                            await Context.Channel.SendMessageAsync("", false, builder.Build());
                        }
                        catch (Exception)
                        {
                            await ReplyAsync("Your friends have no scrobbles on their Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use fmrecent again!");
                        }
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your friends could not be found. Please set your friends using fmsetfriends.");
            }
        }

        [Command("fmrecent")]
        [Alias("fmrecenttracks")]
        public async Task fmrecentAsync(string list = "5", IUser user = null)
        {
            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                int num = int.Parse(list);

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
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                        await ReplyAsync("You have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use .fmrecent again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmartists")]
        public async Task fmartistsAsync(string list = "5", string time = "overall", IUser user = null)
        {
            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                int num = int.Parse(list);
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
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Overall;

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

                        var artists = await client.User.GetTopArtists(LastFMName, timespan, 1, num);

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
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }

                        if (time.Equals("weekly"))
                        {
                            builder.WithDescription("Top " + num + " Weekly Artist List");
                        }
                        else if (time.Equals("monthly"))
                        {
                            builder.WithDescription("Top " + num + " Monthly Artist List");
                        }
                        else if (time.Equals("yearly"))
                        {
                            builder.WithDescription("Top " + num + " Yearly Artist List");
                        }
                        else if (time.Equals("overall"))
                        {
                            builder.WithDescription("Top " + num + " Overall Artist List");
                        }

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
                        await ReplyAsync("You have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use .fmartists again!");
                    }
                }
            }
            catch (Exception)
            {
                await ReplyAsync("Your Last.FM name cannot be found. Please use the fmset command.");
            }
        }

        [Command("fmalbums")]
        public async Task fmalbumsAsync(string list = "5", string time = "overall", IUser user = null)
        {
            try
            {
                var cfgjson = await JsonCfg.GetJSONDataAsync();

                int num = int.Parse(list);
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
                        LastStatsTimeSpan timespan = LastStatsTimeSpan.Overall;

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

                        var albums = await client.User.GetTopAlbums(LastFMName, timespan, 1, num);

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
                        if (FMBotAdminUtil.IsOwner(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Owner");
                        }
                        else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Super Admin");
                        }
                        else if (FMBotAdminUtil.IsAdmin(DiscordUser))
                        {
                            builder.WithTitle(LastFMName + ", FMBot Admin");
                        }
                        else
                        {
                            builder.WithTitle(LastFMName);
                        }

                        if (time.Equals("weekly"))
                        {
                            builder.WithDescription("Top " + num + " Weekly Album List");
                        }
                        else if (time.Equals("monthly"))
                        {
                            builder.WithDescription("Top " + num + " Monthly Album List");
                        }
                        else if (time.Equals("yearly"))
                        {
                            builder.WithDescription("Top " + num + " Yearly Album List");
                        }
                        else if (time.Equals("overall"))
                        {
                            builder.WithDescription("Top " + num + " Overall Album List");
                        }

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
                        await ReplyAsync("You have no scrobbles on your Last.FM profile. Try scrobbling a song with a Last.FM scrobbler and then use .fmalbums again!");
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
                var cfgjson = await JsonCfg.GetJSONDataAsync();

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
                    if (FMBotAdminUtil.IsOwner(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Owner");
                    }
                    else if (FMBotAdminUtil.IsSuperAdmin(DiscordUser))
                    {
                        builder.WithTitle(LastFMName + ", FMBot Super Admin");
                    }
                    else if (FMBotAdminUtil.IsAdmin(DiscordUser))
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
                    builder.AddInlineField("Is FMBot Admin? ", FMBotAdminUtil.IsAdmin(DiscordUser).ToString());
                    builder.AddInlineField("Is FMBot Super Admin? ", FMBotAdminUtil.IsSuperAdmin(DiscordUser).ToString());
                    builder.AddInlineField("Is FMBot Owner? ", FMBotAdminUtil.IsOwner(DiscordUser).ToString());

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
        [Alias("fmfeaturedavatar", "fmfeatureduser", "fmfeaturedalbum")]
        public async Task fmfeaturedAsync()
        {
            try
            {
                var builder = new EmbedBuilder();
                var SelfUser = Context.Client.CurrentUser;
                builder.WithThumbnailUrl(SelfUser.GetAvatarUrl());
                builder.AddInlineField("Featured Album:", _timer.GetTrackString());

                await Context.Channel.SendMessageAsync("", false, builder.Build());
            }
            catch (Exception)
            {
                await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
            }
        }

        [Command("fmset"), Summary("Sets your Last.FM name.")]
        [Alias("fmsetname", "fmsetmode")]
        public async Task fmsetAsync([Summary("Your Last.FM name")] string name, [Summary("The mode you want to use.")] string mode = "")
        {
            string SelfID = Context.Message.Author.Id.ToString();
            if (DBase.EntryExists(SelfID))
            {
                int modeint = 0;

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    modeint = DBase.GetIntForModeName(mode);
                    if (modeint == 4)
                    {
                        await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                        return;
                    }
                }
                else
                {
                    modeint = DBase.GetModeIntForID(SelfID);
                }

                int admin = DBase.GetAdminIntFromID(SelfID);

                DBase.WriteEntry(SelfID, name, modeint, admin);
            }
            else
            {
                int modeint = DBase.GetIntForModeName("embedmini");
                DBase.WriteEntry(SelfID, name, modeint);
            }
            string LastFMMode = DBase.GetNameForModeInt(DBase.GetModeIntForID(SelfID));
            await ReplyAsync("Your Last.FM name has been set to '" + name + "' and your FMBot mode has been set to '" + LastFMMode + "'.");
        }

        [Command("fmsetfriends"), Summary("Sets your friends' Last.FM names.")]
        [Alias("fmfriendsset")]
        public async Task fmfriendssetAsync([Summary("Friend names")] params string[] friends)
        {
            string SelfID = Context.Message.Author.Id.ToString();

            int friendcount = DBase.RemoveFriendsEntry(SelfID, friends);

            if (friendcount > 1 || friendcount < 1)
            {
                await ReplyAsync("Succesfully added " + friendcount + " friends.");
            }
            else
            {
                await ReplyAsync("Succesfully added a friend.");
            }
        }

        [Command("fmremovefriends"), Summary("Remove your friends' Last.FM names.")]
        [Alias("fmfriendsremove")]
        public async Task fmfriendsremoveAsync([Summary("Friend names")] params string[] friends)
        {
            string SelfID = Context.Message.Author.Id.ToString();

            int friendcount = DBase.RemoveFriendsEntry(SelfID, friends);

            if (friendcount > 1 || friendcount < 1)
            {
                await ReplyAsync("Succesfully removed " + friendcount + " friends.");
            }
            else
            {
                await ReplyAsync("Succesfully removed a friend.");
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

        [Command("fmhelp")]
        public async Task fmhelpAsync()
        {
            var cfgjson = await JsonCfg.GetJSONDataAsync();

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
                string description = null;
                foreach (var cmd in module.Commands)
                {
                    var result = await cmd.CheckPreconditionsAsync(Context);
                    if (result.IsSuccess)
                    {
                        if (!string.IsNullOrWhiteSpace(cmd.Summary))
                        {
                            description += $"{prefix}{cmd.Aliases.First()} - {cmd.Summary}\n";
                        }
                        else
                        {
                            description += $"{prefix}{cmd.Aliases.First()}\n";
                        }
                    }
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

            builder.AddField("FMBot Modes for the fmset command:", "embedmini\nembedfull\ntextfull\ntextmini");

            builder.AddField("FMBot Time Periods for the fmchart, fmartistchart, fmartists, and fmalbums commands:", "weekly\nmonthly\nyearly\noverall");

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
            await ReplyAsync("If you like the bot and you would like to support its development, feel free to support the developer at: https://www.paypal.me/Bitl");
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

        [Command("fmserver"), Summary("Join the Discord server!")]
        public async Task serverAsync()
        {
            await ReplyAsync("Join the Discord server! https://discord.gg/srmpCaa");
        }
    }
}
