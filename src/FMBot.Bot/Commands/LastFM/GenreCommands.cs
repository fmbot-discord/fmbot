using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
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
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using Humanizer;
using Microsoft.Extensions.Options;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.Commands.LastFM
{
    public class GenreCommands : BaseCommandModule
    {
        private readonly IPrefixService _prefixService;
        private readonly UserService _userService;
        private readonly SettingService _settingService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly PlayService _playService;
        private readonly GenreService _genreService;
        private readonly ArtistsService _artistsService;
        private readonly SpotifyService _spotifyService;
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly WhoKnowsArtistService _whoKnowsArtistService;

        private InteractiveService Interactivity { get; }

        public GenreCommands(
            IPrefixService prefixService,
            IOptions<BotSettings> botSettings,
            UserService userService,
            SettingService settingService,
            LastFmRepository lastFmRepository,
            PlayService playService,
            InteractiveService interactivity,
            GenreService genreService,
            ArtistsService artistsService,
            SpotifyService spotifyService,
            GuildService guildService,
            IIndexService indexService,
            WhoKnowsArtistService whoKnowsArtistService) : base(botSettings)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._settingService = settingService;
            this._lastFmRepository = lastFmRepository;
            this._playService = playService;
            this.Interactivity = interactivity;
            this._genreService = genreService;
            this._artistsService = artistsService;
            this._spotifyService = spotifyService;
            this._guildService = guildService;
            this._indexService = indexService;
            this._whoKnowsArtistService = whoKnowsArtistService;
        }

        [Command("topgenres", RunMode = RunMode.Async)]
        [Summary("Shows a list of your or someone else their top genres over a certain time period.")]
        [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
        [Examples("tg", "topgenres", "tg a lfm:fm-bot", "topgenres weekly @user")]
        [Alias("gl", "tg", "genrelist", "genres", "top genres", "genreslist")]
        [UsernameSetRequired]
        [SupportsPagination]
        [CommandCategories(CommandCategory.Genres)]
        public async Task TopGenresAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
                var topListSettings = SettingService.SetTopListSettings(extraOptions);
                userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
                var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);

                var pages = new List<PageBuilder>();

                string userTitle;
                if (!userSettings.DifferentUser)
                {
                    this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                    userTitle = await this._userService.GetUserTitleAsync(this.Context);
                }
                else
                {
                    userTitle =
                        $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
                }

                this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist genres for {userTitle}");
                this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");

                Response<TopArtistList> artists;
                var previousTopArtists = new List<TopArtist>();

                if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
                {
                    artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                        timeSettings, 1000, userSessionKey: userSettings.SessionKeyLastFm);

                    if (!artists.Success || artists.Content == null)
                    {
                        this._embed.ErrorResponse(artists.Error, artists.Message, this.Context);
                        this.Context.LogCommandUsed(CommandResponse.LastFmError);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }
                }
                else if (timeSettings.TimePeriod == TimePeriod.AllTime)
                {
                    artists = new Response<TopArtistList>
                    {
                        Content = new TopArtistList
                        {
                            TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                        }
                    };
                }
                else
                {
                    artists = new Response<TopArtistList>
                    {
                        Content = await this._playService.GetTopArtists(userSettings.UserId,
                            timeSettings.PlayDays.GetValueOrDefault())
                    };
                }

                if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue && timeSettings.BillboardEndDateTime.HasValue)
                {
                    var previousArtistsCall = await this._lastFmRepository
                        .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm, timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200, userSettings.SessionKeyLastFm);

                    if (previousArtistsCall.Success)
                    {
                        previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
                    }
                }

                if (artists.Content.TopArtists.Count < 10)
                {
                    this._embed.WithDescription("Sorry, you don't have enough top artists yet to use this command.\n\n" +
                                                "Please try again later.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                var genres = await this._genreService.GetTopGenresForTopArtists(artists.Content.TopArtists);
                var previousTopGenres = await this._genreService.GetTopGenresForTopArtists(previousTopArtists);

                var genrePages = genres.ChunkBy(topListSettings.ExtraLarge ? Constants.DefaultExtraLargePageSize : Constants.DefaultPageSize);

                var counter = 1;
                var pageCounter = 1;
                var rnd = new Random().Next(0, 4);

                foreach (var genrePage in genrePages)
                {
                    var genrePageString = new StringBuilder();
                    foreach (var genre in genrePage)
                    {
                        var name = $"**{genre.GenreName.Transform(To.TitleCase)}** ({genre.UserPlaycount} {StringExtensions.GetPlaysString(genre.UserPlaycount)})";

                        if (topListSettings.Billboard && previousTopGenres.Any())
                        {
                            var previousTopGenre = previousTopGenres.FirstOrDefault(f => f.GenreName == genre.GenreName);
                            int? previousPosition = previousTopGenre == null ? null : previousTopGenres.IndexOf(previousTopGenre);

                            genrePageString.AppendLine(StringService.GetBillboardLine(name, counter - 1, previousPosition).Text);
                        }
                        else
                        {
                            genrePageString.Append($"{counter}. ");
                            genrePageString.AppendLine(name);
                        }

                        counter++;
                    }

                    var footer = new StringBuilder();
                    footer.AppendLine("Genre source: Spotify");
                    footer.AppendLine($"Page {pageCounter}/{genrePages.Count} - {genres.Count} total genres");

                    if (topListSettings.Billboard)
                    {
                        footer.Append(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
                    }

                    if (rnd == 1 && !topListSettings.Billboard)
                    {
                        footer.AppendLine();
                        footer.Append("View this list as a billboard by adding 'billboard' or 'bb'");
                    }

                    pages.Add(new PageBuilder()
                        .WithDescription(genrePageString.ToString())
                        .WithAuthor(this._embedAuthor)
                        .WithFooter(footer.ToString()));
                    pageCounter++;
                }

                var paginator = StringService.BuildStaticPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator,
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show genre info due to an internal error. Please contact .fmbot staff.");
            }
        }

        [Command("genre", RunMode = RunMode.Async)]
        [Summary("Shows your top artists for a specific genre")]
        [Examples("genre", "genres hip hop, electronic", "g", "genre Indie Soul")]
        [Alias("genreinfo", "genres", "gi", "g")]
        [UsernameSetRequired]
        [SupportsPagination]
        [CommandCategories(CommandCategory.Genres)]
        public async Task GenreInfoAsync([Remainder] string genreOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                var genres = new List<string>();
                if (string.IsNullOrWhiteSpace(genreOptions))
                {
                    var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(contextUser.UserNameLastFM, 1, true, contextUser.SessionKeyLastFm);

                    if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, contextUser.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    var artistName = recentScrobbles.Content.RecentTracks.First().ArtistName;

                    var foundGenres = await this._genreService.GetGenresForArtist(artistName);

                    if (foundGenres == null)
                    {
                        var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistName, contextUser.UserNameLastFM);
                        if (artistCall.Success)
                        {
                            var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistCall.Content);

                            if (cachedArtist.ArtistGenres != null && cachedArtist.ArtistGenres.Any())
                            {
                                genres.AddRange(cachedArtist.ArtistGenres.Select(s => s.Name));
                            }
                        }
                    }
                    else
                    {
                        genres.AddRange(foundGenres);
                    }

                    if (genres.Any())
                    {
                        var artist = await this._artistsService.GetArtistFromDatabase(artistName);

                        this._embed.WithTitle($"Genre info for '{artistName}'");

                        var genreDescription = new StringBuilder();
                        foreach (var artistGenre in artist.ArtistGenres)
                        {
                            genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");
                        }

                        if (artist?.SpotifyImageUrl != null)
                        {
                            this._embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                        }

                        this._embed.WithDescription(genreDescription.ToString());

                        this._embed.WithFooter($"Genre source: Spotify\n" +
                                               $"Add a genre to this command to see top artists");

                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        return;
                    }
                }
                else
                {
                    var foundGenre = await this._genreService.GetValidGenre(genreOptions);
                    if (foundGenre == null)
                    {
                        this._embed.WithDescription(
                            "Sorry, Spotify does not have the genre you're searching for.");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return;
                    }

                    genres = new List<string> { foundGenre };
                }

                if (!genres.Any())
                {
                    var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
                    this._embed.WithDescription(
                        "Sorry, we don't have any registered genres for the artist you're currently listening to.\n\n" +
                        $"Please try again later or manually enter a genre (example: `{prfx}genre hip hop`)");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var topArtists = await this._artistsService.GetUserAllTimeTopArtists(contextUser.UserId, true);
                if (topArtists.Count < 100)
                {
                    this._embed.WithDescription("Sorry, you don't have enough top artists yet to use this command.\n\n" +
                                                "Please try again later.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                var genresWithArtists = await this._genreService.GetArtistsForGenres(genres, topArtists);

                if (!genresWithArtists.Any())
                {
                    this._embed.WithDescription("Sorry, we couldn't find any top artists for your selected genres.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);

                var pages = new List<PageBuilder>();

                var genre = genresWithArtists.First();

                if (!genre.Artists.Any())
                {
                    var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
                    this._embed.WithDescription(
                        "Sorry, we don't have any registered artists for you for the genre you're searching for.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                this._embedAuthor.WithName($"Top '{genre.GenreName.Transform(To.TitleCase)}' artists for {userTitle}");

                var genrePages = genre.Artists.ChunkBy(10);

                var counter = 1;
                var pageCounter = 1;
                foreach (var genrePage in genrePages)
                {
                    var genrePageString = new StringBuilder();
                    foreach (var genreArtist in genrePage)
                    {
                        genrePageString.AppendLine($"{counter}. **{genreArtist.ArtistName}** ({genreArtist.UserPlaycount} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)})");
                        counter++;
                    }

                    var footer = $"Genre source: Spotify\n" +
                                 $"Page {pageCounter}/{genrePages.Count} - {genre.Artists.Count} total artists";

                    pages.Add(new PageBuilder()
                        .WithDescription(genrePageString.ToString())
                        .WithAuthor(this._embedAuthor)
                        .WithFooter(footer));
                    pageCounter++;
                }

                var paginator = StringService.BuildStaticPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator,
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show genre info due to an internal error. Please contact .fmbot staff.");
            }
        }


        [Command("whoknowsgenre", RunMode = RunMode.Async)]
        [Summary("Shows what other users listen to a genre in your server")]
        [Examples("wg", "wkg hip hop", "whoknowsgenre", "whoknowsgenre techno")]
        [Alias("wg", "wkg", "whoknows genre")]
        [UsernameSetRequired]
        [GuildOnly]
        [RequiresIndex]
        [CommandCategories(CommandCategory.Genres, CommandCategory.WhoKnows)]
        public async Task WhoKnowsGenreAsync([Remainder] string genreValues = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                if (string.IsNullOrWhiteSpace(genreValues))
                {
                    var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(contextUser.UserNameLastFM, 1, true, contextUser.SessionKeyLastFm);

                    if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, contextUser.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    var artistName = recentScrobbles.Content.RecentTracks.First().ArtistName;

                    var foundGenres = await this._genreService.GetGenresForArtist(artistName);

                    if (foundGenres != null && foundGenres.Any())
                    {
                        var artist = await this._artistsService.GetArtistFromDatabase(artistName);

                        this._embed.WithTitle($"Genre info for '{artistName}'");

                        var genreDescription = new StringBuilder();
                        foreach (var artistGenre in artist.ArtistGenres)
                        {
                            genreDescription.AppendLine($"- **{artistGenre.Name.Transform(To.TitleCase)}**");
                        }

                        if (artist?.SpotifyImageUrl != null)
                        {
                            this._embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                        }

                        this._embed.WithDescription(genreDescription.ToString());

                        this._embed.WithFooter($"Genre source: Spotify\n" +
                                               $"Add a genre to this command to WhoKnows this genre");

                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        return;
                    }
                    else
                    {
                        this._embed.WithDescription(
                            "Sorry, we don't have any stored genres for this artist.");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return;
                    }
                }

                var genre = await this._genreService.GetValidGenre(genreValues);

                if (genre == null)
                {
                    this._embed.WithDescription(
                        "Sorry, Spotify does not have the genre you're searching for.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var guildTask = this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

                var user = await this._userService.GetUserSettingsAsync(this.Context.User);

                var guild = await guildTask;

                var currentUser = await this._indexService.GetOrAddUserToGuild(guild, await this.Context.Guild.GetUserAsync(user.DiscordUserId), user);

                if (!guild.GuildUsers.Select(s => s.UserId).Contains(user.UserId))
                {
                    guild.GuildUsers.Add(currentUser);
                }

                await this._indexService.UpdateGuildUser(await this.Context.Guild.GetUserAsync(user.DiscordUserId), currentUser.UserId, guild);

                var guildTopUserArtists = await this._genreService.GetTopUserArtistsForGuildAsync(guild.GuildId, genre);
                var usersWithGenre =
                    await this._genreService.GetUsersWithGenreForUserArtists(guildTopUserArtists, guild.GuildUsers);

                var filteredUsersWithGenre = WhoKnowsService.FilterGuildUsersAsync(usersWithGenre, guild);

                var serverUsers = WhoKnowsService.WhoKnowsListToString(filteredUsersWithGenre, user.UserId, PrivacyLevel.Server);
                if (filteredUsersWithGenre.Count == 0)
                {
                    serverUsers = "Nobody in this server (not even you) has listened to this genre.";
                }

                this._embed.WithDescription(serverUsers);

                var footer = "";

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);
                footer += $"\nWhoKnows genre requested by {userTitle}";

                var rnd = new Random();
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);
                if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-50))
                {
                    footer += $"\nMissing members? Update with {prfx}index";
                }

                if (filteredUsersWithGenre.Any() && filteredUsersWithGenre.Count > 1)
                {
                    var serverListeners = filteredUsersWithGenre.Count;
                    var serverPlaycount = filteredUsersWithGenre.Sum(a => a.Playcount);
                    var avgServerPlaycount = filteredUsersWithGenre.Average(a => a.Playcount);

                    footer += $"\n{serverListeners} {StringExtensions.GetListenersString(serverListeners)} - ";
                    footer += $"{serverPlaycount} total {StringExtensions.GetPlaysString(serverPlaycount)} - ";
                    footer += $"{(int)avgServerPlaycount} avg {StringExtensions.GetPlaysString((int)avgServerPlaycount)}";
                }

                if (usersWithGenre.Count > filteredUsersWithGenre.Count && !guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    var filteredAmount = usersWithGenre.Count - filteredUsersWithGenre.Count;
                    footer += $"\n{filteredAmount} inactive/blocked users filtered";
                }
                if (guild.WhoKnowsWhitelistRoleId.HasValue)
                {
                    footer += $"\nUsers with WhoKnows whitelisted role only";
                }

                this._embed.WithTitle($"{genre.Transform(To.TitleCase)} in {this.Context.Guild.Name}");
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
                    await ReplyAsync("Something went wrong while using whoknows.");
                }
            }
        }
    }
}
