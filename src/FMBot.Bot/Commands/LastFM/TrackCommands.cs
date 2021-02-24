using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.API.Rest;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Interactivity;
using Interactivity.Confirmation;

namespace FMBot.Bot.Commands.LastFM
{
    [Name("Tracks")]
    public class TrackCommands : ModuleBase
    {
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly LastFmService _lastFmService;
        private readonly PlayService _playService;
        private readonly SettingService _settingService;
        private readonly SpotifyService _spotifyService;
        private readonly UserService _userService;
        private readonly WhoKnowsTrackService _whoKnowsTrackService;
        private readonly WhoKnowsPlayService _whoKnowsPlayService;
        private readonly WhoKnowsService _whoKnowsService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly InteractivityService _interactivity;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new List<DateTimeOffset>();
        private static readonly List<SocketUser> StackCooldownTarget = new List<SocketUser>();

        public TrackCommands(
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                IUpdateService updateService,
                LastFmService lastFmService,
                PlayService playService,
                SettingService settingService,
                SpotifyService spotifyService,
                UserService userService,
                WhoKnowsTrackService whoKnowsTrackService,
                WhoKnowsPlayService whoKnowsPlayService,
                InteractivityService interactivity,
                WhoKnowsService whoKnowsService)
        {
            this._guildService = guildService;
            this._indexService = indexService;
            this._lastFmService = lastFmService;
            this._playService = playService;
            this._prefixService = prefixService;
            this._settingService = settingService;
            this._spotifyService = spotifyService;
            this._updateService = updateService;
            this._userService = userService;
            this._whoKnowsTrackService = whoKnowsTrackService;
            this._whoKnowsPlayService = whoKnowsPlayService;
            this._interactivity = interactivity;
            this._whoKnowsService = whoKnowsService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("track", RunMode = RunMode.Async)]
        [Summary("Displays track info and stats.")]
        [Alias("tr", "ti", "ts", "trackinfo")]
        [UsernameSetRequired]
        public async Task TrackAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
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

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
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
                var tags = LastFmService.TopTagsToString(track.Toptags);

                this._embed.AddField("Tags", tags);
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }

        [Command("trackplays", RunMode = RunMode.Async)]
        [Summary("Displays track info and stats.")]
        [Alias("tp", "trackplay", "tplays", "trackp", "track plays")]
        [UsernameSetRequired]
        public async Task TrackPlaysAsync([Remainder] string trackValues = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
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

            var userSettings = await this._settingService.GetUser(trackValues, user, this.Context);

            var track = await this.SearchTrack(userSettings.NewSearchValue, user.UserNameLastFM, user.SessionKeyLastFm, userSettings.UserNameLastFm);
            if (track == null)
            {
                return;
            }

            var reply =
                $"**{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}** has `{track.Userplaycount}` {StringExtensions.GetPlaysString(track.Userplaycount)} for **{track.Name.FilterOutMentions()}** " +
                $"by **{track.Artist.Name.FilterOutMentions()}**";

            if (!userSettings.DifferentUser && user.LastUpdated != null)
            {
                var playsLastWeek =
                    await this._playService.GetWeekTrackPlaycountAsync(userSettings.UserId, track.Name, track.Artist.Name);
                if (playsLastWeek != 0)
                {
                    reply += $" (`{playsLastWeek}` last week)";
                }
            }

            await this.Context.Channel.SendMessageAsync(reply);
            this.Context.LogCommandUsed();
        }


