using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Humanizer;
using Microsoft.Extensions.Options;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.TextCommands.LastFM;

public class GenreCommands : BaseCommandModule
{
    private readonly IPrefixService _prefixService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GenreService _genreService;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly GenreBuilders _genreBuilders;

    private InteractiveService Interactivity { get; }

    public GenreCommands(
        IPrefixService prefixService,
        IOptions<BotSettings> botSettings,
        UserService userService,
        SettingService settingService,
        LastFmRepository lastFmRepository,
        InteractiveService interactivity,
        GenreService genreService,
        ArtistsService artistsService,
        GuildService guildService,
        IIndexService indexService,
        GenreBuilders genreBuilders) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._userService = userService;
        this._settingService = settingService;
        this._lastFmRepository = lastFmRepository;
        this.Interactivity = interactivity;
        this._genreService = genreService;
        this._artistsService = artistsService;
        this._guildService = guildService;
        this._indexService = indexService;
        this._genreBuilders = genreBuilders;
    }

    [Command("topgenres", RunMode = RunMode.Async)]
    [Summary("Shows a list of your or someone else's top genres over a certain time period.")]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [Examples("tg", "topgenres", "tg a lfm:fm-bot", "topgenres weekly @user")]
    [Alias("gl", "tg", "genrelist", "genres", "top genres", "genreslist")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    public async Task TopGenresAsync([Remainder] string extraOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        _ = this.Context.Channel.TriggerTypingAsync();

        try
        {
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            var topListSettings = SettingService.SetTopListSettings(extraOptions);

            userSettings.RegisteredLastFm ??= await this._indexService.AddUserRegisteredLfmDate(userSettings.UserId);
            var timeSettings = SettingService.GetTimePeriod(extraOptions, registeredLastFm: userSettings.RegisteredLastFm);

            var response = await this._genreBuilders.GetTopGenres(new ContextModel(this.Context, prfx, contextUser),
                userSettings, timeSettings, topListSettings);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("genre", RunMode = RunMode.Async)]
    [Summary("Shows genre information for an artist, or top artist for a specific genre")]
    [Examples("genre", "genres hip hop, electronic", "g", "genre Indie Soul", "genre The Beatles")]
    [Alias("genreinfo", "genres", "gi", "g")]
    [UsernameSetRequired]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Genres)]
    [GuildOnly]
    public async Task GenreInfoAsync([Remainder] string genreOptions = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._genreBuilders.GenreAsync(new ContextModel(this.Context, prfx, contextUser), genreOptions, guild);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
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

            var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild?.Id);

            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var guildTopUserArtists = await this._genreService.GetTopUserArtistsForGuildAsync(guild.GuildId, genre);
            var usersWithGenre = await this._genreService.GetUsersWithGenreForUserArtists(guildTopUserArtists, guildUsers);
                
            var discordGuildUser = await this.Context.Guild.GetUserAsync(user.DiscordUserId);
            var currentUser = await this._indexService.GetOrAddUserToGuild(guildUsers, guild, discordGuildUser, user);
            await this._indexService.UpdateGuildUser(discordGuildUser, currentUser.UserId, guild);

            var (filterStats, filteredUsersWithGenre) = WhoKnowsService.FilterWhoKnowsObjectsAsync(usersWithGenre, guild);

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
            if (rnd.Next(0, 10) == 1 && lastIndex < DateTime.UtcNow.AddDays(-180))
            {
                footer += $"\nMissing members? Update with {prfx}refreshmembers";
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

            if (filterStats.FullDescription != null)
            {
                footer += $"\n{filterStats.FullDescription}";
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
                await this.Context.HandleCommandException(e, sendReply: false);
                await ReplyAsync("Error while replying: The bot is missing permissions.\n" +
                                 "Make sure it has permission to 'Embed links' and 'Attach Images'");
            }
            else
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("servergenres", RunMode = RunMode.Async)]
    [Summary("Top genres for your server")]
    [Options("Time periods: `weekly`, `monthly` and `alltime`", "Order options: `plays` and `listeners`")]
    [Examples("sg", "sg a p", "servergenres", "servergenres alltime", "servergenres listeners weekly")]
    [Alias("sg", "sgenres", "serverg", "server genre", "servergenre", "server genres")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Genres)]
    public async Task GuildGenresAsync([Remainder] string extraOptions = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        _ = this.Context.Channel.TriggerTypingAsync();

        var guildListSettings = new GuildRankingSettings
        {
            ChartTimePeriod = TimePeriod.Weekly,
            TimeDescription = "weekly",
            OrderType = OrderType.Listeners,
            AmountOfDays = 7,
            NewSearchValue = extraOptions
        };

        guildListSettings = SettingService.SetGuildRankingSettings(guildListSettings, extraOptions);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, guildListSettings.ChartTimePeriod, cachedOrAllTimeOnly: true);

        if (timeSettings.UsePlays || timeSettings.TimePeriod is TimePeriod.AllTime or TimePeriod.Monthly or TimePeriod.Weekly)
        {
            guildListSettings = SettingService.TimeSettingsToGuildRankingSettings(guildListSettings, timeSettings);
        }

        try
        {
            var response = await this._genreBuilders.GetGuildGenres(new ContextModel(this.Context, prfx), guild, guildListSettings);
            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
