using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.ResponseModels;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.LastFM
{
    public class TrackCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService;
        private readonly LastFMService _lastFmService;
        private readonly PlayService _playService;
        private readonly IUpdateService _updateService;
        private readonly IIndexService _indexService;
        private readonly WhoKnowsTrackService _whoKnowsTrackService;
        private readonly SpotifyService _spotifyService;
        private readonly Logger.Logger _logger;

        private readonly UserService _userService;

        private readonly IPrefixService _prefixService;

        public TrackCommands(Logger.Logger logger,
            IPrefixService prefixService,
            GuildService guildService,
            UserService userService,
            LastFMService lastFmService,
            SpotifyService spotifyService,
            WhoKnowsTrackService whoKnowsTrackService,
            PlayService playService,
            IUpdateService updateService,
            IIndexService indexService)
        {
            this._logger = logger;
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._userService = userService;
            this._lastFmService = lastFmService;
            this._spotifyService = spotifyService;
            this._whoKnowsTrackService = whoKnowsTrackService;
            this._playService = playService;
            this._updateService = updateService;
            this._indexService = indexService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("track", RunMode = RunMode.Async)]
        [Summary("Displays track info and stats.")]
        [Alias("tr", "ti", "ts", "trackinfo")]
        [UsernameSetRequired]
        public async Task TrackAsync(params string[] trackValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (trackValues.Any() && trackValues.First() == "help")
            {
                this._embed.WithTitle($"{prfx}track");
                this._embed.WithDescription($"Shows track info about the track you're currently listening to or searching for.");

                this._embed.AddField("Examples",
                    $"`{prfx}tr` \n" +
                    $"`{prfx}track` \n" +
                    $"`{prfx}track Depeche Mode Enjoy The Silence` \n" +
                    $"`{prfx}track Crystal Waters | Gypsy Woman (She's Homeless) - Radio Edit`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings, prfx);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Info about {track.Artist.Name} - {track.Name} for {userTitle}");

            if (Uri.IsWellFormedUriString(track.Url, UriKind.Absolute))
            {
                this._embed.WithUrl(track.Url);
            }

            this._embed.WithAuthor(this._embedAuthor);

            var spotifyTrack = await this._spotifyService.GetOrStoreTrackAsync(track);

            if (spotifyTrack != null && !string.IsNullOrEmpty(spotifyTrack.SpotifyId))
            {
                this._embed.AddField("Stats",
                    $"`{track.Listeners}` listeners\n" +
                    $"`{track.Playcount}` global {StringExtensions.GetPlaysString(track.Playcount)}\n" +
                    $"`{track.Userplaycount}` {StringExtensions.GetPlaysString(track.Userplaycount)} by you\n",
                    true);

                var trackLength = TimeSpan.FromMilliseconds(spotifyTrack.DurationMs.GetValueOrDefault());
                var formattedTrackLength = string.Format("{0}{1}:{2:D2}",
                    trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:",
                    trackLength.Minutes,
                    trackLength.Seconds);

                var pitch = StringExtensions.KeyIntToPitchString(spotifyTrack.Key.GetValueOrDefault());
                var bpm = $"{spotifyTrack.Tempo.GetValueOrDefault():0.0}";

                this._embed.AddField("Info",
                    $"`{formattedTrackLength}` duration\n" +
                    $"`{pitch}` key\n" +
                    $"`{bpm}` bpm\n",
                    true);
            }
            else
            {
                this._embed.AddField("Listeners", track.Listeners, true);
                this._embed.AddField("Global playcount", track.Playcount, true);
                this._embed.AddField("Your playcount", track.Userplaycount, true);
            }

            if (!string.IsNullOrWhiteSpace(track.Wiki?.Summary))
            {
                var linktext = $"<a href=\"{track.Url.Replace("https", "http")}\">Read more on Last.fm</a>";
                var filteredSummary = track.Wiki.Summary.Replace(linktext, "");
                if (!string.IsNullOrWhiteSpace(filteredSummary))
                {
                    this._embed.AddField("Summary", filteredSummary);
                }
            }

            if (track.Toptags.Tag.Any())
            {
                var tags = this._lastFmService.TopTagsToString(track.Toptags);

                this._embed.AddField("Tags", tags);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("trackplays", RunMode = RunMode.Async)]
        [Summary("Displays track info and stats.")]
        [Alias("tp", "trackplay", "tplays", "trackp")]
        [UsernameSetRequired]
        public async Task TrackPlaysAsync(params string[] trackValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (trackValues.Any() && trackValues.First() == "help")
            {
                this._embed.WithTitle($"{prfx}trackplays");
                this._embed.WithDescription($"Shows your total plays from the track you're currently listening to or searching for.");

                this._embed.AddField("Examples",
                    $"`{prfx}tp` \n" +
                    $"`{prfx}trackplays` \n" +
                    $"`{prfx}trackplays Mac DeMarco Here Comes The Cowboy` \n" +
                    $"`{prfx}trackplays Cocteau Twins | Heaven or Las Vegas`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings, prfx);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            var playString = track.Userplaycount == 1 ? "play" : "plays";
            this._embedAuthor.WithName($"{userTitle} has {track.Userplaycount} {playString} for {track.Name} by {track.Artist.Name}");
            this._embed.WithUrl(track.Url);
            this._embed.WithAuthor(this._embedAuthor);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }


        [Command("love", RunMode = RunMode.Async)]
        [Summary("Add track to loved tracks")]
        [UserSessionRequired]
        [Alias("l", "heart")]
        public async Task LoveAsync(params string[] trackValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (trackValues.Any() && trackValues.First() == "help")
            {
                this._embed.WithTitle($"{prfx}love");
                this._embed.WithDescription("Loves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings, prfx);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackLoved = await this._lastFmService.LoveTrackAsync(userSettings, track.Artist.Name, track.Name);

            if (trackLoved)
            {
                this._embed.WithTitle($"❤️ Loved track for {userTitle}");
                this._embed.WithDescription(LastFMService.ResponseTrackToLinkedString(track));
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync(
                    "Something went wrong while adding loved track.");
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("unlove", RunMode = RunMode.Async)]
        [Summary("Add track to loved tracks")]
        [UserSessionRequired]
        [Alias("ul", "unheart", "hate", "fuck")]
        public async Task UnLoveAsync(params string[] trackValues)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (trackValues.Any() && trackValues.First() == "help")
            {
                this._embed.WithTitle($"{prfx}unlove");
                this._embed.WithDescription("Unloves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings, prfx);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackLoved = await this._lastFmService.UnLoveTrackAsync(userSettings, track.Artist.Name, track.Name);

            if (trackLoved)
            {
                this._embed.WithTitle($"💔 Unloved track for {userTitle}");
                this._embed.WithDescription(LastFMService.ResponseTrackToLinkedString(track));
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync(
                    "Something went wrong while unloving track.");
                this.Context.LogCommandUsed(CommandResponse.Error);
                return;
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("toptracks", RunMode = RunMode.Async)]
        [Summary("Displays top tracks.")]
        [Alias("tt", "tl", "tracklist", "tracks", "trackslist")]
        [UsernameSetRequired]
        public async Task TopTracksAsync(params string[] extraOptions)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}toptracks options");
                this._embed.WithDescription($"- `{Constants.CompactTimePeriodList}`\n" +
                                            $"- `number of tracks (max 16)`\n" +
                                            $"- `user mention/id`");

                this._embed.AddField("Example",
                    $"`{prfx}toptracks alltime @john 11`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(extraOptions);
            var userSettings = await SettingService.GetUser(extraOptions, user.UserNameLastFM, this.Context);
            var amount = SettingService.GetAmount(extraOptions);

            try
            {
                Response<TopTracksResponse> topTracks;
                if (!timeSettings.UsePlays)
                {
                    topTracks = await this._lastFmService.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings.ApiParameter, amount);

                    if (!topTracks.Success)
                    {
                        this._embed.ErrorResponse(topTracks.Error, topTracks.Message, this.Context, this._logger);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }

                    if (topTracks.Content?.TopTracks?.Attr != null)
                    {
                        this._embedFooter.WithText($"{topTracks.Content.TopTracks.Attr.Total} different tracks in this time period");
                    }
                }
                else
                {
                    int userId;
                    if (userSettings.DifferentUser && userSettings.DiscordUserId.HasValue)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId.Value);
                        if (otherUser.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(otherUser);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(otherUser);
                        }

                        userId = otherUser.UserId;
                    }
                    else
                    {
                        if (user.LastIndexed == null)
                        {
                            await this._indexService.IndexUser(user);
                        }
                        else if (user.LastUpdated < DateTime.UtcNow.AddMinutes(-15))
                        {
                            await this._updateService.UpdateUser(user);
                        }

                        userId = user.UserId;
                    }

                    topTracks = await this._playService.GetTopTracks(userId,
                        timeSettings.PlayDays.GetValueOrDefault());

                    this._embedFooter.WithText($"{topTracks.Content.TopTracks.Track.Count} different tracks in this time period");

                    topTracks.Content.TopTracks.Track = topTracks.Content.TopTracks.Track.Take(amount).ToList();
                }

                var userUrl = $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/tracks?{timeSettings.UrlParameter}";

                if (!topTracks.Content.TopTracks.Track.Any())
                {
                    this._embed.WithDescription("No top tracks returned for selected time period.\n" +
                                                $"View [track history here]({userUrl})");
                    this._embed.WithColor(DiscordConstants.WarningColorOrange);
                    await ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                string userTitle;
                if (!userSettings.DifferentUser)
                {
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                var trackStrings = amount == 1 ? "track" : "tracks";
                this._embedAuthor.WithName($"Top {amount} {timeSettings.Description.ToLower()} {trackStrings} for {userTitle}");
                this._embedAuthor.WithUrl(userUrl);
                this._embed.WithAuthor(this._embedAuthor);

                var description = "";
                for (var i = 0; i < topTracks.Content.TopTracks.Track.Count; i++)
                {
                    var track = topTracks.Content.TopTracks.Track[i];

                    description += $"{i + 1}. [{track.Artist.Name}]({track.Artist.Url}) - [{track.Name}]({track.Url}) ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}) \n";
                }

                this._embed.WithDescription(description);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.FM info due to an internal error.");
            }
        }

        [Command("whoknowstrack", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("wt", "wkt", "wktr", "wtr", "wktrack", "wk track", "whoknows track")]
        [UsernameSetRequired]
        public async Task WhoKnowsAsync(params string[] trackValues)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (trackValues.Any() && trackValues.First() == "help")
            {
                this._embed.WithTitle($"{prfx}whoknowstrack");
                this._embed.WithDescription($"Shows what members in your server listened to the track you're currently listening to or searching for.");

                this._embed.AddField("Examples",
                    $"`{prfx}wt` \n" +
                    $"`{prfx}whoknowstrack` \n" +
                    $"`{prfx}whoknowstrack Hothouse Flowers Don't Go` \n" +
                    $"`{prfx}whoknowstrack Natasha Bedingfield | Unwritten`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-50))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 50 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (trackValues.Any() && trackValues.First() == "help")
            {
                await ReplyAsync(
                    $"Usage: `{prfx}whoknowstrack 'artist and track name'`\n" +
                    "If you don't enter any track name, it will get the info from the track you're currently listening to.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings, prfx);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.Artist.Name} - {track.Name}";

            try
            {
                var guild = await guildTask;
                var users = guild.GuildUsers.Select(s => s.User).ToList();

                if (!users.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    await this._indexService.AddUserToGuild(this.Context.Guild, userSettings);
                    users.Add(userSettings);
                }

                var usersWithArtist = await this._whoKnowsTrackService.GetIndexedUsersForTrack(this.Context, users, track.Artist.Name, track.Name);

                if (track.Userplaycount != 0)
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, userSettings, guildUser, trackName, track.Userplaycount);
                }

                var serverUsers = WhoKnowsService.WhoKnowsListToString(usersWithArtist);
                if (usersWithArtist.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this track.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows track requested by {userTitle} - Users with 3 plays or higher are shown";

                var rnd = new Random();
                if (rnd.Next(0, 4) == 1 && lastIndex < DateTime.UtcNow.AddDays(-3))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                this._embed.WithTitle($"Who knows {trackName} in {this.Context.Guild.Name}");

                if (Uri.IsWellFormedUriString(track.Url, UriKind.Absolute))
                {
                    this._embed.WithUrl(track.Url);
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Something went wrong while using whoknows track. Please let us know as this feature is in beta.");
            }
        }


        [Command("servertracks", RunMode = RunMode.Async)]
        [Summary("Shows top albums for your server")]
        [Alias("st", "stt", "servertoptracks", "servertrack")]
        public async Task GuildAlbumsAsync(params string[] extraOptions)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (extraOptions.Any() && extraOptions.First() == "help")
            {
                this._embed.WithTitle($"{prfx}servertracks");

                var helpDescription = new StringBuilder();
                helpDescription.AppendLine("Shows the top tracks for your server.");
                helpDescription.AppendLine();
                helpDescription.AppendLine("Available time periods: `weekly` and `alltime`");
                helpDescription.AppendLine("Available order options: `plays` and `listeners`");

                this._embed.WithDescription(helpDescription.ToString());

                this._embed.AddField("Examples",
                    $"`{prfx}st` \n" +
                    $"`{prfx}st a p` \n" +
                    $"`{prfx}servertracks` \n" +
                    $"`{prfx}servertracks alltime` \n" +
                    $"`{prfx}servertracks listeners weekly`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (guild.LastIndexed == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-60))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 60 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverTrackSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Playcount
            };

            serverTrackSettings = SettingService.SetGuildRankingSettings(serverTrackSettings, extraOptions);

            try
            {
                IReadOnlyList<ListTrack> topGuildTracks;
                var users = guild.GuildUsers.Select(s => s.User).ToList();
                if (serverTrackSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildTracks = await this._whoKnowsTrackService.GetTopTracksForGuild(users, serverTrackSettings.OrderType);
                    this._embed.WithTitle($"Top alltime tracks in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildTracks = await this._playService.GetTopWeekTracksForGuild(users, serverTrackSettings.OrderType);
                    this._embed.WithTitle($"Top weekly tracks in {this.Context.Guild.Name}");
                }

                var description = "";
                var footer = "";

                if (serverTrackSettings.OrderType == OrderType.Listeners)
                {
                    footer += "Listeners / Plays - Ordered by listeners\n";
                    foreach (var track in topGuildTracks)
                    {
                        description += $"`{track.ListenerCount}` / `{track.Playcount}` | **{track.TrackName}** by **{track.ArtistName}**\n";
                    }
                }
                else
                {
                    footer += "Plays / Listeners - Ordered by plays\n";
                    foreach (var track in topGuildTracks)
                    {
                        description += $"`{track.Playcount}` / `{track.ListenerCount}` | **{track.TrackName}** by **{track.ArtistName}**\n";
                    }
                }

                this._embed.WithDescription(description);

                var rnd = new Random();
                var randomHintNumber = rnd.Next(0, 5);
                if (randomHintNumber == 1)
                {
                    footer += $"View specific album listeners with {prfx}whoknowstrack";
                }
                else if (randomHintNumber == 2)
                {
                    footer += $"Available time periods: alltime and weekly";
                }
                else if (randomHintNumber == 3)
                {
                    footer += $"Available sorting options: plays and listeners";
                }
                if (guild.LastIndexed < DateTime.UtcNow.AddDays(-7) && randomHintNumber == 4)
                {
                    footer += $"Missing members? Update with {prfx}index\n";
                }

                this._embedFooter.WithText(footer);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while using servertracks. Please report this issue.");
            }
        }

        private async Task<ResponseTrack> SearchTrack(string[] trackValues, User userSettings, string prfx)
        {
            string searchValue;
            if (trackValues.Any())
            {
                searchValue = string.Join(" ", trackValues);

                if (searchValue.Contains(" | "))
                {
                    var trackInfo = await this._lastFmService.GetTrackInfoAsync(searchValue.Split(" | ")[1], searchValue.Split(" | ")[0],
                        userSettings.UserNameLastFM);
                    return trackInfo;
                }
            }
            else
            {
                var track = await this._lastFmService.GetRecentScrobblesAsync(userSettings.UserNameLastFM, 1);

                if (!track.Content.Any())
                {
                    this._embed.NoScrobblesFoundErrorResponse(track.Status, prfx, userSettings.UserNameLastFM);
                    await this.ReplyAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return null;
                }

                var trackResult = track.Content.First();
                var trackInfo = await this._lastFmService.GetTrackInfoAsync(trackResult.Name, trackResult.ArtistName,
                    userSettings.UserNameLastFM);
                return trackInfo;
            }

            var result = await this._lastFmService.SearchTrackAsync(searchValue);
            if (result.Success && result.Content.Any())
            {
                var track = result.Content[0];

                var trackInfo = await this._lastFmService.GetTrackInfoAsync(track.Name, track.ArtistName,
                    userSettings.UserNameLastFM);
                return trackInfo;
            }

            if (result.Success)
            {
                this._embed.WithDescription($"Track could not be found, please check your search values and try again.");
                await this.ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            this._embed.WithDescription($"Last.fm returned an error: {result.Status}");
            await this.ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Error);
            return null;
        }
    }
}
