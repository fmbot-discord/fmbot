using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Hangfire;
using Hangfire.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.TextCommands;

[Name("Admin settings")]
[Summary(".fmbot Admins Only")]
[ExcludeFromHelp]
public class AdminCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly GuildService _guildService;
    private readonly TimerService _timer;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly FeaturedService _featuredService;
    private readonly IndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly StaticBuilders _staticBuilders;
    private readonly AlbumService _albumService;
    private readonly ArtistsService _artistsService;
    private readonly AliasService _aliasService;
    private readonly WhoKnowsFilterService _whoKnowsFilterService;
    private readonly PlayService _playService;
    private readonly DiscordShardedClient _client;
    private readonly WebhookService _webhookService;
    private readonly TrackService _trackService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private InteractiveService Interactivity { get; }

    public AdminCommands(
        AdminService adminService,
        CensorService censorService,
        GuildService guildService,
        TimerService timer,
        IDataSourceFactory dataSourceFactory,
        SupporterService supporterService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        SettingService settingService,
        FeaturedService featuredService,
        IndexService indexService,
        IPrefixService prefixService,
        StaticBuilders staticBuilders,
        InteractiveService interactivity,
        AlbumService albumService,
        ArtistsService artistsService,
        AliasService aliasService,
        WhoKnowsFilterService whoKnowsFilterService,
        PlayService playService, DiscordShardedClient client, WebhookService webhookService, TrackService trackService,
        WhoKnowsTrackService whoKnowsTrackService, IDbContextFactory<FMBotDbContext> contextFactory) : base(botSettings)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._guildService = guildService;
        this._timer = timer;
        this._dataSourceFactory = dataSourceFactory;
        this._supporterService = supporterService;
        this._userService = userService;
        this._settingService = settingService;
        this._featuredService = featuredService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._staticBuilders = staticBuilders;
        this.Interactivity = interactivity;
        this._albumService = albumService;
        this._artistsService = artistsService;
        this._aliasService = aliasService;
        this._whoKnowsFilterService = whoKnowsFilterService;
        this._playService = playService;
        this._client = client;
        this._webhookService = webhookService;
        this._trackService = trackService;
        this._whoKnowsTrackService = whoKnowsTrackService;
        this._contextFactory = contextFactory;
    }

    //[Command("debug")]
    //[Summary("Returns user data")]
    //[Alias("dbcheck")]
    //public async Task DebugAsync(IUser user = null)
    //{
    //    var chosenUser = user ?? this.Context.Message.Author;
    //    var userSettings = await this._userService.GetFullUserAsync(chosenUser.Id);

    //    if (userSettings?.UserNameLastFM == null)
    //    {
    //        await ReplyAsync("The user's Last.fm name has not been set.");
    //        this.Context.LogCommandUsed(CommandResponse.UsernameNotSet);
    //        return;
    //    }

    //    this._embed.WithTitle($"Debug for {chosenUser.ToString()}");

    //    var description = new StringBuilder();

    //    this._embed.WithDescription(description);
    //    this._embed.WithFooter("")
    //    await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
    //    this.Context.LogCommandUsed();
    //}


    [Command("serverdebug")]
    [Summary("Returns server data")]
    [Alias("guilddebug", "debugserver", "debugguild")]
    public async Task DebugGuildAsync([Remainder] string guildId = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        guildId ??= this.Context.Guild.Id.ToString();

        if (!ulong.TryParse(guildId, out var discordGuildId))
        {
            await ReplyAsync("Enter a valid discord guild id");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var guild = await this._guildService.GetGuildAsync(discordGuildId);

        if (guild == null)
        {
            await ReplyAsync("Guild does not exist in database");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        this._embed.WithTitle($"Debug for guild with id {discordGuildId}");

        var description = "";
        foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(guild))
        {
            var name = descriptor.Name;
            var value = descriptor.GetValue(guild);

            if (value == null)
            {
                description += $"{name}: null \n";
                continue;
            }

            if (descriptor.PropertyType.Name == "String[]")
            {
                var a = (Array)descriptor.GetValue(guild);
                var arrayValue = "";
                for (var i = 0; i < a.Length; i++)
                {
                    arrayValue += $"{a.GetValue(i)} - ";
                }

                if (a.Length > 0)
                {
                    description += $"{name}: `{arrayValue}` \n";
                }
                else
                {
                    description += $"{name}: null \n";
                }
            }
            else
            {
                description += $"{name}: `{value}` \n";
            }
        }

        this._embed.WithDescription(description);

        // Add guild flags selectmenu
        var guildFlagsOptions = new SelectMenuBuilder()
            .WithPlaceholder("Select guild flags")
            .WithCustomId($"guild-flags-{guild.DiscordGuildId}")
            .WithMinValues(0)
            .WithMaxValues(Enum.GetValues<GuildFlags>().Length);

        foreach (var flag in Enum.GetValues(typeof(GuildFlags)).Cast<GuildFlags>())
        {
            if (flag == 0)
            {
                continue;
            }

            var flagName = Enum.GetName(flag);
            var isActive = guild.GuildFlags.HasValue && guild.GuildFlags.Value.HasFlag(flag);

            guildFlagsOptions.AddOption(new SelectMenuOptionBuilder(
                label: flagName,
                value: flagName,
                description: $"Toggle {flagName} flag",
                isDefault: isActive
            ));
        }

        var builder = new ComponentBuilder()
            .WithSelectMenu(guildFlagsOptions);

        await ReplyAsync("", false, this._embed.Build(), components: builder.Build()).ConfigureAwait(false);
        this.Context.LogCommandUsed();
    }

    [Command("issues")]
    [Summary("Toggles issue mode")]
    public async Task IssuesAsync([Remainder] string reason = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!PublicProperties.IssuesAtLastFm || reason != null)
            {
                PublicProperties.IssuesAtLastFm = true;
                PublicProperties.IssuesReason = reason;
                await ReplyAsync(
                    "Enabled issue mode. This adds some warning messages, changes the bot status and disables full updates.\n" +
                    $"Reason given: *\"{reason}\"*", allowedMentions: AllowedMentions.None);
            }
            else
            {
                PublicProperties.IssuesAtLastFm = false;
                PublicProperties.IssuesReason = null;
                await ReplyAsync("Disabled issue mode");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("leaveserver")]
    [Summary("Makes the bot leave a server")]
    [Alias("leaveguild")]
    public async Task LeaveGuild([Remainder] string reason = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (!ulong.TryParse(reason, out var id))
            {
                await ReplyAsync("Invalid guild ID");
                this.Context.LogCommandUsed();
                return;
            }

            var guildToLeave = await this._client.Rest.GetGuildAsync(id);
            await guildToLeave.LeaveAsync();

            await ReplyAsync("Left guild (if the bot was in there)");
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("banguild")]
    [Summary("Bans a guild and makes the bot leave the server")]
    [Alias("banserver")]
    public async Task BanGuild([Remainder] string guildId = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            if (!ulong.TryParse(guildId, out var id))
            {
                await ReplyAsync("Invalid guild ID");
                this.Context.LogCommandUsed();
                return;
            }

            // Check if guild exists in database
            var dbGuild = await this._guildService.GetGuildAsync(id);
            if (dbGuild == null)
            {
                await ReplyAsync("Guild does not exist in database");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            // Add banned flag
            var currentFlags = dbGuild.GuildFlags ?? (GuildFlags)0;
            var newFlags = currentFlags | GuildFlags.Banned;
            await this._guildService.SetGuildFlags(dbGuild.GuildId, newFlags);

            // Leave the guild
            var guildToLeave = await this._client.Rest.GetGuildAsync(id);
            if (guildToLeave != null)
            {
                await guildToLeave.LeaveAsync();
                await ReplyAsync($"Banned and left guild: {guildToLeave.Name} ({id})");
            }
            else
            {
                await ReplyAsync($"Guild banned but bot was not in the guild ({id})");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("updategwfilter")]
    [Summary("Updates gwk quality filter")]
    public async Task UpdateGlobalWhoKnowsFilter([Remainder] string _ = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync("Starting gwk quality filter update..");

            var filteredUsers = await this._whoKnowsFilterService.GetNewGlobalFilteredUsers();
            await this._whoKnowsFilterService.AddFilteredUsersToDatabase(filteredUsers);

            var description = new StringBuilder();

            description.AppendLine(
                $"Found {filteredUsers.Count(c => c.Reason == GlobalFilterReason.PlayTimeInPeriod)} users exceeding max playtime");
            description.AppendLine(
                $"Found {filteredUsers.Count(c => c.Reason == GlobalFilterReason.AmountPerPeriod)} users exceeding max amount");
            await ReplyAsync(description.ToString());

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("debuglogs")]
    [Summary("View user command logs")]
    public async Task DebugLogs([Remainder] string user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

                if (guild.SpecialGuild != true)
                {
                    await ReplyAsync("This command can only be used in special guilds.");
                    this.Context.LogCommandUsed(CommandResponse.NoPermission);
                    return;
                }

                user ??= this.Context.User.Id.ToString();

                var userToView = await this._settingService.GetDifferentUser(user);

                if (userToView == null)
                {
                    await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var logs = await this._adminService.GetRecentUserInteractions(userToView.UserId);

                var logPages = logs.Chunk(10).ToList();
                var pageCounter = 1;

                var pages = new List<PageBuilder>();
                foreach (var logPage in logPages)
                {
                    var description = new StringBuilder();

                    foreach (var log in logPage)
                    {
                        if (log.Type is UserInteractionType.SlashCommandGuild or UserInteractionType.SlashCommandUser)
                        {
                            description.Append($"/");
                        }

                        description.Append($"**{log.CommandName}** - <t:{log.Timestamp.ToUnixEpochDate()}:R>");

                        if (log.ErrorReferenceId != null)
                        {
                            description.Append($" - ❌");
                        }
                        else if (log.Response != CommandResponse.Ok)
                        {
                            description.Append($" - 🫥");
                        }
                        else
                        {
                            description.Append($" - ✅");
                        }

                        description.AppendLine();

                        if (log.Artist != null || log.Album != null || log.Track != null)
                        {
                            description.Append("*");
                            if (log.Artist != null)
                            {
                                description.Append(log.Artist);
                            }

                            if (log.Album != null)
                            {
                                description.Append($" - {log.Album}");
                            }

                            if (log.Track != null)
                            {
                                description.Append($" - {log.Track}");
                            }

                            description.Append("*");
                            description.AppendLine();
                        }

                        if (log.Type == UserInteractionType.TextCommand)
                        {
                            description.AppendLine($"*`{log.CommandContent}`*");
                        }

                        if (log.ErrorReferenceId != null)
                        {
                            description.AppendLine($"Error - Reference {log.ErrorReferenceId}");
                        }
                        else if (log.Response != CommandResponse.Ok)
                        {
                            description.AppendLine($"{Enum.GetName(log.Response)}");
                        }

                        description.AppendLine();
                    }

                    pages.Add(new PageBuilder()
                        .WithDescription(description.ToString())
                        .WithFooter($"Page {pageCounter}/{logPages.Count()} - Limited to 3 days\n" +
                                    $"{userToView.DiscordUserId} - {userToView.UserId}\n" +
                                    $"Command not intended for use in public channels")
                        .WithTitle($"User command log for {userToView.UserNameLastFM}"));

                    pageCounter++;
                }

                if (!pages.Any())
                {
                    pages.Add(new PageBuilder()
                        .WithDescription(
                            "User logs yet")
                        .WithFooter($"Page {pageCounter}/{logPages.Count()}")
                        .WithTitle(
                            $"User command log for {this.Context.Guild.Name} | {this.Context.Guild.Id}"));
                }

                var paginator = StringService.BuildStaticPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator.Build(),
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("purgecache")]
    [Summary("Purges discord caches")]
    public async Task PurgeCacheAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var client = this.Context.Client as DiscordShardedClient;
            if (client == null)
            {
                await ReplyAsync("Client is null");
                return;
            }

            var currentProcess = Process.GetCurrentProcess();
            var currentMemoryUsage = currentProcess.WorkingSet64;
            var reply = new StringBuilder();

            reply.AppendLine("Purged user cache and ran garbage collector.");
            reply.AppendLine($"Memory before purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            foreach (var socketClient in client.Shards)
            {
                socketClient.PurgeUserCache();
            }

            GC.Collect();

            await Task.Delay(2000);

            currentProcess = Process.GetCurrentProcess();
            currentMemoryUsage = currentProcess.WorkingSet64;

            reply.AppendLine($"Memory after purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            await ReplyAsync(reply.ToString());

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("removeoldplays")]
    [Summary("Purges discord caches")]
    public async Task RemoveOldPlaysAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var oldUsers = await this._indexService.GetUnusedUsers();
            await ReplyAsync(
                $"Found {oldUsers.Count} users that haven't used fmbot in 3 months. I will now remove their cached scrobbles that are over a year and a half old.");

            await this._indexService.RemoveOldPlaysForUsers(oldUsers);
            await ReplyAsync(
                $"Done removing old cached scrobbles.");

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("opencollectivesupporters", RunMode = RunMode.Async)]
    [Summary("Displays all .fmbot supporters.")]
    [Alias("ocsupporters")]
    public async Task OpenCollectiveSupportersAsync([Remainder] string extraOptions = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._staticBuilders.OpenCollectiveSupportersAsync(
                    new ContextModel(this.Context, prfx, userSettings), extraOptions == "expired");

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    [Command("discordsupporters", RunMode = RunMode.Async)]
    [Summary("Displays all .fmbot supporters.")]
    [Alias("dsupporters", "dsupp", "discsupp")]
    public async Task DiscordSupportersAsync()
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        try
        {
            var response =
                await this._staticBuilders.DiscordSupportersAsync(new ContextModel(this.Context, prfx, userSettings));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("addalbum")]
    [Summary("Manage album censoring")]
    [Examples("addcensoredalbum Death Grips No Love Deep Web")]
    [Alias("addcensoredalbum", "addnsfwalbum", "checkalbum")]
    public async Task AddAlbumAsync([Remainder] string albumValues)
    {
        try
        {
            if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(Constants.FmbotStaffOnly);
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var albumSearch = await this._albumService.SearchAlbum(new ResponseModel(), this.Context.User, albumValues,
                userSettings.UserNameLastFM, referencedMessage: this.Context.Message.ReferencedMessage);
            if (albumSearch.Album == null)
            {
                await this.Context.SendResponse(this.Interactivity, albumSearch.Response);
                return;
            }

            var existingAlbum =
                await this._censorService.GetCurrentAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            if (existingAlbum == null)
            {
                if (this.Context.Message.Content[..12].Contains("nsfw"))
                {
                    await this._censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.AlbumCoverNsfw);
                    this._embed.WithDescription(
                        $"Marked `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` as NSFW.");
                }
                else if (this.Context.Message.Content[..12].Contains("censored"))
                {
                    await this._censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.AlbumCoverCensored);
                    this._embed.WithDescription(
                        $"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the censored albums.");
                }
                else
                {
                    await this._censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.None);
                    this._embed.WithDescription(
                        $"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the censored music list, however not banned anywhere.");
                }

                existingAlbum =
                    await this._censorService.GetCurrentAlbum(albumSearch.Album.AlbumName,
                        albumSearch.Album.ArtistName);
            }
            else
            {
                this._embed.WithDescription($"Showing existing album entry (no modifications made).");
            }

            var censorOptions = new SelectMenuBuilder()
                .WithPlaceholder("Select censor types")
                .WithCustomId($"admin-censor-{existingAlbum.CensoredMusicId}")
                .WithMinValues(0)
                .WithMaxValues(2);

            var censorDescription = new StringBuilder();
            foreach (var option in ((CensorType[])Enum.GetValues(typeof(CensorType))))
            {
                var name = option.GetAttribute<OptionAttribute>().Name;
                var description = option.GetAttribute<OptionAttribute>().Description;
                var value = Enum.GetName(option);

                var active = existingAlbum.CensorType.HasFlag(option);

                if ((name.ToLower().Contains("album cover") || active) && name != "None")
                {
                    censorDescription.Append(active ? "✅" : "❌");
                    censorDescription.Append(" - ");
                    censorDescription.AppendLine(name);

                    censorOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
                }
            }

            var builder = new ComponentBuilder()
                .WithSelectMenu(censorOptions);

            this._embed.WithTitle("Album - Censor information");

            this._embed.AddField("Album name", existingAlbum.AlbumName);
            this._embed.AddField("Artist name", existingAlbum.ArtistName);
            this._embed.AddField("Times censored", existingAlbum.TimesCensored ?? 0);
            this._embed.AddField("Types", censorDescription.ToString());

            await ReplyAsync(embed: this._embed.Build(), components: builder.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("managealias")]
    [Summary("Manage artist alias")]
    public async Task ManageArtistAlias([Remainder] string alias)
    {
        try
        {
            if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(Constants.FmbotStaffOnly);
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var artistAlias = await this._aliasService.GetArtistAlias(alias);
            if (artistAlias == null)
            {
                await ReplyAsync("Artist alias not found");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var aliasOptions = new SelectMenuBuilder()
                .WithPlaceholder("Select alias options")
                .WithCustomId($"artist-alias-{artistAlias.Id}")
                .WithMinValues(0)
                .WithMaxValues(5);

            var censorDescription = new StringBuilder();
            foreach (var option in ((AliasOption[])Enum.GetValues(typeof(AliasOption))))
            {
                var name = option.GetAttribute<OptionAttribute>().Name;
                var description = option.GetAttribute<OptionAttribute>().Description;
                var value = Enum.GetName(option);

                var active = artistAlias.Options.HasFlag(option);

                censorDescription.Append(active ? "✅" : "❌");
                censorDescription.Append(" - ");
                censorDescription.AppendLine(name);

                aliasOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
            }

            var builder = new ComponentBuilder()
                .WithSelectMenu(aliasOptions);

            this._embed.WithTitle("Artist alias - Option information");

            var artist = await this._artistsService.GetArtistForId(artistAlias.ArtistId);

            this._embed.AddField("Artist name", artist.Name);
            this._embed.AddField("Alias", artistAlias.Alias);
            this._embed.AddField("Types", censorDescription.ToString());

            this._embed.WithFooter($"Artist id {artistAlias.ArtistId}\n" +
                                   "Case insensitive\n" +
                                   "Aliases are cached for 5 minutes");

            await ReplyAsync(embed: this._embed.Build(), components: builder.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("addartist")]
    [Summary("Manage artist censoring")]
    [Examples("addcensoredartist Last Days of Humanity")]
    [Alias("addcensoredartist", "addnsfwartist", "addfeaturedban", "checkartist")]
    public async Task AddArtistAsync([Remainder] string artist)
    {
        try
        {
            if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(Constants.FmbotStaffOnly);
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(artist))
            {
                await ReplyAsync("Enter a correct artist to manage\n" +
                                 "Example: `.addartist \"Last Days of Humanity\"");
                return;
            }

            artist = artist.Replace("\"", "");

            var existingArtist = await this._censorService.GetCurrentArtist(artist);
            if (existingArtist == null)
            {
                if (this.Context.Message.Content[..12].Contains("nsfw"))
                {
                    await this._censorService.AddArtist(artist, CensorType.ArtistAlbumsNsfw);
                    this._embed.WithDescription($"Added `{artist}` to the album nsfw marked artists.");
                }
                else if (this.Context.Message.Content[..12].Contains("censored"))
                {
                    await this._censorService.AddArtist(artist, CensorType.ArtistAlbumsCensored);
                    this._embed.WithDescription($"Added `{artist}` to the album censored artists.");
                }
                else if (this.Context.Message.Content[..12].Contains("featured"))
                {
                    await this._censorService.AddArtist(artist, CensorType.ArtistFeaturedBan);
                    this._embed.WithDescription($"Added `{artist}` to the list of featured banned artists.");
                }
                else
                {
                    await this._censorService.AddArtist(artist, CensorType.None);
                    this._embed.WithDescription(
                        $"Added `{artist}` to the censored music list, however not banned anywhere.");
                }

                existingArtist = await this._censorService.GetCurrentArtist(artist);
            }
            else
            {
                this._embed.WithDescription($"Showing existing artist entry (no modifications made).");
            }

            var censorOptions = new SelectMenuBuilder()
                .WithPlaceholder("Select censor types")
                .WithCustomId($"admin-censor-{existingArtist.CensoredMusicId}")
                .WithMinValues(0)
                .WithMaxValues(5);

            var censorDescription = new StringBuilder();
            foreach (var option in ((CensorType[])Enum.GetValues(typeof(CensorType))))
            {
                var name = option.GetAttribute<OptionAttribute>().Name;
                var description = option.GetAttribute<OptionAttribute>().Description;
                var value = Enum.GetName(option);

                var active = existingArtist.CensorType.HasFlag(option);

                if ((name.ToLower().Contains("artist") || active) && name != "None")
                {
                    censorDescription.Append(active ? "✅" : "❌");
                    censorDescription.Append(" - ");
                    censorDescription.AppendLine(name);

                    censorOptions.AddOption(new SelectMenuOptionBuilder(name, value, description, isDefault: active));
                }
            }

            var builder = new ComponentBuilder()
                .WithSelectMenu(censorOptions);

            this._embed.WithTitle("Artist - Censor information");

            this._embed.AddField("Name", existingArtist.ArtistName);
            this._embed.AddField("Times censored", existingArtist.TimesCensored ?? 0);
            this._embed.AddField("Types", censorDescription.ToString());

            await ReplyAsync(embed: this._embed.Build(), components: builder.Build());
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("checkbotted")]
    [Alias("checkbotteduser")]
    [Summary("Checks some stats for a user and if they're banned from global whoknows")]
    public async Task CheckBottedUserAsync(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await ReplyAsync("Enter an username to check\n" +
                                 "Example: `.fmcheckbotted Kefkef123`");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var targetedUser = await this._settingService.GetUser(user, contextUser, this.Context);
            var targetedDate = (DateTime?)null;

            if (targetedUser.DifferentUser)
            {
                user = targetedUser.UserNameLastFm;
                targetedDate = targetedUser.RegisteredLastFm;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user, targetedDate);
            var filteredUser = await this._adminService.GetFilteredUserAsync(user, targetedDate);
            var isBannedSomewhere = false;

            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(user);

            this._embed.WithTitle($"Botted check for Last.fm '{user}'");

            if (userInfo == null)
            {
                this._embed.WithDescription($"Not found on Last.fm - [User]({Constants.LastFMUserUrl}{user})");
            }
            else
            {
                this._embed.WithDescription($"[Profile]({Constants.LastFMUserUrl}{user}) - " +
                                            $"[Library]({Constants.LastFMUserUrl}{user}/library) - " +
                                            $"[Last.week]({Constants.LastFMUserUrl}{user}/listening-report) - " +
                                            $"[Last.year]({Constants.LastFMUserUrl}{user}/listening-report/year)");

                var dateAgo = DateTime.UtcNow.AddDays(-365);
                var timeFrom = ((DateTimeOffset)dateAgo).ToUnixTimeSeconds();

                var count = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(user, timeFrom);

                var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
                var totalDays = (DateTime.UtcNow - age).TotalDays;

                var avgPerDay = count / totalDays;
                this._embed.AddField("Avg scrobbles / day in last year", Math.Round(avgPerDay.GetValueOrDefault(0), 1));
            }

            this._embed.AddField("Banned from GlobalWhoKnows",
                bottedUser == null ? "No" : bottedUser.BanActive ? "Yes" : "No, but has been banned before");
            if (bottedUser?.BanActive == true)
            {
                isBannedSomewhere = true;
            }

            if (bottedUser != null)
            {
                this._embed.AddField("Reason / additional notes", bottedUser.Notes ?? "*No reason/notes*");
                if (bottedUser.LastFmRegistered != null)
                {
                    this._embed.AddField("Last.fm join date banned",
                        "Yes (This means that the gwk ban will survive username changes)");
                }
            }


            if (filteredUser != null)
            {
                var startDate = filteredUser.OccurrenceEnd ?? filteredUser.Created;

                var length = filteredUser.MonthLength ?? 3;

                if (startDate > DateTime.UtcNow.AddMonths(-length))
                {
                    this._embed.AddField("Globally filtered", "Yes");
                    this._embed.AddField("Filter reason", WhoKnowsFilterService.FilteredUserReason(filteredUser));

                    var specifiedDateTime = DateTime.SpecifyKind(startDate.AddMonths(length), DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    this._embed.AddField("Filter expires", $"<t:{dateValue}:R> - <t:{dateValue}:F>");

                    if (filteredUser.MonthLength is > 3)
                    {
                        this._embed.AddField("Repeat offender",
                            $"Yes, has been filtered at least 3 times with 4 weeks in between each filter. This filter plus all future filters will last 6 months.");
                    }

                    isBannedSomewhere = true;
                }
                else
                {
                    this._embed.AddField("Globally filtered", "No, but was filtered in the past");
                    this._embed.AddField("Expired filter reason",
                        WhoKnowsFilterService.FilteredUserReason(filteredUser));
                }
            }
            else
            {
                this._embed.AddField("Globally filtered", "No");
            }

            ComponentBuilder components = null;
            if (filteredUser != null && bottedUser == null)
            {
                components = new ComponentBuilder().WithButton($"Convert to ban",
                    $"gwk-filtered-user-to-ban-{filteredUser.GlobalFilteredUserId}", style: ButtonStyle.Secondary);
            }

            this._embed.WithFooter("Command not intended for use in public channels");
            this._embed.WithColor(isBannedSomewhere
                ? DiscordConstants.WarningColorOrange
                : DiscordConstants.SuccessColorGreen);

            await ReplyAsync("", false, this._embed.Build(), components: components?.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("getusers")]
    [Examples("getusers frikandel_")]
    [GuildOnly]
    public async Task GetUsersForLastfmUserNameAsync(string userString = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild.SpecialGuild != true)
            {
                await ReplyAsync("This command can only be used in special guilds.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(userString))
            {
                await ReplyAsync("Enter a Last.fm username to get the accounts for.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var otherUser = await this._settingService.GetDifferentUser(userString);
            if (otherUser != null && otherUser.UserNameLastFM.ToLower() != userString.ToLower())
            {
                userString = otherUser.UserNameLastFM;
            }

            var users = await this._adminService.GetUsersWithLfmUsernameAsync(userString);

            this._embed.WithTitle($"All .fmbot users with Last.fm username {userString}");
            this._embed.WithUrl($"https://www.last.fm/user/{userString}");

            foreach (var user in users.OrderByDescending(o => o.LastUsed))
            {
                var userDescription = new StringBuilder();

                if (user.SessionKeyLastFm != null)
                {
                    userDescription.AppendLine($"Authorized");
                }

                userDescription.AppendLine($"`{user.DiscordUserId}`");
                userDescription.AppendLine($"<@{user.DiscordUserId}>");

                if (user.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(user.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    userDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                this._embed.AddField($"{user.UserId} {user.UserType.UserTypeToIcon()}", userDescription.ToString());
            }

            this._embed.WithFooter("Command not intended for use in public channels");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addbotted")]
    [Alias("addbotteduser")]
    [Examples(".addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"")]
    public async Task AddBottedUserAsync(string user = null, string reason = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(reason))
            {
                await ReplyAsync("Enter an username and reason to remove someone from gwk banlist\n" +
                                 "Example: `.addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user);

            var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(user);

            DateTimeOffset? age = null;
            if (userInfo != null && userInfo.Subscriber)
            {
                age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
            }

            if (bottedUser == null)
            {
                if (!await this._adminService.AddBottedUserAsync(user, reason, age?.DateTime))
                {
                    await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                }
                else
                {
                    await ReplyAsync(
                        $"User {user} has been banned from GlobalWhoKnows with reason `{reason}`" +
                        (age.HasValue ? " (+ join date so username change resilient)" : ""),
                        allowedMentions: AllowedMentions.None);
                    this.Context.LogCommandUsed();
                }
            }
            else
            {
                if (!await this._adminService.EnableBottedUserBanAsync(user, reason, age?.DateTime))
                {
                    await ReplyAsync("Something went wrong while adding this user to the gwk banlist");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                }
                else
                {
                    await ReplyAsync(
                        $"User {user} has been banned from GlobalWhoKnows with reason `{reason}`" +
                        (age.HasValue ? " (+ join date so username change resilient)" : ""),
                        allowedMentions: AllowedMentions.None);
                    this.Context.LogCommandUsed();
                }
            }
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("removebotted")]
    [Alias("removebotteduser")]
    [Examples("removebotteduser \"Kefkef123\"")]
    public async Task RemoveBottedUserAsync(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await ReplyAsync(
                    "Enter an username to remove from the gwk banlist. This will flag their ban as `false`.\n" +
                    "Example: `.removebotteduser \"Kefkef123\"`");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var bottedUser = await this._adminService.GetBottedUserAsync(user);
            if (bottedUser == null)
            {
                await ReplyAsync("The specified user has never been banned from GlobalWhoKnows");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (!bottedUser.BanActive)
            {
                await ReplyAsync("User is in banned user list, but their ban was already inactive");
                return;
            }

            if (!await this._adminService.DisableBottedUserBanAsync(user))
            {
                await ReplyAsync("The specified user has not been banned from GlobalWhoKnows");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            else
            {
                await ReplyAsync($"User {user} has been unbanned from GlobalWhoKnows");
                this.Context.LogCommandUsed();
                return;
            }
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addsupporter")]
    public async Task AddSupporterAsync(string user = null, string openCollectiveId = null, string sendDm = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            const string formatError = "Make sure to follow the correct format when adding a supporter\n" +
                                       "`.addsupporter \"discordUserId\" \"open-collective-id\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\"`\n\n" +
                                       "If you don't want the bot to send a thank you dm, add `\"nodm\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\" \"nodm\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(openCollectiveId) || user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();
            var userSettings = await this._userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (userSettings.UserType != UserType.User &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                await ReplyAsync("`Can only change usertype of normal users`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var openCollectiveSupporter = await this._supporterService.GetOpenCollectiveSupporter(openCollectiveId);
            if (openCollectiveSupporter == null)
            {
                await ReplyAsync("`OpenCollective user not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var existingSupporters = await this._supporterService.GetAllSupporters();
            if (existingSupporters
                    .Where(w => w.OpenCollectiveId != null)
                    .FirstOrDefault(f => f.OpenCollectiveId.ToLower() == openCollectiveId.ToLower()) != null)
            {
                await ReplyAsync("`OpenCollective account already connected to someone else`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var supporter =
                await this._supporterService.AddOpenCollectiveSupporter(userSettings.DiscordUserId,
                    openCollectiveSupporter);

            await this._supporterService.ModifyGuildRole(userSettings.DiscordUserId);

            this._embed.WithTitle("Added new supporter");
            var description = new StringBuilder();
            description.AppendLine($"User id: {user} | <@{user}>\n" +
                                   $"Name: **{supporter.Name}**\n" +
                                   $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            description.AppendLine();
            description.AppendLine("✅ Full update started");

            this._embed.WithFooter("Name changes go through OpenCollective and apply within 24h");

            var discordUser = await this.Context.Client.GetUserAsync(discordUserId);
            if (discordUser != null && sendDm == null)
            {
                await SupporterService.SendSupporterWelcomeMessage(discordUser, userSettings.UserDiscogs != null,
                    supporter);

                description.AppendLine("✅ Thank you dm sent");
            }
            else
            {
                description.AppendLine("❌ Did not send thank you dm");
            }

            this._embed.WithDescription(description.ToString());

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

            await this._indexService.IndexUser(userSettings);
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("sendsupporterwelcome")]
    [Alias("sendwelcomedm")]
    public async Task SendWelcomeDm(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!ulong.TryParse(user, out var discordUserId))
            {
                await ReplyAsync("Wrong discord user id format");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var userSettings = await this._userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await ReplyAsync("User does not exist in database");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var supporter = await this._supporterService.GetSupporter(discordUserId);
            var discordUser = await this.Context.Client.GetUserAsync(discordUserId);

            if (discordUser == null)
            {
                await ReplyAsync("Discord user not found");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            await SupporterService.SendSupporterWelcomeMessage(discordUser, userSettings.UserDiscogs != null,
                supporter);

            await ReplyAsync("✅ Thank you dm sent");
        }
    }

    [Command("sendsupportergoodbye")]
    [Alias("sendgoodbyedm")]
    public async Task SendGoodbyeDm(string user = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!ulong.TryParse(user, out var discordUserId))
            {
                await ReplyAsync("Wrong discord user id format");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var discordUser = await this.Context.Client.GetUserAsync(discordUserId);

            if (discordUser == null)
            {
                await ReplyAsync("Discord user not found");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var userSettings = await this._userService.GetUserAsync(discordUserId);

            var hasImported = userSettings != null && userSettings.DataSource != DataSource.LastFm;

            await SupporterService.SendSupporterGoodbyeMessage(discordUser, hasImported);

            await ReplyAsync("✅ Goodbye dm sent");
        }
    }

    [Command("removesupporter")]
    public async Task RemoveSupporterAsync(string user = null, string sendDm = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when removing a supporter\n" +
                              "`.removesupporter \"discord-user-id\"`";

            if (string.IsNullOrEmpty(user) || user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userSettings = await this._userService.GetUserAsync(discordUserId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (userSettings.UserType != UserType.Supporter)
            {
                await ReplyAsync("`User is not a supporter`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var stripeSupporter = await this._supporterService.GetStripeSupporter(userSettings.DiscordUserId);
            if (stripeSupporter != null)
            {
                await this._supporterService.CheckExpiredStripeSupporters(userSettings.DiscordUserId);
                await ReplyAsync("Ran fast-tracked Stripe supporter expiry for " + stripeSupporter.PurchaserDiscordUserId);
                this.Context.LogCommandUsed();
                return;
            }

            var hadImported = userSettings.DataSource != DataSource.LastFm;

            var existingSupporter = await this._supporterService.GetSupporter(discordUserId);
            if (existingSupporter == null)
            {
                await ReplyAsync("`Existing supporter not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (existingSupporter.SubscriptionType != SubscriptionType.MonthlyOpenCollective &&
                existingSupporter.SubscriptionType != SubscriptionType.YearlyOpenCollective)
            {
                await ReplyAsync("You can only use this command on Stripe subs or on OpenCollective monthly and yearly subs");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var supporter = await this._supporterService.OpenCollectiveSupporterExpired(existingSupporter);

            var removedRole = false;
            if (this.Context.Guild.Id == this._botSettings.Bot.BaseServerId)
            {
                try
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(discordUserId);
                    if (guildUser != null)
                    {
                        var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Name == "Supporter");
                        await guildUser.RemoveRoleAsync(role);
                        removedRole = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Removing supporter role failed for {id}", discordUserId, e);
                }
            }


            this._embed.WithTitle("Processed supporter expiry");

            var description = new StringBuilder();
            description.AppendLine($"User id: {user} | <@{user}>\n" +
                                   $"Name: **{supporter.Name}**\n" +
                                   $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            description.AppendLine();
            description.AppendLine(removedRole ? "✅ Supporter role removed" : "❌ Unable to remove supporter role");
            description.AppendLine("✅ Full update started");

            var discordUser = await this.Context.Client.GetUserAsync(discordUserId);
            if (discordUser != null && sendDm == null)
            {
                await SupporterService.SendSupporterGoodbyeMessage(discordUser, hadImported);

                description.AppendLine("✅ Goodbye dm sent");
            }
            else
            {
                description.AppendLine("❌ Did not send goodbye dm");
            }

            this._embed.WithDescription(description.ToString());

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

            await this._indexService.IndexUser(userSettings);
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("addsupporterclassic")]
    [Examples("addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"",
        "addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"")]
    public async Task AddSupporterClassicAsync(string user = null, string name = null, string internalNotes = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when adding a supporter\n" +
                              "Examples: \n" +
                              "`.addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"`\n" +
                              "`.addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(internalNotes) || string.IsNullOrEmpty(name) ||
                user == "help")
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            if (!ulong.TryParse(user, out var userId))
            {
                await ReplyAsync(formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userSettings = await this._userService.GetUserAsync(userId);

            if (userSettings == null)
            {
                await ReplyAsync("`User not found`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (userSettings.UserType != UserType.User)
            {
                await ReplyAsync("`Can only change usertype of normal users`\n\n" + formatError);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._supporterService.AddSupporter(userSettings.DiscordUserId, name, internalNotes);

            this._embed.WithDescription("Supporter added.\n" +
                                        $"User id: {user} | <@{user}>\n" +
                                        $"Name: **{name}**\n" +
                                        $"Internal notes: `{internalNotes}`");

            this._embed.WithFooter("Command not intended for use in public channels");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("featuredoverride"), Summary("Changes the avatar to be an album.")]
    [Examples("featuredoverride \"imageurl\" \"description\" true")]
    public async Task FeaturedOverrideAsync(string url, string desc, bool stopTimer = false)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                await this._featuredService.CustomFeatured(this._timer.CurrentFeatured, desc, url);

                if (stopTimer)
                {
                    RecurringJob.TriggerJob(nameof(this._timer.CheckForNewFeatured));
                    await Task.Delay(5000);
                    RecurringJob.RemoveIfExists(nameof(this._timer.CheckForNewFeatured));
                }
                else
                {
                    RecurringJob.TriggerJob(nameof(this._timer.CheckForNewFeatured));
                }

                var description = new StringBuilder();
                description.AppendLine($"Avatar: {url}");
                description.AppendLine($"Description: {desc}");
                description.AppendLine($"Timer stopped: {stopTimer}");

                this._embed.WithTitle("Featured override");
                this._embed.WithDescription(description.ToString());
                this._embed.WithFooter(
                    "You might also have to edit the next few hours in the database (with no update true)");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot owners can set featured.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("updateavatar"), Summary("Changes the avatar to given url.")]
    public async Task UpdateAvatar(string url)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                await this._webhookService.ChangeToNewAvatar(this._client, url);

                await this.Context.Channel.SendMessageAsync($"Changed avatar to {url}");
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot owners can change avatar.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("reconnectshard", RunMode = RunMode.Async)]
    [Summary("Reconnects a shard")]
    [GuildOnly]
    [ExcludeFromHelp]
    [Alias("reconnectshards")]
    [Examples("shard 0", "shard 821660544581763093")]
    public async Task ReconnectShardAsync(ulong? guildId = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Channel.SendMessageAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (!guildId.HasValue)
        {
            await this.Context.Channel.SendMessageAsync(
                $"Enter a server id please (this server is `{this.Context.Guild.Id}`)");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var client = this.Context.Client as DiscordShardedClient;

        DiscordSocketClient shard;

        if (guildId is < 1000 and >= 0)
        {
            shard = client.GetShard(int.Parse(guildId.Value.ToString()));
        }
        else
        {
            var guild = client.GetGuild(guildId.Value);
            shard = client.GetShardFor(guild);
        }

        if (shard != null)
        {
            if (shard.ConnectionState == ConnectionState.Disconnected ||
                shard.ConnectionState == ConnectionState.Disconnecting ||
                shard.ConnectionState == ConnectionState.Connecting)
            {
                await this.Context.Channel.SendMessageAsync($"Connecting Shard #{shard.ShardId}");
                await shard.StartAsync();
                await this.Context.Channel.SendMessageAsync($"Connected Shard #{shard.ShardId}");
            }
            else
            {
                await this.Context.Channel.SendMessageAsync($"Shard #{shard.ShardId} is not in a disconnected state.");
            }
        }
        else
        {
            await this.Context.Channel.SendMessageAsync("Server or shard could not be found. \n" +
                                                        "This either means the bot is not connected to that server or that the bot is not in this server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        this.Context.LogCommandUsed();
    }

    [Command("postembed"), Summary("Posts one of the reporting embeds")]
    [Examples("postembed \"gwkreporter\"")]
    public async Task PostAdminEmbed([Remainder] string type = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Channel.SendMessageAsync($"No permissions mate");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (string.IsNullOrWhiteSpace("type"))
        {
            await ReplyAsync(
                "Pick an embed type that you want to post. Currently available: `rules`, `gwkreporter`, `nsfwreporter` and `buysupporter`");
            return;
        }

        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        if (type == "rules")
        {
            var components = new ComponentBuilderV2();
            components.AddComponent(new ContainerBuilder
            {
                Components =
                [
                    new TextDisplayBuilder(
                        "Welcome to the .fmbot support server!\n" +
                        "Get help with .fmbot, chat with other music enthusiasts and more."),
                    new SeparatorBuilder { Spacing = SeparatorSpacingSize.Large },
                    new TextDisplayBuilder(@"## 📜 Server rules:
1. **Be nice to each other.** Treat everyone with respect. No bigotry allowed, including but not limited to racism, sexism, homophobia, transphobia, ableism, and use of slurs (reclaimed or otherwise).
2. **Remember that music taste is subjective.** Criticism is fine, music elitism is not. Respect the music taste of others.
3. **Don't be spammy or annoying in general.** This can include repeatedly interrupting others, flooding the chat, immaturity, personal attacks, instigating or prolonging drama, and other behaviour that sucks the air out of the chat.
4. **No self promotion or marketing.** Established community members may do it in moderation. DM ads are never allowed.
5. **Safe for work content and chat only.** Keep it family friendly. An exception is made for spoilered album covers.
6. **English only.** We can't moderate other languages.
7. **Don't mention or DM other members without reason.** Mentions and DMs should be for a purpose, such as continuing conversations.
8. **No political discussion**, except for when it directly relates to music. This includes social issues or discussions about religion.

Not following these rules might lead to a mute, kick or ban. Staff members can ban, kick or mute you for any reason if they feel it is needed."),
                    new SeparatorBuilder { Spacing = SeparatorSpacingSize.Large },
                    new TextDisplayBuilder(@"Role pings:
- Ping <@&1083762942144688198> for issues that require immediate staff attention, like someone disrupting the server or a raid
- Ping <@&1083762904924434442> if the bot is down (not responding)

For anything else, you must use <#856212952305893376> and after that ask in <#1006526334316576859>. Someone will get to you when they have time."),
                    new SeparatorBuilder { Spacing = SeparatorSpacingSize.Large },
                    new TextDisplayBuilder(@"## 🔗 Links:
- Documentation: <https://fm.bot/>
- Link to this server: <http://discord.gg/fmbot>
- Bluesky: <https://bsky.app/profile/fm.bot>
- Feature requests or bug reports: <#1006526334316576859> or [GitHub](<https://github.com/fmbot-discord/fmbot/issues/new/choose>)
- Frequently Asked Questions: <#856212952305893376>
- Configure your notification roles in <id:customize>"),
                    new SeparatorBuilder { Spacing = SeparatorSpacingSize.Large },
                    new SectionBuilder
                    {
                        Components =
                        [
                            new TextDisplayBuilder("### Get .fmbot in your server")
                        ],
                        Accessory = new ButtonBuilder("Add to server", style: ButtonStyle.Link,
                            url:
                            "https://discord.com/oauth2/authorize?client_id=356268235697553409&permissions=275415092288&scope=applications.commands%20bot")
                    },
                    new SectionBuilder
                    {
                        Components =
                        [
                            new TextDisplayBuilder("### Use .fmbot slash commands everywhere")
                        ],
                        Accessory = new ButtonBuilder("Add to account", style: ButtonStyle.Link,
                            url:
                            "https://discord.com/oauth2/authorize?client_id=356268235697553409&scope=applications.commands&integration_type=1")
                    },
                    new SeparatorBuilder { Spacing = SeparatorSpacingSize.Large },
                    new SectionBuilder
                    {
                        Components =
                        [
                            new TextDisplayBuilder("### Support us and unlock extra perks")
                        ],
                        Accessory = new ButtonBuilder("Get .fmbot supporter", style: ButtonStyle.Primary,
                            customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(true, false,
                                false, source: "rulespromo"))
                    },
                ],
            }.WithAccentColor(DiscordConstants.InformationColorBlue));

            components.AddComponent(new ContainerBuilder
            {
                Components =
                [
                    new TextDisplayBuilder("## 🚨 NSFW and NSFL artwork report form"),
                    new TextDisplayBuilder(
                        "Found album artwork or an artist image that should be marked NSFW or censored entirely? Please report that here. \n\n" +
                        "Note that artwork is censored according to Discord guidelines and only as required by Discord. .fmbot is fundamentally opposed to artistic censorship."),
                    new TextDisplayBuilder("**Marked NSFW**\n" +
                                           "Frontal nudity [genitalia, exposed anuses, and 'female presenting nipples,' which is not our terminology], furry art in an erotic context and people covered in blood and/or wounds"),
                    new TextDisplayBuilder(
                        "**Fully censored / NSFL**\n" +
                        "Hate speech [imagery or text promoting prejudice against a group], gore [detailed, realistic, or semi realistic depictions of viscera or extreme bodily harm, not blood alone] and pornographic content [depictions of sex]"),
                    new SeparatorBuilder(),
                    new ActionRowBuilder()
                        .WithButton("Report artist image", style: ButtonStyle.Secondary,
                            customId: InteractionConstants.ModerationCommands.ReportArtist)
                        .WithButton("Report album cover", style: ButtonStyle.Secondary,
                            customId: InteractionConstants.ModerationCommands.ReportAlbum)
                ]
            }.WithAccentColor(DiscordConstants.InformationColorBlue));

            var globalWhoKnowsDescription = new StringBuilder();
            globalWhoKnowsDescription.AppendLine(
                "Want staff to take a look at someone that might be adding artificial or fake scrobbles? Or someone that is spamming short tracks? Report their profile here.");
            globalWhoKnowsDescription.AppendLine();
            globalWhoKnowsDescription.AppendLine(
                "Optionally you can add a note to your report. Keep in mind that everyone is kept to the same standard regardless of the added note.");
            globalWhoKnowsDescription.AppendLine();
            globalWhoKnowsDescription.AppendLine(
                "Note that we don't take reports for sleep or 24/7 scrobbling, those get filtered automatically with temporary bans.");
            this._embed.WithDescription(globalWhoKnowsDescription.ToString());

            components.AddComponent(new ContainerBuilder
            {
                Components =
                [
                    new TextDisplayBuilder("## 🌐 GlobalWhoKnows report form"),
                    new TextDisplayBuilder(globalWhoKnowsDescription.ToString()),
                    new SeparatorBuilder(),
                    new ActionRowBuilder()
                        .WithButton("Report user", style: ButtonStyle.Secondary,
                            customId: InteractionConstants.ModerationCommands.GlobalWhoKnowsReport)
                ]
            }.WithAccentColor(DiscordConstants.InformationColorBlue));

            await ReplyAsync(components: components.Build(), flags: MessageFlags.ComponentsV2,
                allowedMentions: AllowedMentions.None);
        }

        if (type == "buysupporter")
        {
            var description = new StringBuilder();
            description.AppendLine(
                "Use the button below to get started.");
            this._embed.WithDescription(description.ToString());

            var components = new ComponentBuilder().WithButton("Get .fmbot supporter", style: ButtonStyle.Primary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(true, false, false,
                    source: "embedpromo"));
            await ReplyAsync(embed: this._embed.Build(), components: components.Build());
        }

        if (type == "gwkreporter")
        {
            this._embed.WithTitle("GlobalWhoKnows report form");

            var description = new StringBuilder();
            description.AppendLine(
                "Want staff to take a look at someone that might be adding artificial or fake scrobbles? Or someone that is spamming short tracks? Report their profile here.");
            description.AppendLine();
            description.AppendLine(
                "Optionally you can add a note to your report. Keep in mind that everyone is kept to the same standard regardless of the added note.");
            description.AppendLine();
            description.AppendLine(
                "Note that we don't take reports for sleep or 24/7 scrobbling, those get filtered automatically with temporary bans.");
            this._embed.WithDescription(description.ToString());

            var components = new ComponentBuilder().WithButton("Report user", style: ButtonStyle.Secondary,
                customId: InteractionConstants.ModerationCommands.GlobalWhoKnowsReport);
            await ReplyAsync(embed: this._embed.Build(), components: components.Build());
        }

        if (type == "nsfwreporter")
        {
            this._embed.WithTitle("NSFW and NSFL artwork report form");

            var description = new StringBuilder();
            description.AppendLine(
                "Found album artwork or an artist image that should be marked NSFW or censored entirely? Please report that here.");
            description.AppendLine();
            description.AppendLine(
                "Note that artwork is censored according to Discord guidelines and only as required by Discord. .fmbot is fundamentally opposed to artistic censorship.");
            description.AppendLine();
            description.AppendLine("**Marked NSFW**");
            description.AppendLine(
                "Frontal nudity [genitalia, exposed anuses, and 'female presenting nipples,' which is not our terminology]");
            description.AppendLine();
            description.AppendLine("**Fully censored / NSFL**");
            description.AppendLine(
                "Hate speech [imagery or text promoting prejudice against a group], gore [detailed, realistic, or semi realistic depictions of viscera or extreme bodily harm, not blood alone] and pornographic content [depictions of sex]");
            this._embed.WithDescription(description.ToString());

            var components = new ComponentBuilder()
                .WithButton("Report artist image", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.ModerationCommands.ReportArtist)
                .WithButton("Report album cover", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.ModerationCommands.ReportAlbum);

            await ReplyAsync(embed: this._embed.Build(), components: components.Build());
        }
    }


    //[Command("fmavataroverride"), Summary("Changes the avatar to be a image from a link.")]
    //[Alias("fmsetavatar")]
    //public async Task fmavataroverrideAsync(string link, string desc = "Custom FMBot Avatar", int ievent = 0)
    //{
    //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
    //    {
    //        JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

    //        if (link == "help")
    //        {
    //            await ReplyAsync(cfgjson.Prefix + "fmavataroverride <image link> [message in quotation marks] [event 0 or 1]");
    //            return;
    //        }

    //        try
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;

    //            if (ievent == 1)
    //            {
    //                _timer.UseCustomAvatarFromLink(client, link, desc, true);
    //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is an event and it cannot be stopped the without the Owner's assistance. To stop an event, please contact the owner of the bot or specify a different avatar without the event parameter.");
    //            }
    //            else
    //            {
    //                _timer.UseCustomAvatarFromLink(client, link, desc, false);
    //                await ReplyAsync("Set avatar to '" + link + "' with description '" + desc + "'. This is not an event.");
    //            }
    //        }
    //        catch (Exception e)
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            ExceptionReporter.ReportException(client, e);
    //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
    //        }
    //    }
    //}

    //[Command("fmresetavatar"), Summary("Changes the avatar to be the default.")]
    //public async Task fmresetavatar()
    //{
    //    if (await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
    //    {
    //        try
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            _timer.UseDefaultAvatar(client);
    //            await ReplyAsync("Set avatar to 'FMBot Default'");
    //        }
    //        catch (Exception e)
    //        {
    //            DiscordSocketClient client = this.Context.Client as DiscordSocketClient;
    //            ExceptionReporter.ReportException(client, e);
    //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
    //        }
    //    }
    //}

    [Command("resetfeatured")]
    [Summary("Restarts the featured timer.")]
    [Alias("restarttimer", "timerstart", "timerrestart")]
    public async Task RestartTimerAsync([Remainder] int? id = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var feature = this._timer.CurrentFeatured;

                if (id.HasValue)
                {
                    feature = await this._featuredService.GetFeaturedForId(id.Value);
                }

                var updateDescription = new StringBuilder();
                updateDescription.AppendLine("**Selected feature**");
                updateDescription.AppendLine(feature.Description);
                updateDescription.AppendLine(feature.ImageUrl);
                updateDescription.AppendLine();

                var newFeature = await this._featuredService.ReplaceFeatured(feature, this.Context.User.Id);

                updateDescription.AppendLine("**New feature**");
                updateDescription.AppendLine(newFeature.Description);
                updateDescription.AppendLine(newFeature.ImageUrl);
                updateDescription.AppendLine();

                updateDescription.AppendLine(
                    "Featured timer restarted. Can take up to three minutes to show, max 3 times / hour");

                var dateValue = ((DateTimeOffset)feature.DateTime).ToUnixTimeSeconds();
                this._embed.AddField("Time", $"<t:{dateValue}:F>");

                this._embed.WithDescription(updateDescription.ToString());
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only .fmbot staff can restart timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("picknewfeatureds")]
    [Summary("Runs the job that picks new featureds manually.")]
    public async Task StopTimerAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await ReplyAsync("Started pick new featured job");
                await this._timer.PickNewFeatureds();
                await ReplyAsync("Finished pick new featured job");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("checknewsupporters")]
    [Summary("Runs the job that checks for new OpenCollective supporters.")]
    public async Task CheckNewSupporters()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await this._timer.CheckForNewOcSupporters();
                await ReplyAsync("Checked for new oc supporters");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("checkdiscordsupporterusers")]
    [Summary("Updates all discord supporters")]
    public async Task CheckDiscordSupporterUserTypes()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await ReplyAsync("Fetching supporters from Discord...");
                await this._supporterService.CheckIfDiscordSupportersHaveCorrectUserType();
                await ReplyAsync("Updated all Discord supporters");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("checksupporterroles")]
    [Summary("Updates all discord supporters")]
    public async Task CheckDiscordSupporterRoles()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                _ = this.Context.Channel.TriggerTypingAsync();
                var activeSupporters = await this._supporterService.GetAllVisibleSupporters();
                var guildMembers = await this.Context.Guild.GetUsersAsync();

                var discordUserIds = activeSupporters
                    .Where(w => w.DiscordUserId.HasValue)
                    .Select(s => s.DiscordUserId.Value)
                    .ToHashSet();

                var count = 0;
                var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Name == "Supporter");

                var reply = new StringBuilder();

                foreach (var member in guildMembers.Where(w => discordUserIds.Contains(w.Id)))
                {
                    if (member.RoleIds.All(a => a != role.Id))
                    {
                        await member.AddRoleAsync(role);
                        reply.AppendLine($"{member.Id} - <@{member.Id}> - {member.DisplayName}");

                        count++;
                    }
                }

                reply.AppendLine();
                reply.AppendLine($"Updated all Discord supporters.");
                reply.AppendLine($"{count} users didn't have the supporter role when they should have had it.");

                var allSupporters = await this._supporterService.GetAllSupporters();
                var expiredOnly = allSupporters
                    .Where(w => w.DiscordUserId.HasValue)
                    .GroupBy(g => g.DiscordUserId)
                    .Where(w => w.All(a => a.Expired == true))
                    .Select(s => s.Key).ToHashSet();

                reply.AppendLine();
                reply.AppendLine("Check if these should have it (nvm, just ignore this):");
                foreach (var member in guildMembers.Where(w => expiredOnly.Contains(w.Id)))
                {
                    if (member.RoleIds.Any(a => a == role.Id))
                    {
                        reply.AppendLine($"{member.Id} - <@{member.Id}> - {member.DisplayName}");
                    }
                }


                await ReplyAsync(reply.ToString());
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("updatediscordsupporter")]
    [Summary("Updates single discord supporter")]
    public async Task UpdateSingleDiscordSupporters([Remainder] string user)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                var userToUpdate = await this._settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                await this._supporterService.UpdateSingleDiscordSupporter(userToUpdate.DiscordUserId);
                await ReplyAsync("Updated single discord supporter");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("updatemultidiscordsupporters")]
    [Summary("Updates multiple discord supporter")]
    public async Task UpdateMultipleDiscordSupporters([Remainder] string user)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                var idsToUpdate = user.Split(",");
                var updated = new StringBuilder();
                var skipped = new StringBuilder();

                foreach (var id in idsToUpdate)
                {
                    var userToUpdate = await this._settingService.GetDifferentUser(user);

                    if (userToUpdate == null)
                    {
                        skipped.AppendLine($"{id} - <@{id}>");
                        continue;
                    }

                    await this._supporterService.UpdateSingleDiscordSupporter(userToUpdate.DiscordUserId);
                    updated.AppendLine($"{id} - <@{id}>");
                }

                await ReplyAsync("Updated:\n" +
                                 $"{updated}\n" +
                                 $"Skipped:\n" +
                                 $"{skipped}",
                    allowedMentions: AllowedMentions.None);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("refreshpremiumservers")]
    [Summary("Refreshes cached premium servers")]
    public async Task RefreshPremiumGuilds()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await this._guildService.RefreshPremiumGuilds();
                await ReplyAsync("Refreshed premium server cache dictionary");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("runtimer")]
    [Summary("Run a timer manually (only works if it exists)")]
    [Alias("triggerjob", "runjob")]
    public async Task RunTimerAsync([Remainder] string job = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                if (job == null)
                {
                    await ReplyAsync("Pick a job to run. Check `.timerstatus` for available jobs.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                if (job == "masterjobs")
                {
                    this._timer.QueueMasterJobs();
                    await ReplyAsync("Added masterjobs");
                    return;
                }

                var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

                var jobToRun = recurringJobs.FirstOrDefault(f => f.Id.ToLower() == job.ToLower());

                if (jobToRun == null)
                {
                    await ReplyAsync("Could not find job you're looking for. Check `.timerstatus` for available jobs.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                RecurringJob.TriggerJob(jobToRun.Id);
                await ReplyAsync($"Triggered job {jobToRun.Id}", allowedMentions: AllowedMentions.None);

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can stop timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("removetimer")]
    [Summary("Remove a timer manually (only works if it exists)")]
    [Alias("removejob", "deletejob")]
    public async Task RemoveJobAsync([Remainder] string job = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                if (job == null)
                {
                    await ReplyAsync("Pick a job to remove. Check `.timerstatus` for available jobs.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

                var jobToRemove = recurringJobs.FirstOrDefault(f => f.Id.ToLower() == job.ToLower());

                if (jobToRemove == null)
                {
                    await ReplyAsync("Could not find job you're looking for. Check `.timerstatus` for available jobs.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                RecurringJob.RemoveIfExists(jobToRemove.Id);
                await ReplyAsync($"Removed job {jobToRemove.Id}", allowedMentions: AllowedMentions.None);

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot owners can remove jobs.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("timerstatus")]
    [Summary("Checks the status of the timer.")]
    public async Task TimerStatusAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

                var description = new StringBuilder();

                foreach (var job in recurringJobs)
                {
                    description.AppendLine($"**{job.Id}**");

                    if (job.RetryAttempt > 0)
                    {
                        description.AppendLine($"*Retried {job.RetryAttempt} times*");
                    }

                    if (job.Error != null)
                    {
                        description.AppendLine($"⚠️ Error");
                    }

                    description.Append("Last execution ");
                    if (job.LastExecution.HasValue)
                    {
                        var dateValue = ((DateTimeOffset)job.LastExecution).ToUnixTimeSeconds();
                        description.Append($"<t:{dateValue}:R>");
                    }
                    else
                    {
                        description.Append($"never");
                    }

                    description.Append(" - ");

                    description.Append("Next ");
                    if (job.NextExecution.HasValue)
                    {
                        var dateValue = ((DateTimeOffset)job.NextExecution).ToUnixTimeSeconds();
                        description.Append($"<t:{dateValue}:R>");
                    }
                    else
                    {
                        description.Append($"never");
                    }

                    description.AppendLine();
                    description.AppendLine();
                }

                this._embed.WithColor(DiscordConstants.InformationColorBlue);
                this._embed.WithDescription(description.ToString());
                this._embed.WithFooter("15 second timer interval");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can check timer.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("globalblockadd")]
    [Summary("Blocks a user from using .fmbot.")]
    [Alias("globalblocklistadd", "globalblacklistadd")]
    public async Task BlacklistAddAsync(ulong? user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!user.HasValue)
                {
                    await ReplyAsync("Please specify what discord user id you want to block from using .fmbot.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                if (user == this.Context.Message.Author.Id)
                {
                    await ReplyAsync("You cannot block yourself from using .fmbot!");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var blacklistResult = await this._adminService.AddUserToBlocklistAsync(user.Value);

                if (blacklistResult)
                {
                    await ReplyAsync(
                        "Blocked " + user +
                        " from using .fmbot. Cached up to 5 minutes, applies to their Last.fm username globally.",
                        allowedMentions: AllowedMentions.None);
                }
                else
                {
                    await ReplyAsync("You have already added " + user +
                                     " to the list of blocked users or something went wrong.",
                        allowedMentions: AllowedMentions.None);
                }

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("globalblockremove")]
    [Summary("Unblocks a user so they can use .fmbot again.")]
    [Alias("globalblocklistremove", "globalblacklistremove")]
    public async Task BlackListRemoveAsync(ulong? user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!user.HasValue)
                {
                    await ReplyAsync(
                        "Please specify what user you want to remove from the list of users who are blocked from using .fmbot.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var blacklistResult = await this._adminService.RemoveUserFromBlocklistAsync(user.Value);

                if (blacklistResult)
                {
                    await ReplyAsync(
                        "Removed " + user +
                        " from the list of users who are blocked from using .fmbot. Cached up to 5 minutes, applies to their Last.fm username globally.",
                        allowedMentions: AllowedMentions.None);
                }
                else
                {
                    await ReplyAsync(
                        "You have already removed " + user +
                        " from the list of users who are blocked from using the bot.",
                        allowedMentions: AllowedMentions.None);
                }

                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("runfullupdate")]
    [Summary("Runs a full update for someone else")]
    public async Task RunFullUpdate([Remainder] string user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var userToUpdate = await this._settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                await ReplyAsync($"Running full update for '{userToUpdate.UserNameLastFM}'",
                    allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed();

                await this._indexService.IndexUser(userToUpdate);
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("runtoplistupdate")]
    [Summary("Runs a toplist update for someone else")]
    public async Task RunTopListUpdate([Remainder] string user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var userToUpdate = await this._settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                await ReplyAsync($"Running top list update for '{userToUpdate.UserNameLastFM}'",
                    allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed();

                await this._indexService.RecalculateTopLists(userToUpdate);
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("supporterlink")]
    public async Task GetSupporterTestLink([Remainder] string user = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var components = new ComponentBuilder()
                    .WithButton("Get .fmbot supporter",
                        customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}-true-false-true-testlink");

                var embed = new EmbedBuilder();
                embed.WithDescription("Start the new purchase flow below");
                embed.WithColor(DiscordConstants.InformationColorBlue);

                await ReplyAsync(embed: embed.Build(), components: components.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("importdebug")]
    [Summary("Debug your import playcount")]
    [Options("Artist name")]
    public async Task ImportDebug([Remainder] string user = null)
    {
        try
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var userSettings = await this._settingService.GetUser(user, contextUser, this.Context);

            if (!SupporterService.IsSupporter(userSettings.UserType))
            {
                await ReplyAsync("You can only debug imports for supporters.");
                this.Context.LogCommandUsed(CommandResponse.SupporterRequired);
                return;
            }

            var dbUser = await this._settingService.GetDifferentUser(userSettings.DiscordUserId.ToString());

            if (dbUser == null)
            {
                await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var allPlays = await this._playService.GetAllUserPlays(dbUser.UserId, false);
            var allFinalizedPlays = await this._playService.GetAllUserPlays(dbUser.UserId, true);

            var description = new StringBuilder();

            string artistName = null;
            if (!string.IsNullOrWhiteSpace(userSettings.NewSearchValue))
            {
                description.AppendLine($"Filtering to artist `{userSettings.NewSearchValue}`");
                description.AppendLine();
                artistName = userSettings.NewSearchValue;
            }

            if (dbUser.UserType != UserType.User)
            {
                description.AppendLine(
                    $"**{StringExtensions.Sanitize(userSettings.DisplayName)} {dbUser.UserType.UserTypeToIcon()}**");
            }

            if (dbUser.DataSource != DataSource.LastFm)
            {
                var name = dbUser.DataSource.GetAttribute<OptionAttribute>().Name;

                switch (dbUser.DataSource)
                {
                    case DataSource.FullImportThenLastFm:
                    case DataSource.ImportThenFullLastFm:
                        description.AppendLine($"Imported: {name}");
                        break;
                    case DataSource.LastFm:
                    default:
                        break;
                }

                description.AppendLine();

                var firstImportPlay = allPlays
                    .OrderBy(o => o.TimePlayed)
                    .Where(w => artistName == null ||
                                string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(w => w.PlaySource != PlaySource.LastFm);
                if (firstImportPlay != null)
                {
                    var dateValue = ((DateTimeOffset)firstImportPlay.TimePlayed).ToUnixTimeSeconds();
                    description.AppendLine($"First imported play: <t:{dateValue}:F>");
                }

                description.AppendLine($"Imported play count: `{allPlays
                    .Where(w => artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase)).Count(w => w.PlaySource != PlaySource.LastFm)}`");

                var lastImportPlay = allPlays
                    .OrderByDescending(o => o.TimePlayed)
                    .Where(w => artistName == null ||
                                string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(w => w.PlaySource != PlaySource.LastFm);
                if (lastImportPlay != null)
                {
                    var dateValue = ((DateTimeOffset)lastImportPlay.TimePlayed).ToUnixTimeSeconds();
                    description.AppendLine($"Last imported play: <t:{dateValue}:F>");
                }

                description.AppendLine();
                var firstFinalizedImportPlay = allFinalizedPlays
                    .OrderBy(o => o.TimePlayed)
                    .Where(w => artistName == null ||
                                string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(w => w.PlaySource != PlaySource.LastFm);
                if (firstFinalizedImportPlay != null)
                {
                    var dateValue = ((DateTimeOffset)firstFinalizedImportPlay.TimePlayed).ToUnixTimeSeconds();
                    description.AppendLine($"First imported play after filtering: <t:{dateValue}:F>");
                }

                description.AppendLine($"Imported play count after filtering: `{allFinalizedPlays
                    .Where(w => artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase)).Count(w => w.PlaySource != PlaySource.LastFm)}`");

                var lastFinalizedImportPlay = allFinalizedPlays
                    .OrderByDescending(o => o.TimePlayed)
                    .Where(w => artistName == null ||
                                string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(w => w.PlaySource != PlaySource.LastFm);
                if (lastFinalizedImportPlay != null)
                {
                    var dateValue = ((DateTimeOffset)lastFinalizedImportPlay.TimePlayed).ToUnixTimeSeconds();
                    description.AppendLine($"Last imported play after filtering: <t:{dateValue}:F>");
                }

                description.AppendLine();
            }

            var firstLfmPlay = allPlays
                .OrderBy(o => o.TimePlayed)
                .Where(w => artistName == null ||
                            string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(w => w.PlaySource == PlaySource.LastFm);
            if (firstLfmPlay != null)
            {
                var dateValue = ((DateTimeOffset)firstLfmPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"First Last.fm play: <t:{dateValue}:F>");
            }

            description.AppendLine($"Last.fm play count: `{allPlays
                .Where(w => artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase)).Count(w => w.PlaySource == PlaySource.LastFm)}`");

            var lastLfmPlay = allPlays
                .OrderByDescending(o => o.TimePlayed)
                .Where(w => artistName == null ||
                            string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(w => w.PlaySource == PlaySource.LastFm);
            if (lastLfmPlay != null)
            {
                var dateValue = ((DateTimeOffset)lastLfmPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"Last Last.fm play: <t:{dateValue}:F>");
            }

            description.AppendLine();

            var firstFilteredLfmPlay = allFinalizedPlays
                .OrderBy(o => o.TimePlayed)
                .Where(w => artistName == null ||
                            string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(w => w.PlaySource == PlaySource.LastFm);
            if (firstFilteredLfmPlay != null)
            {
                var dateValue = ((DateTimeOffset)firstFilteredLfmPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"First Last.fm play after filtering: <t:{dateValue}:F>");
            }

            description.AppendLine($"Last.fm play count after filtering: `{allFinalizedPlays
                .Where(w => artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase)).Count(w => w.PlaySource == PlaySource.LastFm)}`");

            var lastFilteredLfmPlay = allFinalizedPlays
                .OrderByDescending(o => o.TimePlayed)
                .Where(w => artistName == null ||
                            string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(w => w.PlaySource == PlaySource.LastFm);
            if (lastFilteredLfmPlay != null)
            {
                var dateValue = ((DateTimeOffset)lastFilteredLfmPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"Last Last.fm play after filtering: <t:{dateValue}:F>");
            }

            description.AppendLine();

            var firstFilteredPlay = allFinalizedPlays
                .OrderBy(o => o.TimePlayed)
                .FirstOrDefault(w =>
                    artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase));
            if (firstFilteredPlay != null)
            {
                var dateValue = ((DateTimeOffset)firstFilteredPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"Final first play: <t:{dateValue}:F>");
            }

            description.AppendLine(
                $"Final play count: `{allFinalizedPlays.Count(w => artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase))}`");

            var lastFilteredPlay = allFinalizedPlays
                .OrderByDescending(o => o.TimePlayed)
                .FirstOrDefault(w =>
                    artistName == null || string.Equals(artistName, w.ArtistName, StringComparison.OrdinalIgnoreCase));
            if (lastFilteredPlay != null)
            {
                var dateValue = ((DateTimeOffset)lastFilteredPlay.TimePlayed).ToUnixTimeSeconds();
                description.AppendLine($"Final last play: <t:{dateValue}:F>");
            }

            this._embed.WithDescription(description.ToString());
            this._embed.WithFooter("Import debug");

            await ReplyAsync(embed: this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("movedata")]
    [Summary("Move imports, streaks, featured logs and users that have them as friends from one user to another")]
    public async Task MoveData(string oldUserId = null, string newUserId = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (oldUserId == null || newUserId == null)
                {
                    await ReplyAsync(
                        "Enter the old and new id. For example, `.moveimports 125740103539621888 356268235697553409`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var oldUser = await this._settingService.GetDifferentUser(oldUserId);
                var newUser = await this._settingService.GetDifferentUser(newUserId);

                if (oldUser == null || newUser == null)
                {
                    await ReplyAsync(
                        "One or both users could not be found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var embed = new EmbedBuilder();

                var oldUserDescription = new StringBuilder();
                oldUserDescription.AppendLine($"`{oldUser.DiscordUserId}` - <@{oldUser.DiscordUserId}>");
                oldUserDescription.AppendLine($"Last.fm: `{oldUser.UserNameLastFM}`");
                if (oldUser.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(oldUser.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    oldUserDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                embed.AddField($"Old user - {oldUser.UserId} {oldUser.UserType.UserTypeToIcon()}",
                    oldUserDescription.ToString());

                var newUserDescription = new StringBuilder();
                newUserDescription.AppendLine($"`{newUser.DiscordUserId}` - <@{newUser.DiscordUserId}>");
                newUserDescription.AppendLine($"Last.fm: `{newUser.UserNameLastFM}`");
                if (newUser.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(newUser.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    newUserDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                embed.AddField($"New user - {newUser.UserId} {newUser.UserType.UserTypeToIcon()}",
                    newUserDescription.ToString());

                if (!string.Equals(oldUser.UserNameLastFM, newUser.UserNameLastFM, StringComparison.OrdinalIgnoreCase))
                {
                    embed.AddField("⚠️ Warning ⚠️", "Last.fm usernames are different, are you sure?");
                }

                embed.WithDescription(
                    "This will move over all imported plays, saved streaks, featured logs and users that added them as friend from one user to another.\n\n" +
                    "Note about imports:\n" +
                    "- The new user should have no imports! Otherwise they might be duplicated" +
                    "- After moving they can enable the imports with `/import manage`");

                var components = new ComponentBuilder().WithButton("Move data",
                    customId: $"move-user-data-{oldUser.UserId}-{newUser.UserId}", ButtonStyle.Danger);

                await ReplyAsync(null, embed: embed.Build(), allowedMentions: AllowedMentions.None,
                    components: components.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("movesupporter")]
    [Summary("Move stripe supporter from one account to another")]
    public async Task MoveSupporter(string oldUserId = null, string newUserId = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (oldUserId == null || newUserId == null)
                {
                    await ReplyAsync(
                        "Enter the old and new id. For example, `.movesupporter 125740103539621888 356268235697553409`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var oldUser = await this._settingService.GetDifferentUser(oldUserId);
                var newUser = await this._settingService.GetDifferentUser(newUserId);

                if (oldUser == null || newUser == null)
                {
                    await ReplyAsync(
                        "One or both users could not be found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                if (oldUser.UserType != UserType.Supporter)
                {
                    await ReplyAsync(
                        "Old user is not a supporter");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var stripeSupporter = await this._supporterService.GetStripeSupporter(oldUser.DiscordUserId);

                if (stripeSupporter == null)
                {
                    await ReplyAsync(
                        "Old user is not a Stripe supporter. At the moment only transferring Stripe supporters is supported.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var embed = new EmbedBuilder();

                var stripeDescription = new StringBuilder();
                stripeDescription.AppendLine($"Customer ID: `{stripeSupporter.StripeCustomerId}`");
                stripeDescription.AppendLine($"Subscription ID: `{stripeSupporter.StripeSubscriptionId}`");
                embed.AddField("Stripe details", stripeDescription.ToString());

                var oldUserDescription = new StringBuilder();
                oldUserDescription.AppendLine($"`{oldUser.DiscordUserId}` - <@{oldUser.DiscordUserId}>");
                oldUserDescription.AppendLine($"Last.fm: `{oldUser.UserNameLastFM}`");
                if (oldUser.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(oldUser.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    oldUserDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                embed.AddField($"Old user - {oldUser.UserId} {oldUser.UserType.UserTypeToIcon()}",
                    oldUserDescription.ToString());

                var newUserDescription = new StringBuilder();
                newUserDescription.AppendLine($"`{newUser.DiscordUserId}` - <@{newUser.DiscordUserId}>");
                newUserDescription.AppendLine($"Last.fm: `{newUser.UserNameLastFM}`");
                if (newUser.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(newUser.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    newUserDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                embed.AddField($"New user - {newUser.UserId} {newUser.UserType.UserTypeToIcon()}",
                    newUserDescription.ToString());

                if (!string.Equals(oldUser.UserNameLastFM, newUser.UserNameLastFM, StringComparison.OrdinalIgnoreCase))
                {
                    embed.AddField("⚠️ Warning ⚠️", "Last.fm usernames are different, are you sure?");
                }

                embed.WithDescription(
                    "This will move over Stripe supporter from one user to another and update their details within the Stripe dashboard. This can take a few seconds to complete.");

                var components = new ComponentBuilder().WithButton("Move supporter",
                    customId: $"move-supporter-{oldUser.DiscordUserId}-{newUser.DiscordUserId}", ButtonStyle.Danger);

                await ReplyAsync(null, embed: embed.Build(), allowedMentions: AllowedMentions.None,
                    components: components.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("deleteuser")]
    [Summary("Remove a user")]
    [Alias("removeuser")]
    public async Task DeleteUser(string userToDelete = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner) &&
                this.Context.Guild.Id == 821660544581763093)
            {
                if (userToDelete == null)
                {
                    await ReplyAsync("Enter the user to delete. For example, `.deleteuser 125740103539621888`");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                var user = await this._settingService.GetDifferentUser(userToDelete);

                if (user == null)
                {
                    await ReplyAsync("User could not be found. Are you sure they are registered in .fmbot?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var embed = new EmbedBuilder();

                var userDescription = new StringBuilder();
                userDescription.AppendLine($"`{user.DiscordUserId}` - <@{user.DiscordUserId}>");
                userDescription.AppendLine($"Last.fm: `{user.UserNameLastFM}`");
                if (user.LastUsed.HasValue)
                {
                    var specifiedDateTime = DateTime.SpecifyKind(user.LastUsed.Value, DateTimeKind.Utc);
                    var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                    userDescription.AppendLine($"Last used: <t:{dateValue}:R>.");
                }

                embed.AddField($"User to delete - {user.UserId} {user.UserType.UserTypeToIcon()}",
                    userDescription.ToString());
                embed.WithFooter("⚠️ You cant revert this ⚠️ watch out whee oooo");

                var components = new ComponentBuilder().WithButton("Delete user",
                    customId: $"admin-delete-user-{user.UserId}", ButtonStyle.Danger);

                await ReplyAsync(null, embed: embed.Build(), allowedMentions: AllowedMentions.None,
                    components: components.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You are not authorized to use this command, or you're in the wrong server.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("lastfmissue")]
    [Alias("lfmissue")]
    public async Task LastfmIssue(string _ = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var embed = new EmbedBuilder();
            embed.WithDescription(
                "It looks like you asked for help with a Last.fm issue, and not an .fmbot issue.\n\n" +
                ".fmbot is not affiliated with Last.fm, the bot and the website are two different things.\n\n" +
                "Generally speaking we can't help with Last.fm issues, but we and other members of the community might still be able to offer suggestions. You can also consider asking the two communities linked below.");

            var components = new ComponentBuilder()
                .WithButton(url: "https://support.last.fm/", label: "Last.fm support forums", style: ButtonStyle.Link)
                .WithButton(url: "https://discord.gg/lastfm", label: "Last.fm Discord", style: ButtonStyle.Link);

            embed.WithColor(DiscordConstants.LastFmColorRed);

            await ReplyAsync(null, embed: embed.Build(), allowedMentions: AllowedMentions.None,
                components: components.Build());

            if (this.Context.Channel is SocketThreadChannel
                {
                    ParentChannel: SocketForumChannel forumChannel
                } threadChannel &&
                forumChannel.Tags.Any())
            {
                var tagToApply = forumChannel.Tags.FirstOrDefault(f => f.Name == "Last.fm issue");
                if (tagToApply != null)
                {
                    await threadChannel.ModifyAsync(m => m.AppliedTags = new List<ulong> { tagToApply.Id });
                }
            }
        }
    }

    [Command("banshortlisteners")]
    public async Task BanShortListeners([Remainder] string trackValues = null)
    {
        try
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                _ = this.Context.Channel.TriggerTypingAsync();

                var response = new ResponseModel
                {
                    ResponseType = ResponseType.Embed,
                };

                var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

                var shortTrack = await this._trackService.SearchTrack(response, this.Context.User, trackValues,
                    contextUser.UserNameLastFM);
                if (shortTrack.Track == null)
                {
                    await ReplyAsync("Couldn't find track");
                    return;
                }

                var usersWithTrack = await this._whoKnowsTrackService.GetGlobalUsersForTrack(this.Context.Guild,
                    shortTrack.Track.ArtistName, shortTrack.Track.TrackName);

                var bannedUsers = new StringBuilder();
                var bannedUserCount = 0;
                var manualCheckUsers = new StringBuilder();

                const int playThreshold = 5000;

                usersWithTrack = usersWithTrack.Where(w => w.Playcount >= playThreshold).ToList();
                await ReplyAsync(
                    $"Checking and banning {usersWithTrack.Count} users with over {playThreshold} plays on {shortTrack.Track.TrackName} by {shortTrack.Track.ArtistName}");

                foreach (var userToCheck in usersWithTrack.Where(w => w.Playcount >= playThreshold))
                {
                    var bottedUser =
                        await this._adminService.GetBottedUserAsync(userToCheck.LastFMUsername,
                            userToCheck.RegisteredLastFm);
                    if (bottedUser != null)
                    {
                        continue;
                    }

                    var currentPlaycount = await this._dataSourceFactory.GetTrackInfoAsync(shortTrack.Track.TrackName,
                        shortTrack.Track.ArtistName,
                        userToCheck.LastFMUsername);
                    if (currentPlaycount.Success && currentPlaycount.Content.UserPlaycount >= playThreshold)
                    {
                        var reason = $"Semi-automated ban for short track spam\n" +
                                     $"{shortTrack.Track.TrackName} by {shortTrack.Track.ArtistName}\n" +
                                     $"{currentPlaycount.Content.UserPlaycount} plays";
                        await this._adminService.AddBottedUserAsync(userToCheck.LastFMUsername, reason);
                        Log.Information("Banning {userNameLastFm} from GW: {reason}", userToCheck.LastFMUsername,
                            reason);

                        bannedUsers.AppendLine(
                            $"- [{userToCheck.LastFMUsername}]({LastfmUrlExtensions.GetUserUrl(userToCheck.LastFMUsername)}) - {currentPlaycount.Content.UserPlaycount}");
                        bannedUserCount++;
                    }
                    else
                    {
                        manualCheckUsers.AppendLine(
                            $"- [{userToCheck.LastFMUsername}]({LastfmUrlExtensions.GetUserUrl(userToCheck.LastFMUsername)}) - {currentPlaycount.Content?.UserPlaycount}");
                    }

                    await Task.Delay(400);
                    if (bannedUserCount > 30)
                    {
                        break;
                    }
                }

                response.Embed.WithTitle($"Banned {bannedUserCount} that spammed a short track");
                response.Embed.WithDescription(bannedUsers.ToString());

                if (manualCheckUsers.Length > 0)
                {
                    response.Embed.AddField("Check these users", manualCheckUsers.ToString());
                }

                response.Embed.WithFooter($"{shortTrack.Track.TrackName} by {shortTrack.Track.ArtistName}");

                await this.Context.SendResponse(this.Interactivity, response);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            else
            {
                await ReplyAsync("Only bot owners can do this command");
                return;
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("postpendingreports")]
    public async Task PostPendingReports([Remainder] string trackValues = null)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        await ReplyAsync("Reposting open reports...");

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var musicReports = db.CensoredMusicReport
            .Where(w => w.ReportStatus == ReportStatus.Pending)
            .Include(i => i.Album)
            .Include(i => i.Artist)
            .ToList();

        foreach (var report in musicReports)
        {
            await this._censorService.PostReport(report);
        }

        var userReports = db.BottedUserReport.Where(w => w.ReportStatus == ReportStatus.Pending).ToList();
        foreach (var report in userReports)
        {
            await this._adminService.PostReport(report);
        }

        await ReplyAsync("Done");
    }
}