        [Command("love", RunMode = RunMode.Async)]
        [Summary("Add track to loved tracks")]
        [UserSessionRequired]
        [Alias("l", "heart")]
        public async Task LoveAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}love");
                this._embed.WithDescription("Loves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackLoved = await this._lastFmService.LoveTrackAsync(userSettings, track.Artist.Name, track.Name);

            if (trackLoved)
            {
                this._embed.WithTitle($"❤️ Loved track for {userTitle}");
                this._embed.WithDescription(LastFmService.ResponseTrackToLinkedString(track));
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
        public async Task UnLoveAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}unlove");
                this._embed.WithDescription("Unloves the track you're currently listening to or searching for on last.fm.");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackLoved = await this._lastFmService.UnLoveTrackAsync(userSettings, track.Artist.Name, track.Name);

            if (trackLoved)
            {
                this._embed.WithTitle($"💔 Unloved track for {userTitle}");
                this._embed.WithDescription(LastFmService.ResponseTrackToLinkedString(track));
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

        [Command("scrobble", RunMode = RunMode.Async)]
        [Summary("Scrobbles a track to Last.fm")]
        [UserSessionRequired]
        [Alias("sb")]
        public async Task ScrobbleAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (string.IsNullOrWhiteSpace(trackValues) || trackValues.ToLower() == "help")
            {
                this._embed.WithTitle($"{prfx}scrobble");
                this._embed.WithDescription("Scrobbles a track. You can enter a search value or enter the exact name with separators. " +
                                            "You can only scrobble tracks that already exist on Last.fm.");

                this._embed.AddField("Search for a track to scrobble",
                    $"Format: `{prfx}scrobble SearchValue`\n" +
                    $"`{prfx}sb Stronger Kanye` *(scrobbles Stronger by Kanye West)*\n" +
                    $"`{prfx}scrobble Loona Heart Attack` *(scrobbles Heart Attack (츄) by LOONA)*");

                this._embed.AddField("Or enter the exact name with separators",
                    $"Format: `{prfx}scrobble Artist | Track`\n" +
                    $"`{prfx}scrobble Mac DeMarco | Chamber of Reflection`\n" +
                    $"`{prfx}scrobble Home | Climbing Out`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var msg = this.Context.Message as SocketUserMessage;
            if (StackCooldownTarget.Contains(this.Context.Message.Author))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(30) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                        .AddSeconds(30) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= 28)
                    {
                        await ReplyAsync("Please wait before scrobbling to Last.fm again.");
                        this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    }

                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
            }
            else
            {
                StackCooldownTarget.Add(msg.Author);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);

            var trackScrobbled = await this._lastFmService.ScrobbleAsync(userSettings, track.Artist.Name, track.Name, track.Album?.Title);

            if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Accepted > 0)
            {
                this._embed.WithTitle($"Scrobbled track for {userTitle}");
                this._embed.WithDescription(LastFmService.ResponseTrackToLinkedString(track));
            }
            else if (trackScrobbled.Success && trackScrobbled.Content.Scrobbles.Attr.Ignored > 0)
            {
                this._embed.WithTitle($"Last.fm ignored scrobble for {userTitle}");
                var description = new StringBuilder();

                if (!string.IsNullOrWhiteSpace(trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text))
                {
                    description.AppendLine($"Reason: {trackScrobbled.Content.Scrobbles.Scrobble.IgnoredMessage?.Text}");
                }

                description.AppendLine(LastFmService.ResponseTrackToLinkedString(track));
                this._embed.WithDescription(description.ToString());
            }
            else
            {
                await this.Context.Message.Channel.SendMessageAsync("Something went wrong while scrobbling track :(.");
                this.Context.LogCommandWithLastFmError(trackScrobbled.Error);
                return;
            }

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();

            //var request = new ConfirmationBuilder()
            //    .WithContent(new PageBuilder().WithText("Please Confirm"))
            //    .Build();

            //var result = await Interactivity.SendConfirmationAsync(request, Context.Channel);

            //if (result.Value == true)
            //{
            //    await Context.Channel.SendMessageAsync("Confirmed :thumbsup:!");
            //}
            //else
            //{
            //    await Context.Channel.SendMessageAsync("Declined :thumbsup:!");
            //}

        }

        [Command("toptracks", RunMode = RunMode.Async)]
        [Summary("Displays top tracks.")]
        [Alias("tt", "tl", "tracklist", "tracks", "trackslist", "top tracks", "top track")]
        [UsernameSetRequired]
        public async Task TopTracksAsync([Remainder] string extraOptions = null)
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(extraOptions) && extraOptions.ToLower() == "help")
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

            var timePeriodString = extraOptions;

            var amountString = extraOptions;

            var timeSettings = SettingService.GetTimePeriod(timePeriodString);
            var userSettings = await this._settingService.GetUser(extraOptions, user, this.Context);
            var amount = SettingService.GetAmount(amountString);

            try
            {
                Response<TopTracksLfmResponse> topTracks;
                if (!timeSettings.UsePlays)
                {
                    topTracks = await this._lastFmService.GetTopTracksAsync(userSettings.UserNameLastFm, timeSettings.ApiParameter, amount);

                    if (!topTracks.Success)
                    {
                        this._embed.ErrorResponse(topTracks.Error, topTracks.Message, this.Context);
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
                    if (userSettings.DifferentUser)
                    {
                        var otherUser = await this._userService.GetUserAsync(userSettings.DiscordUserId);
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

                    if (topTracks.Content.TopTracks.Track.Count > 10)
                    {
                        description += $"{i + 1}. **{track.Artist.Name}** - **{track.Name}** ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}) \n";
                    }
                    else
                    {
                        description += $"{i + 1}. **[{track.Artist.Name}]({track.Artist.Url})** - **[{track.Name}]({track.Url})** ({track.Playcount} {StringExtensions.GetPlaysString(track.Playcount)}) \n";
                    }

                }

                this._embed.WithDescription(description);
                this._embed.WithFooter(this._embedFooter);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show Last.fm info due to an internal error.");
            }
        }

        [Command("whoknowstrack", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same artist in your server")]
        [Alias("wt", "wkt", "wktr", "wtr", "wktrack", "wk track", "whoknows track")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task WhoKnowsTrackAsync([Remainder] string trackValues = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.ToLower() == "help")
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
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.Name} by {track.Artist.Name}";

            try
            {
                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId), userSettings);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(userSettings.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateUserName(currentUser, await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId));

                var usersWithTrack = await this._whoKnowsTrackService.GetIndexedUsersForTrack(this.Context, guild.GuildId, track.Artist.Name, track.Name);

                if (track.Userplaycount != 0)
                {
                    usersWithTrack = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithTrack, currentUser, trackName, track.Userplaycount);
                }

                var filteredUsersWithTrack = WhoKnowsService.FilterGuildUsersAsync(usersWithTrack, guild);

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithTrack, userSettings.UserId, PrivacyLevel.Server);
                if (filteredUsersWithTrack.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this track.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"WhoKnows track requested by {userTitle}";

                var rnd = new Random();
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-15))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (filteredUsersWithTrack.Any() && filteredUsersWithTrack.Count > 1)
                {
                    var serverListeners = filteredUsersWithTrack.Count;
                    var serverPlaycount = filteredUsersWithTrack.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithTrack.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (usersWithTrack.Count > filteredUsersWithTrack.Count)
                {
                    var filteredAmount = usersWithTrack.Count - filteredUsersWithTrack.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }

                this._embed.WithTitle($"{trackName} in {this.Context.Guild.Name}");

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


        [Command("globalwhoknowstrack", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to the same track on .fmbot")]
        [Alias("gwt", "gwkt", "gwtr", "gwktr", "globalwhoknows track")]
        [UsernameSetRequired]
        [GuildOnly]
        public async Task GlobalWhoKnowsTrackAsync([Remainder] string trackValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }


            var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);
            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            var track = await this.SearchTrack(trackValues, userSettings.UserNameLastFM, userSettings.SessionKeyLastFm);
            if (track == null)
            {
                return;
            }

            var trackName = $"{track.Name} by {track.Artist.Name}";

            try
            {
                var usersWithArtist = await this._whoKnowsTrackService.GetGlobalUsersForTrack(this.Context, track.Artist.Name, track.Name);

                if (track.Userplaycount != 0 && this.Context.Guild != null)
                {
                    var discordGuildUser = await this.Context.Guild.GetUserAsync(userSettings.DiscordUserId);
                    var guildUser = new GuildUser
                    {
                        UserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : userSettings.UserNameLastFM,
                        User = userSettings
                    };
                    usersWithArtist = WhoKnowsService.AddOrReplaceUserToIndexList(usersWithArtist, guildUser, trackName, track.Userplaycount);
                }

                var guild = await guildTask;

                var filteredUsersWithAlbum = await this._whoKnowsService.FilterGlobalUsersAsync(usersWithArtist);

                filteredUsersWithAlbum =
                    WhoKnowsService.ShowGuildMembersInGlobalWhoKnowsAsync(filteredUsersWithAlbum, guild.GuildUsers.ToList());

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithAlbum, userSettings.UserId, PrivacyLevel.Global);
                if (!filteredUsersWithAlbum.Any())
                {
                    serverUsers = "Nobody that uses .fmbot has listened to this track.";
                }

                this._embed.WithDescription(serverUsers);

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                var footer = $"Global WhoKnows track requested by {userTitle}";

                if (filteredUsersWithAlbum.Any() && filteredUsersWithAlbum.Count() > 1)
                {
                    var serverListeners = filteredUsersWithAlbum.Count();
                    var serverPlaycount = filteredUsersWithAlbum.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithAlbum.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (userSettings.PrivacyLevel != PrivacyLevel.Global)
                {
                    footer += $"\nYou are currently not globally visible - use '{prfx}privacy global' to enable.";
                }

                this._embed.WithTitle($"{trackName} globally");

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
                if (!string.IsNullOrEmpty(e.Message) && e.Message.Contains("The server responded with error 50013: Missing Permissions"))
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                     "Make sure it has permission to 'Embed links' and 'Attach Images'");
                }
                else
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Something went wrong while using global whoknows track.");
                }
            }
        }


        [Command("servertracks", RunMode = RunMode.Async)]
        [Summary("Shows top albums for your server")]
        [Alias("st", "stt", "servertoptracks", "servertrack", "server tracks")]
        [GuildOnly]
        public async Task GuildTracksAsync(params string[] extraOptions)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

            var filteredGuildUsers = this._guildService.FilterGuildUsersAsync(guild);

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
                                 $"Please run `{prfx}index` to index this server.\n" +
                                 $"Note that this can take some time on large servers.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.LastIndexed < DateTime.UtcNow.AddDays(-100))
            {
                await ReplyAsync("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                 $"Please run `{prfx}index` to re-index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var serverTrackSettings = new GuildRankingSettings
            {
                ChartTimePeriod = ChartTimePeriod.Weekly,
                OrderType = OrderType.Listeners
            };

            serverTrackSettings = SettingService.SetGuildRankingSettings(serverTrackSettings, extraOptions);

            try
            {
                IReadOnlyList<ListTrack> topGuildTracks;
                var users = filteredGuildUsers.Select(s => s.User).ToList();
                if (serverTrackSettings.ChartTimePeriod == ChartTimePeriod.AllTime)
                {
                    topGuildTracks = await this._whoKnowsTrackService.GetTopTracksForGuild(users, serverTrackSettings.OrderType);
                    this._embed.WithTitle($"Top alltime tracks in {this.Context.Guild.Name}");
                }
                else
                {
                    topGuildTracks = await this._whoKnowsPlayService.GetTopWeekTracksForGuild(users, serverTrackSettings.OrderType);
                    this._embed.WithTitle($"Top weekly tracks in {this.Context.Guild.Name}");
                }

                var description = "";
                var footer = "";

                if (serverTrackSettings.OrderType == OrderType.Listeners)
                {
                    footer += "Listeners / Plays - Ordered by listeners\n";
                }
                else
                {
                    footer += "Listeners / Plays  - Ordered by plays\n";
                }

                foreach (var track in topGuildTracks)
                {
                    description += $"`{track.ListenerCount}` / `{track.Playcount}` | **{track.TrackName}** by **{track.ArtistName}**\n";
                }

                this._embed.WithDescription(description);

                var rnd = new Random();
                var randomHintNumber = rnd.Next(0, 5);
                if (randomHintNumber == 1)
                {
                    footer += $"View specific album listeners with {prfx}whoknowstrack\n";
                }
                else if (randomHintNumber == 2)
                {
                    footer += $"Available time periods: alltime and weekly\n";
                }
                else if (randomHintNumber == 3)
                {
                    footer += $"Available sorting options: plays and listeners\n";
                }
                if (guild.LastIndexed < DateTime.UtcNow.AddDays(-7) && randomHintNumber == 4)
                {
                    footer += $"Missing members? Update with {prfx}index\n";
                }

                if (guild.GuildUsers.Count > filteredGuildUsers.Count)
                {
                    var filteredAmount = guild.GuildUsers.Count - filteredGuildUsers.Count;
                    footer += $"{filteredAmount} inactive/blocked users filtered";
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

        private async Task<TrackInfoLfm> SearchTrack(string trackValues, string lastFmUserName, string sessionKey = null, string otherUserUsername = null)
        {
            string searchValue;
            if (!string.IsNullOrWhiteSpace(trackValues) && trackValues.Length != 0)
            {
                searchValue = trackValues;

                if (trackValues.Contains(" | "))
                {
                    var trackInfo = await this._lastFmService.GetTrackInfoAsync(searchValue.Split(" | ")[1], searchValue.Split(" | ")[0],
                        lastFmUserName);
                    return trackInfo;
                }
            }
            else
            {
                var recentScrobbles = await this._lastFmService.GetRecentTracksAsync(lastFmUserName, 1, useCache: true, sessionKey: sessionKey);

                if (await ErrorService.RecentScrobbleCallFailedReply(recentScrobbles, lastFmUserName, this.Context))
                {
                    return null;
                }

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var trackResult = recentScrobbles.Content.RecentTracks[0];
                var trackInfo = await this._lastFmService.GetTrackInfoAsync(trackResult.TrackName, trackResult.ArtistName,
                    lastFmUserName);

                if (trackInfo == null)
                {
                    this._embed.WithDescription($"Last.fm did not return a result for **{trackResult.TrackName}** by **{trackResult.ArtistName}**.\n" +
                                                $"This usually happens on recently released tracks. Please try again later.");

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return null;
                }

                return trackInfo;
            }

            var result = await this._lastFmService.SearchTrackAsync(searchValue);
            if (result.Success && result.Content.Any())
            {
                var track = result.Content[0];

                if (otherUserUsername != null)
                {
                    lastFmUserName = otherUserUsername;
                }

                var trackInfo = await this._lastFmService.GetTrackInfoAsync(track.Name, track.ArtistName,
                    lastFmUserName);
                return trackInfo;
            }

            if (result.Success)
            {
                this._embed.WithDescription($"Track could not be found, please check your search values and try again.");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return null;
            }

            this._embed.WithDescription($"Last.fm returned an error: {result.Status}");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed(CommandResponse.Error);
            return null;
        }
    }
}
