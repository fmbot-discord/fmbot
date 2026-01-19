using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using NetCord.Rest;
using Serilog;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;
using NetCord.Services.Commands;
using NetCord;
using Fergun.Interactive;
using NetCord.Gateway;

namespace FMBot.Bot.TextCommands;

[ExcludeFromHelp]
public class AdminCommands(
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
    PlayService playService,
    ShardedGatewayClient client,
    WebhookService webhookService,
    TrackService trackService,
    WhoKnowsTrackService whoKnowsTrackService,
    IDbContextFactory<FMBotDbContext> contextFactory)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    //[Command("debug")]
    //[Summary("Returns user data")]
    //[Alias("dbcheck")]
    //public async Task DebugAsync(NetCord.User user = null)
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
    //    await ReplyAsync("", false, this._embed).ConfigureAwait(false);
    //    this.Context.LogCommandUsed();
    //}


    [Command("serverdebug", "guilddebug", "debugserver", "debugguild")]
    [Summary("Returns server data")]
    public async Task DebugGuildAsync([CommandParameter(Remainder = true)] string guildId = null)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        guildId ??= this.Context.Guild.Id.ToString();

        if (!ulong.TryParse(guildId, out var discordGuildId))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter a valid discord guild id" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var guild = await guildService.GetGuildAsync(discordGuildId);

        if (guild == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Guild does not exist in database" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
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
        var guildFlagsOptions = new StringMenuProperties($"guild-flags:{guild.DiscordGuildId}")
            .WithPlaceholder("Select guild flags")
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

            guildFlagsOptions.AddOption(flagName, flagName, isActive, $"Toggle {flagName} flag");
        }

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
            .AddEmbeds(this._embed)
            .WithComponents([guildFlagsOptions]));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("issues")]
    [Summary("Toggles issue mode")]
    public async Task IssuesAsync([CommandParameter(Remainder = true)] string reason = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!PublicProperties.IssuesAtLastFm || reason != null)
            {
                PublicProperties.IssuesAtLastFm = true;
                PublicProperties.IssuesReason = reason;
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent("Enabled issue mode. This adds some warning messages, changes the bot status and disables full updates.\n" +
                        $"Reason given: *\"{reason}\"*")
                    .WithAllowedMentions(AllowedMentionsProperties.None));
            }
            else
            {
                PublicProperties.IssuesAtLastFm = false;
                PublicProperties.IssuesReason = null;
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Disabled issue mode" });
            }

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("leaveserver", "leaveguild")]
    [Summary("Makes the bot leave a server")]
    public async Task LeaveGuild([CommandParameter(Remainder = true)] string reason = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (!ulong.TryParse(reason, out var id))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Invalid guild ID" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                return;
            }

            var guildToLeave = await client.GetGuildAsync(id);
            await guildToLeave.LeaveAsync();

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Left guild (if the bot was in there)" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("banguild", "banserver")]
    [Summary("Bans a guild and makes the bot leave the server")]
    public async Task BanGuild([CommandParameter(Remainder = true)] string guildId = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (!ulong.TryParse(guildId, out var id))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Invalid guild ID" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                return;
            }

            // Check if guild exists in database
            var dbGuild = await guildService.GetGuildAsync(id);
            if (dbGuild == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Guild does not exist in database" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            // Add banned flag
            var currentFlags = dbGuild.GuildFlags ?? (GuildFlags)0;
            var newFlags = currentFlags | GuildFlags.Banned;
            await guildService.SetGuildFlags(dbGuild.GuildId, newFlags);

            // Leave the guild
            var guildToLeave = await client.GetGuildAsync(id);
            if (guildToLeave != null)
            {
                await guildToLeave.LeaveAsync();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Banned and left guild: {guildToLeave.Name} ({id})" });
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Guild banned but bot was not in the guild ({id})" });
            }

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("updategwfilter")]
    [Summary("Updates gwk quality filter")]
    public async Task UpdateGlobalWhoKnowsFilter([CommandParameter(Remainder = true)] string _ = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Starting gwk quality filter update.." });

            var filteredUsers = await whoKnowsFilterService.GetNewGlobalFilteredUsers();
            await whoKnowsFilterService.AddFilteredUsersToDatabase(filteredUsers);

            var description = new StringBuilder();

            description.AppendLine(
                $"Found {filteredUsers.Count(c => c.Reason == GlobalFilterReason.PlayTimeInPeriod)} users exceeding max playtime");
            description.AppendLine(
                $"Found {filteredUsers.Count(c => c.Reason == GlobalFilterReason.AmountPerPeriod)} users exceeding max amount");
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = description.ToString() });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("debuglogs")]
    [Summary("View user command logs")]
    public async Task DebugLogs([CommandParameter(Remainder = true)] string user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

                if (guild.SpecialGuild != true)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "This command can only be used in special guilds." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                    return;
                }

                user ??= this.Context.User.Id.ToString();

                var userToView = await settingService.GetDifferentUser(user);

                if (userToView == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                var logs = await adminService.GetRecentUserInteractions(userToView.UserId);

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

                var paginator = StringService.BuildComponentPaginator(pages);

                _ = this.Interactivity.SendPaginatorAsync(
                    paginator.Build(),
                    this.Context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("purgecache")]
    [Summary("Purges discord caches")]
    public async Task PurgeCacheAsync()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            if (client == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Client is null" });
                return;
            }

            var currentProcess = Process.GetCurrentProcess();
            var currentMemoryUsage = currentProcess.WorkingSet64;
            var reply = new StringBuilder();

            reply.AppendLine("Purged user cache and ran garbage collector.");
            reply.AppendLine($"Memory before purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            GC.Collect();

            await Task.Delay(2000);

            currentProcess = Process.GetCurrentProcess();
            currentMemoryUsage = currentProcess.WorkingSet64;

            reply.AppendLine($"Memory after purge: `{currentMemoryUsage.ToFormattedByteString()}`");

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = reply.ToString() });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("removeoldplays")]
    [Summary("Purges discord caches")]
    public async Task RemoveOldPlaysAsync()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var oldUsers = await indexService.GetUnusedUsers();
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content =
                $"Found {oldUsers.Count} users that haven't used fmbot in 3 months. I will now remove their cached scrobbles that are over a year and a half old." });

            await indexService.RemoveOldPlaysForUsers(oldUsers);
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content =
                "Done removing old cached scrobbles." });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("opencollectivesupporters", "ocsupporters")]
    [Summary("Displays all .fmbot supporters.")]
    public async Task OpenCollectiveSupportersAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await staticBuilders.OpenCollectiveSupportersAsync(
                    new ContextModel(this.Context, prfx, userSettings), extraOptions == "expired");

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    [Command("discordsupporters", "dsupporters", "dsupp", "discsupp")]
    [Summary("Displays all .fmbot supporters.")]
    public async Task DiscordSupportersAsync()
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        try
        {
            var response =
                await staticBuilders.DiscordSupportersAsync(new ContextModel(this.Context, prfx, userSettings));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("addalbum", "addcensoredalbum", "addnsfwalbum", "checkalbum")]
    [Summary("Manage album censoring")]
    [Examples("addcensoredalbum Death Grips No Love Deep Web")]
    public async Task AddAlbumAsync([CommandParameter(Remainder = true)] string albumValues)
    {
        try
        {
            if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
            var albumSearch = await albumService.SearchAlbum(new ResponseModel(), this.Context.User, albumValues,
                userSettings.UserNameLastFM, referencedMessage: this.Context.Message.ReferencedMessage);
            if (albumSearch.Album == null)
            {
                await this.Context.SendResponse(this.Interactivity, albumSearch.Response, userService);
                return;
            }

            var existingAlbum =
                await censorService.GetCurrentAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName);
            if (existingAlbum == null)
            {
                if (this.Context.Message.Content[..12].Contains("nsfw"))
                {
                    await censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.AlbumCoverNsfw);
                    this._embed.WithDescription(
                        $"Marked `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` as NSFW.");
                }
                else if (this.Context.Message.Content[..12].Contains("censored"))
                {
                    await censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.AlbumCoverCensored);
                    this._embed.WithDescription(
                        $"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the censored albums.");
                }
                else
                {
                    await censorService.AddAlbum(albumSearch.Album.AlbumName, albumSearch.Album.ArtistName,
                        CensorType.None);
                    this._embed.WithDescription(
                        $"Added `{albumSearch.Album.AlbumName}` by `{albumSearch.Album.ArtistName}` to the censored music list, however not banned anywhere.");
                }

                existingAlbum =
                    await censorService.GetCurrentAlbum(albumSearch.Album.AlbumName,
                        albumSearch.Album.ArtistName);
            }
            else
            {
                this._embed.WithDescription($"Showing existing album entry (no modifications made).");
            }

            var censorOptions = new StringMenuProperties($"admin-censor:{existingAlbum.CensoredMusicId}")
                .WithPlaceholder("Select censor types")
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

                    censorOptions.AddOption(name, value, active, description);
                }
            }

            this._embed.WithTitle("Album - Censor information");

            this._embed.AddField("Album name", existingAlbum.AlbumName);
            this._embed.AddField("Artist name", existingAlbum.ArtistName);
            this._embed.AddField("Times censored", (existingAlbum.TimesCensored ?? 0).ToString());
            this._embed.AddField("Types", censorDescription.ToString());

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([censorOptions]));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("managealias")]
    [Summary("Manage artist alias")]
    public async Task ManageArtistAlias([CommandParameter(Remainder = true)] string alias)
    {
        try
        {
            if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            var artistAlias = await aliasService.GetArtistAlias(alias);
            if (artistAlias == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Artist alias not found" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var aliasOptions = new StringMenuProperties($"artist-alias:{artistAlias.Id}")
                .WithPlaceholder("Select alias options")
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

                aliasOptions.AddOption(name, value, active, description);
            }

            this._embed.WithTitle("Artist alias - Option information");

            var artist = await artistsService.GetArtistForId(artistAlias.ArtistId);

            this._embed.AddField("Artist name", artist.Name);
            this._embed.AddField("Alias", artistAlias.Alias);
            this._embed.AddField("Types", censorDescription.ToString());

            this._embed.WithFooter($"Artist id {artistAlias.ArtistId}\n" +
                                   "Case insensitive\n" +
                                   "Aliases are cached for 5 minutes");

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([aliasOptions]));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("addartist", "addcensoredartist", "addnsfwartist", "addfeaturedban", "checkartist")]
    [Summary("Manage artist censoring")]
    [Examples("addcensoredartist Last Days of Humanity")]
    public async Task AddArtistAsync([CommandParameter(Remainder = true)] string artist)
    {
        try
        {
            if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            if (string.IsNullOrEmpty(artist))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter a correct artist to manage\n" +
                                 "Example: `.addartist \"Last Days of Humanity\"" });
                return;
            }

            artist = artist.Replace("\"", "");

            var existingArtist = await censorService.GetCurrentArtist(artist);
            if (existingArtist == null)
            {
                if (this.Context.Message.Content[..12].Contains("nsfw"))
                {
                    await censorService.AddArtist(artist, CensorType.ArtistAlbumsNsfw);
                    this._embed.WithDescription($"Added `{artist}` to the album nsfw marked artists.");
                }
                else if (this.Context.Message.Content[..12].Contains("censored"))
                {
                    await censorService.AddArtist(artist, CensorType.ArtistAlbumsCensored);
                    this._embed.WithDescription($"Added `{artist}` to the album censored artists.");
                }
                else if (this.Context.Message.Content[..12].Contains("featured"))
                {
                    await censorService.AddArtist(artist, CensorType.ArtistFeaturedBan);
                    this._embed.WithDescription($"Added `{artist}` to the list of featured banned artists.");
                }
                else
                {
                    await censorService.AddArtist(artist, CensorType.None);
                    this._embed.WithDescription(
                        $"Added `{artist}` to the censored music list, however not banned anywhere.");
                }

                existingArtist = await censorService.GetCurrentArtist(artist);
            }
            else
            {
                this._embed.WithDescription($"Showing existing artist entry (no modifications made).");
            }

            var censorOptions = new StringMenuProperties($"admin-censor:{existingArtist.CensoredMusicId}")
                .WithPlaceholder("Select censor types")
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

                    censorOptions.AddOption(name, value, active, description);
                }
            }

            this._embed.WithTitle("Artist - Censor information");

            this._embed.AddField("Name", existingArtist.ArtistName);
            this._embed.AddField("Times censored", (existingArtist.TimesCensored ?? 0).ToString());
            this._embed.AddField("Types", censorDescription.ToString());

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([censorOptions]));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("checkbotted", "checkbotteduser")]
    [Summary("Checks some stats for a user and if they're banned from global whoknows")]
    public async Task CheckBottedUserAsync(string user = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter an username to check\n" +
                                 "Example: `.fmcheckbotted Kefkef123`" });
                return;
            }

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var targetedUser = await settingService.GetUser(user, contextUser, this.Context);
            var targetedDate = (DateTime?)null;

            if (targetedUser.DifferentUser)
            {
                user = targetedUser.UserNameLastFm;
                targetedDate = targetedUser.RegisteredLastFm;
            }

            var bottedUser = await adminService.GetBottedUserAsync(user, targetedDate);
            var filteredUser = await adminService.GetFilteredUserAsync(user, targetedDate);
            var isBannedSomewhere = false;

            var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(user);

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

                var count = await dataSourceFactory.GetScrobbleCountFromDateAsync(user, timeFrom);

                var age = DateTimeOffset.FromUnixTimeSeconds(timeFrom);
                var totalDays = (DateTime.UtcNow - age).TotalDays;

                var avgPerDay = count / totalDays;
                this._embed.AddField("Avg scrobbles / day in last year", Math.Round(avgPerDay.GetValueOrDefault(0), 1).ToString(CultureInfo.InvariantCulture));
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

            ActionRowProperties components = null;
            if (filteredUser != null && bottedUser == null)
            {
                components = new ActionRowProperties().WithButton($"Convert to ban",
                    $"gwk-filtered-user-to-ban-{filteredUser.GlobalFilteredUserId}", style: ButtonStyle.Secondary);
            }

            this._embed.WithFooter("Command not intended for use in public channels");
            this._embed.WithColor(isBannedSomewhere
                ? DiscordConstants.WarningColorOrange
                : DiscordConstants.SuccessColorGreen);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents(components != null ? [components] : null));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("getusers")]
    [Examples("getusers frikandel_")]
    [GuildOnly]
    public async Task GetUsersForLastfmUserNameAsync(string userString = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild.SpecialGuild != true)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "This command can only be used in special guilds." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
                return;
            }

            if (string.IsNullOrEmpty(userString))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter a Last.fm username to get the accounts for." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var otherUser = await settingService.GetDifferentUser(userString);
            if (otherUser != null && otherUser.UserNameLastFM.ToLower() != userString.ToLower())
            {
                userString = otherUser.UserNameLastFM;
            }

            var users = await adminService.GetUsersWithLfmUsernameAsync(userString);

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

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("addbotted", "addbotteduser")]
    [Examples(".addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"")]
    public async Task AddBottedUserAsync(string user = null, [CommandParameter(Remainder = true)] string reason = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(reason))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter an username and reason to remove someone from gwk banlist\n" +
                                 "Example: `.addbotteduser \"Kefkef123\" \"8 days listening time in Last.week\"`" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var bottedUser = await adminService.GetBottedUserAsync(user);

            var userInfo = await dataSourceFactory.GetLfmUserInfoAsync(user);

            DateTimeOffset? age = null;
            if (userInfo != null && userInfo.Subscriber)
            {
                age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
            }

            if (bottedUser == null)
            {
                if (!await adminService.AddBottedUserAsync(user, reason, age?.DateTime))
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Something went wrong while adding this user to the gwk banlist" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                }
                else
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent($"User {user} has been banned from GlobalWhoKnows with reason `{reason}`" +
                            (age.HasValue ? " (+ join date so username change resilient)" : ""))
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                }
            }
            else
            {
                if (!await adminService.EnableBottedUserBanAsync(user, reason, age?.DateTime))
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Something went wrong while adding this user to the gwk banlist" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                }
                else
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent($"User {user} has been banned from GlobalWhoKnows with reason `{reason}`" +
                            (age.HasValue ? " (+ join date so username change resilient)" : ""))
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                }
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("removebotted", "removebotteduser")]
    [Examples("removebotteduser \"Kefkef123\"")]
    public async Task RemoveBottedUserAsync(string user = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (string.IsNullOrEmpty(user))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content =
                    "Enter an username to remove from the gwk banlist. This will flag their ban as `false`.\n" +
                    "Example: `.removebotteduser \"Kefkef123\"`" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var bottedUser = await adminService.GetBottedUserAsync(user);
            if (bottedUser == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "The specified user has never been banned from GlobalWhoKnows" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            if (!bottedUser.BanActive)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User is in banned user list, but their ban was already inactive" });
                return;
            }

            if (!await adminService.DisableBottedUserBanAsync(user))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "The specified user has not been banned from GlobalWhoKnows" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"User {user} has been unbanned from GlobalWhoKnows" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                return;
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("addsupporter")]
    public async Task AddSupporterAsync(string user = null, string openCollectiveId = null, string sendDm = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            const string formatError = "Make sure to follow the correct format when adding a supporter\n" +
                                       "`.addsupporter \"discordUserId\" \"open-collective-id\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\"`\n\n" +
                                       "If you don't want the bot to send a thank you dm, add `\"nodm\"`\n" +
                                       "`.addsupporter \"278633844763262976\" \"03k0exgz-nm8yj64a-g4965wao-9r7b4dlv\" \"nodm\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(openCollectiveId) || user == "help")
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
                return;
            }

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;
            var userSettings = await userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`User not found`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            if (userSettings.UserType != UserType.User &&
                !await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`Can only change usertype of normal users`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var openCollectiveSupporter = await supporterService.GetOpenCollectiveSupporter(openCollectiveId);
            if (openCollectiveSupporter == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`OpenCollective user not found`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var existingSupporters = await supporterService.GetAllSupporters();
            if (existingSupporters
                    .Where(w => w.OpenCollectiveId != null)
                    .FirstOrDefault(f => f.OpenCollectiveId.ToLower() == openCollectiveId.ToLower()) != null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`OpenCollective account already connected to someone else`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var supporter =
                await supporterService.AddOpenCollectiveSupporter(userSettings.DiscordUserId,
                    openCollectiveSupporter);

            await supporterService.ModifyGuildRole(userSettings.DiscordUserId);

            this._embed.WithTitle("Added new supporter");
            var description = new StringBuilder();
            description.AppendLine($"User id: {user} | <@{user}>\n" +
                                   $"Name: **{supporter.Name}**\n" +
                                   $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            description.AppendLine();
            description.AppendLine("✅ Full update started");

            this._embed.WithFooter("Name changes go through OpenCollective and apply within 24h");

            var discordUser = await this.Context.GetUserAsync(discordUserId);
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

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

            await indexService.IndexUser(userSettings);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("sendsupporterwelcome", "sendwelcomedm")]
    public async Task SendWelcomeDm(string user = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!ulong.TryParse(user, out var discordUserId))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Wrong discord user id format" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var userSettings = await userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User does not exist in database" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var supporter = await supporterService.GetSupporter(discordUserId);
            var discordUser = await this.Context.GetUserAsync(discordUserId);

            if (discordUser == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Discord user not found" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            await SupporterService.SendSupporterWelcomeMessage(discordUser, userSettings.UserDiscogs != null,
                supporter);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "✅ Thank you dm sent" });
        }
    }

    [Command("sendsupportergoodbye", "sendgoodbyedm")]
    public async Task SendGoodbyeDm(string user = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            if (!ulong.TryParse(user, out var discordUserId))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Wrong discord user id format" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var discordUser = await this.Context.GetUserAsync(discordUserId);

            if (discordUser == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Discord user not found" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var userSettings = await userService.GetUserAsync(discordUserId);

            var hasImported = userSettings != null && userSettings.DataSource != DataSource.LastFm;

            await SupporterService.SendSupporterGoodbyeMessage(discordUser, hasImported);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "✅ Goodbye dm sent" });
        }
    }

    [Command("removesupporter")]
    public async Task RemoveSupporterAsync(string user = null, string sendDm = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when removing a supporter\n" +
                              "`.removesupporter \"discord-user-id\"`";

            if (string.IsNullOrEmpty(user) || user == "help")
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
                return;
            }

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var userSettings = await userService.GetUserAsync(discordUserId);

            if (userSettings == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`User not found`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            if (userSettings.UserType != UserType.Supporter)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`User is not a supporter`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var stripeSupporter = await supporterService.GetStripeSupporter(userSettings.DiscordUserId);
            if (stripeSupporter != null)
            {
                await supporterService.CheckExpiredStripeSupporters(userSettings.DiscordUserId);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Ran fast-tracked Stripe supporter expiry for " + stripeSupporter.PurchaserDiscordUserId });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
                return;
            }

            var hadImported = userSettings.DataSource != DataSource.LastFm;

            var existingSupporter = await supporterService.GetSupporter(discordUserId);
            if (existingSupporter == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`Existing supporter not found`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            if (existingSupporter.SubscriptionType != SubscriptionType.MonthlyOpenCollective &&
                existingSupporter.SubscriptionType != SubscriptionType.YearlyOpenCollective)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You can only use this command on Stripe subs or on OpenCollective monthly and yearly subs" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var supporter = await supporterService.OpenCollectiveSupporterExpired(existingSupporter);

            var removedRole = false;
            if (this.Context.Guild.Id == this._botSettings.Bot.BaseServerId)
            {
                try
                {
                    var guildUser = await this.Context.Guild.GetUserAsync(discordUserId);
                    if (guildUser != null)
                    {
                        var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Value.Name == "Supporter");
                        await guildUser.RemoveRoleAsync(role.Value.Id);
                        removedRole = true;
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "Removing supporter role failed for {id}", discordUserId);
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

            var discordUser = await this.Context.GetUserAsync(discordUserId);
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

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

            await indexService.IndexUser(userSettings);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("addsupporterclassic")]
    [Examples("addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"",
        "addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"")]
    public async Task AddSupporterClassicAsync(string user = null, string name = null, string internalNotes = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var formatError = "Make sure to follow the correct format when adding a supporter\n" +
                              "Examples: \n" +
                              "`.addsupporter \"125740103539621888\" \"Drasil\" \"lifetime supporter\"`\n" +
                              "`.addsupporter \"278633844763262976\" \"Aetheling\" \"monthly supporter (perm at 28-11-2021)\"`";

            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(internalNotes) || string.IsNullOrEmpty(name) ||
                user == "help")
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
                return;
            }

            if (!ulong.TryParse(user, out var userId))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            var userSettings = await userService.GetUserAsync(userId);

            if (userSettings == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`User not found`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            if (userSettings.UserType != UserType.User)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "`Can only change usertype of normal users`\n\n" + formatError });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            await supporterService.AddSupporter(userSettings.DiscordUserId, name, internalNotes);

            this._embed.WithDescription("Supporter added.\n" +
                                        $"User id: {user} | <@{user}>\n" +
                                        $"Name: **{name}**\n" +
                                        $"Internal notes: `{internalNotes}`");

            this._embed.WithFooter("Command not intended for use in public channels");

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("featuredoverride"), Summary("Changes the avatar to be an album.")]
    [Examples("featuredoverride \"imageurl\" \"description\" true")]
    public async Task FeaturedOverrideAsync(string url, string desc, bool stopTimer = false)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                await featuredService.CustomFeatured(timer.CurrentFeatured, desc, url);

                if (stopTimer)
                {
                    RecurringJob.TriggerJob(nameof(timer.CheckForNewFeatured));
                    await Task.Delay(5000);
                    RecurringJob.RemoveIfExists(nameof(timer.CheckForNewFeatured));
                }
                else
                {
                    RecurringJob.TriggerJob(nameof(timer.CheckForNewFeatured));
                }

                var description = new StringBuilder();
                description.AppendLine($"Avatar: {url}");
                description.AppendLine($"Description: {desc}");
                description.AppendLine($"Timer stopped: {stopTimer}");

                this._embed.WithTitle("Featured override");
                this._embed.WithDescription(description.ToString());
                this._embed.WithFooter(
                    "You might also have to edit the next few hours in the database (with no update true)");

                await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only .fmbot owners can set featured." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("updateavatar"), Summary("Changes the avatar to given url.")]
    public async Task UpdateAvatar(string url)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                await webhookService.ChangeToNewAvatar(client, url);

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Changed avatar to {url}" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only .fmbot owners can change avatar." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("reconnectshard", "reconnectshards")]
    [Summary("Reconnects a shard")]
    [GuildOnly]
    [ExcludeFromHelp]
    [Examples("shard 0", "shard 821660544581763093")]
    public async Task ReconnectShardAsync(ulong? guildId = null)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        if (!guildId.HasValue)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Content = $"Enter a server id please (this server is `{this.Context.Guild.Id}`)"
            });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var client1 = client;

        GatewayClient shard = null;

        if (guildId is < 1000 and >= 0)
        {
            var shardIndex = (int)guildId.Value;
            if (shardIndex < client1.Count)
            {
                shard = client1[shardIndex];
            }
        }
        else
        {
            shard = client1.FirstOrDefault(s => s.Cache.Guilds.ContainsKey(guildId.Value));
        }

        if (shard != null)
        {
            var shardId = shard.Shard?.Id ?? 0;
            if (shard.Status != WebSocketStatus.Ready)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Connecting Shard #{shardId}" });
                await shard.StartAsync();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Connected Shard #{shardId}" });
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Shard #{shardId} is not in a disconnected state." });
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Content = "Server or shard could not be found. \n" +
                          "This either means the bot is not connected to that server or that the bot is not in this server."
            });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("postembed"), Summary("Posts one of the reporting embeds")]
    [Examples("postembed \"gwkreporter\"")]
    public async Task PostAdminEmbed([CommandParameter(Remainder = true)] string type = null)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "No permissions mate" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        if (string.IsNullOrWhiteSpace("type"))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Pick an embed type that you want to post. Currently available: `rules`, `gwkreporter`, `nsfwreporter` and `buysupporter`" });
            return;
        }

        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        if (type == "rules")
        {
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

            var containers = new List<ComponentContainerProperties>
            {
                new()
                {
                    AccentColor = DiscordConstants.InformationColorBlue,
                    Components =
                    [
                        new TextDisplayProperties(
                            "Welcome to the .fmbot support server!\n" +
                            "Get help with .fmbot, chat with other music enthusiasts and more."),
                        new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                        new TextDisplayProperties(@"## 📜 Server rules:
1. **Be nice to each other.** Treat everyone with respect. No bigotry allowed, including but not limited to racism, sexism, homophobia, transphobia, ableism, and use of slurs (reclaimed or otherwise).
2. **Remember that music taste is subjective.** Criticism is fine, music elitism is not. Respect the music taste of others.
3. **Don't be spammy or annoying in general.** This can include repeatedly interrupting others, flooding the chat, immaturity, personal attacks, instigating or prolonging drama, and other behaviour that sucks the air out of the chat.
4. **No self promotion or marketing.** Established community members may do it in moderation. DM ads are never allowed.
5. **Safe for work content and chat only.** Keep it family friendly. An exception is made for spoilered album covers.
6. **English only.** We can't moderate other languages.
7. **Don't mention or DM other members without reason.** Mentions and DMs should be for a purpose, such as continuing conversations.
8. **No political discussion**, except for when it directly relates to music. This includes social issues or discussions about religion.

Not following these rules might lead to a mute, kick or ban. Staff members can ban, kick or mute you for any reason if they feel it is needed."),
                        new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                        new TextDisplayProperties(@"Role pings:
- Ping <@&1083762942144688198> for issues that require immediate staff attention, like someone disrupting the server or a raid
- Ping <@&1083762904924434442> if the bot is down (not responding)

For anything else, you must use <#856212952305893376> and after that ask in <#1006526334316576859>. Someone will get to you when they have time."),
                        new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                        new TextDisplayProperties(@"## 🔗 Links:
- Documentation: <https://fm.bot/>
- Link to this server: <http://discord.gg/fmbot>
- Bluesky: <https://bsky.app/profile/fm.bot>
- Feature requests or bug reports: <#1006526334316576859> or [GitHub](<https://github.com/fmbot-discord/fmbot/issues/new/choose>)
- Frequently Asked Questions: <#856212952305893376>
- Configure your notification roles in <id:customize>"),
                        new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                        new ComponentSectionProperties(new LinkButtonProperties(
                            "https://discord.com/oauth2/authorize?client_id=356268235697553409&permissions=275415092288&scope=applications.commands%20bot",
                            "Add to server"))
                        {
                            Components = [new TextDisplayProperties("### Get .fmbot in your server")]
                        },
                        new ComponentSectionProperties(new LinkButtonProperties(
                            "https://discord.com/oauth2/authorize?client_id=356268235697553409&scope=applications.commands&integration_type=1",
                            "Add to account"))
                        {
                            Components = [new TextDisplayProperties("### Use .fmbot slash commands everywhere")]
                        },
                        new ComponentSeparatorProperties { Spacing = ComponentSeparatorSpacingSize.Large },
                        new ComponentSectionProperties(new ButtonProperties(
                            InteractionConstants.SupporterLinks.GeneratePurchaseButtons(true, false,
                                false, source: "rulespromo"),
                            "Get .fmbot supporter", ButtonStyle.Primary))
                        {
                            Components = [new TextDisplayProperties("### Support us and unlock extra perks")]
                        }
                    ]
                },
                new()
                {
                    AccentColor = DiscordConstants.InformationColorBlue,
                    Components =
                    [
                        new TextDisplayProperties("## 🚨 NSFW and NSFL artwork report form"),
                        new TextDisplayProperties(
                            "Found album artwork or an artist image that should be marked NSFW or censored entirely? Please report that here. \n\n" +
                            "Note that artwork is censored according to Discord guidelines and only as required by Discord. .fmbot is fundamentally opposed to artistic censorship."),
                        new TextDisplayProperties("**Marked NSFW**\n" +
                                               "Frontal nudity [genitalia, exposed anuses, and 'female presenting nipples,' which is not our terminology], furry art in an erotic context and people covered in blood and/or wounds"),
                        new TextDisplayProperties(
                            "**Fully censored / NSFL**\n" +
                            "Hate speech [imagery or text promoting prejudice against a group], gore [detailed, realistic, or semi realistic depictions of viscera or extreme bodily harm, not blood alone] and pornographic content [depictions of sex]"),
                        new ComponentSeparatorProperties(),
                        new ActionRowProperties()
                            .WithButton("Report artist image", style: ButtonStyle.Secondary,
                                customId: InteractionConstants.ModerationCommands.ReportArtist)
                            .WithButton("Report album cover", style: ButtonStyle.Secondary,
                                customId: InteractionConstants.ModerationCommands.ReportAlbum)
                    ]
                },
                new()
                {
                    AccentColor = DiscordConstants.InformationColorBlue,
                    Components =
                    [
                        new TextDisplayProperties("## 🌐 GlobalWhoKnows report form"),
                        new TextDisplayProperties(globalWhoKnowsDescription.ToString()),
                        new ComponentSeparatorProperties(),
                        new ActionRowProperties()
                            .WithButton("Report user", style: ButtonStyle.Secondary,
                                customId: InteractionConstants.ModerationCommands.GlobalWhoKnowsReport)
                    ]
                }
            };

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Components = containers,
                Flags = MessageFlags.IsComponentsV2,
                AllowedMentions = AllowedMentionsProperties.None
            });
        }

        if (type == "buysupporter")
        {
            var description = new StringBuilder();
            description.AppendLine(
                "Use the button below to get started.");
            this._embed.WithDescription(description.ToString());

            var components = new ActionRowProperties().WithButton("Get .fmbot supporter", style: ButtonStyle.Primary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(true, false, false,
                    source: "embedpromo"));
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([components]));
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

            var components = new ActionRowProperties().WithButton("Report user", style: ButtonStyle.Secondary,
                customId: InteractionConstants.ModerationCommands.GlobalWhoKnowsReport);
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([components]));
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

            var components = new ActionRowProperties()
                .WithButton("Report artist image", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.ModerationCommands.ReportArtist)
                .WithButton("Report album cover", style: ButtonStyle.Secondary,
                    customId: InteractionConstants.ModerationCommands.ReportAlbum);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(this._embed)
                .WithComponents([components]));
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
    //            GatewayClient client = this.Context.Client as GatewayClient;

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
    //            GatewayClient client = this.Context.Client as GatewayClient;
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
    //            GatewayClient client = this.Context.Client as GatewayClient;
    //            _timer.UseDefaultAvatar(client);
    //            await ReplyAsync("Set avatar to 'FMBot Default'");
    //        }
    //        catch (Exception e)
    //        {
    //            GatewayClient client = this.Context.Client as GatewayClient;
    //            ExceptionReporter.ReportException(client, e);
    //            await ReplyAsync("The timer service cannot be loaded. Please wait for the bot to fully load.");
    //        }
    //    }
    //}

    [Command("resetfeatured", "restarttimer", "timerstart", "timerrestart")]
    [Summary("Restarts the featured timer.")]
    public async Task RestartTimerAsync([CommandParameter(Remainder = true)] int? id = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                var feature = timer.CurrentFeatured;

                if (id.HasValue)
                {
                    feature = await featuredService.GetFeaturedForId(id.Value);
                }

                var updateDescription = new StringBuilder();
                updateDescription.AppendLine("**Selected feature**");
                updateDescription.AppendLine(feature.Description);
                updateDescription.AppendLine(feature.ImageUrl);
                updateDescription.AppendLine();

                var newFeature = await featuredService.ReplaceFeatured(feature, this.Context.User.Id);

                updateDescription.AppendLine("**New feature**");
                updateDescription.AppendLine(newFeature.Description);
                updateDescription.AppendLine(newFeature.ImageUrl);
                updateDescription.AppendLine();

                updateDescription.AppendLine(
                    "Featured timer restarted. Can take up to three minutes to show, max 3 times / hour");

                var dateValue = ((DateTimeOffset)feature.DateTime).ToUnixTimeSeconds();
                this._embed.AddField("Time", $"<t:{dateValue}:F>");

                this._embed.WithDescription(updateDescription.ToString());
                await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only .fmbot staff can restart timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("picknewfeatureds")]
    [Summary("Runs the job that picks new featureds manually.")]
    public async Task StopTimerAsync()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Started pick new featured job" });
                await timer.PickNewFeatureds();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Finished pick new featured job" });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("checknewsupporters")]
    [Summary("Runs the job that checks for new OpenCollective supporters.")]
    public async Task CheckNewSupporters()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await timer.CheckForNewOcSupporters();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Checked for new oc supporters" });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = Constants.FmbotStaffOnly });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("checkdiscordsupporterusers")]
    [Summary("Updates all discord supporters")]
    public async Task CheckDiscordSupporterUserTypes()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Fetching supporters from Discord..." });
                await supporterService.CheckIfDiscordSupportersHaveCorrectUserType();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Updated all Discord supporters" });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("checksupporterroles")]
    [Summary("Updates all discord supporters")]
    public async Task CheckDiscordSupporterRoles()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;
                var activeSupporters = await supporterService.GetAllVisibleSupporters();
                var guildMembers = new List<GuildUser>();
                await foreach (var user in this.Context.Guild.GetUsersAsync())
                {
                    guildMembers.Add(user);
                }

                var discordUserIds = activeSupporters
                    .Where(w => w.DiscordUserId.HasValue)
                    .Select(s => s.DiscordUserId.Value)
                    .ToHashSet();

                var count = 0;
                var role = this.Context.Guild.Roles.FirstOrDefault(x => x.Value.Name == "Supporter");

                var reply = new StringBuilder();

                foreach (var member in guildMembers.Where(w => discordUserIds.Contains(w.Id)))
                {
                    if (member.RoleIds.All(a => a != role.Value.Id))
                    {
                        await member.AddRoleAsync(role.Key);
                        reply.AppendLine($"{member.Id} - <@{member.Id}> - {member.GetDisplayName()}");

                        count++;
                    }
                }

                reply.AppendLine();
                reply.AppendLine($"Updated all Discord supporters.");
                reply.AppendLine($"{count} users didn't have the supporter role when they should have had it.");

                var allSupporters = await supporterService.GetAllSupporters();
                var expiredOnly = allSupporters
                    .Where(w => w.DiscordUserId.HasValue)
                    .GroupBy(g => g.DiscordUserId)
                    .Where(w => w.All(a => a.Expired == true))
                    .Select(s => s.Key).ToHashSet();

                reply.AppendLine();
                reply.AppendLine("Check if these should have it (nvm, just ignore this):");
                foreach (var member in guildMembers.Where(w => expiredOnly.Contains(w.Id)))
                {
                    if (member.RoleIds.Any(a => a == role.Key))
                    {
                        reply.AppendLine($"{member.Id} - <@{member.Id}> - {member.GetDisplayName()}");
                    }
                }


                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = reply.ToString() });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("updatediscordsupporter")]
    [Summary("Updates single discord supporter")]
    public async Task UpdateSingleDiscordSupporters([CommandParameter(Remainder = true)] string user)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                var userToUpdate = await settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                await supporterService.UpdateSingleDiscordSupporter(userToUpdate.DiscordUserId);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Updated single discord supporter" });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("updatemultidiscordsupporters")]
    [Summary("Updates multiple discord supporter")]
    public async Task UpdateMultipleDiscordSupporters([CommandParameter(Remainder = true)] string user)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                var idsToUpdate = user.Split(",");
                var updated = new StringBuilder();
                var skipped = new StringBuilder();

                foreach (var id in idsToUpdate)
                {
                    var userToUpdate = await settingService.GetDifferentUser(user);

                    if (userToUpdate == null)
                    {
                        skipped.AppendLine($"{id} - <@{id}>");
                        continue;
                    }

                    await supporterService.UpdateSingleDiscordSupporter(userToUpdate.DiscordUserId);
                    updated.AppendLine($"{id} - <@{id}>");
                }

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent("Updated:\n" +
                        $"{updated}\n" +
                        $"Skipped:\n" +
                        $"{skipped}")
                    .WithAllowedMentions(AllowedMentionsProperties.None));
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("refreshpremiumservers")]
    [Summary("Refreshes cached premium servers")]
    public async Task RefreshPremiumGuilds()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                await guildService.RefreshPremiumGuilds();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Refreshed premium server cache dictionary" });
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("runtimer", "triggerjob", "runjob")]
    [Summary("Run a timer manually (only works if it exists)")]
    public async Task RunTimerAsync([CommandParameter(Remainder = true)] string job = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                if (job == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Pick a job to run. Check `.timerstatus` for available jobs." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                if (job == "masterjobs")
                {
                    timer.QueueMasterJobs();
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Added masterjobs" });
                    return;
                }

                var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

                var jobToRun = recurringJobs.FirstOrDefault(f => f.Id.ToLower() == job.ToLower());

                if (jobToRun == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Could not find job you're looking for. Check `.timerstatus` for available jobs." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                RecurringJob.TriggerJob(jobToRun.Id);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent($"Triggered job {jobToRun.Id}")
                    .WithAllowedMentions(AllowedMentionsProperties.None));

                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can stop timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("removetimer", "removejob", "deletejob")]
    [Summary("Remove a timer manually (only works if it exists)")]
    public async Task RemoveJobAsync([CommandParameter(Remainder = true)] string job = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            try
            {
                if (job == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Pick a job to remove. Check `.timerstatus` for available jobs." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var recurringJobs = JobStorage.Current.GetConnection().GetRecurringJobs();

                var jobToRemove = recurringJobs.FirstOrDefault(f => f.Id.ToLower() == job.ToLower());

                if (jobToRemove == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Could not find job you're looking for. Check `.timerstatus` for available jobs." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                RecurringJob.RemoveIfExists(jobToRemove.Id);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent($"Removed job {jobToRemove.Id}")
                    .WithAllowedMentions(AllowedMentionsProperties.None));

                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot owners can remove jobs." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("timerstatus")]
    [Summary("Checks the status of the timer.")]
    public async Task TimerStatusAsync()
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
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

                await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e, userService);
            }
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Error: Insufficient rights. Only FMBot admins can check timer." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
        }
    }

    [Command("globalblockadd", "globalblocklistadd", "globalblacklistadd")]
    [Summary("Blocks a user from using .fmbot.")]
    public async Task BlacklistAddAsync(ulong? user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!user.HasValue)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please specify what discord user id you want to block from using .fmbot." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                if (user == this.Context.Message.Author.Id)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You cannot block yourself from using .fmbot!" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var blacklistResult = await adminService.AddUserToBlocklistAsync(user.Value);

                if (blacklistResult)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent("Blocked " + user +
                            " from using .fmbot. Cached up to 5 minutes, applies to their Last.fm username globally.")
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                }
                else
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent("You have already added " + user +
                            " to the list of blocked users or something went wrong.")
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                }

                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("globalblockremove", "globalblocklistremove", "globalblacklistremove")]
    [Summary("Unblocks a user so they can use .fmbot again.")]
    public async Task BlackListRemoveAsync(ulong? user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (!user.HasValue)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please specify what user you want to remove from the list of users who are blocked from using .fmbot." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var blacklistResult = await adminService.RemoveUserFromBlocklistAsync(user.Value);

                if (blacklistResult)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent("Removed " + user +
                            " from the list of users who are blocked from using .fmbot. Cached up to 5 minutes, applies to their Last.fm username globally.")
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                }
                else
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                        .WithContent("You have already removed " + user +
                            " from the list of users who are blocked from using the bot.")
                        .WithAllowedMentions(AllowedMentionsProperties.None));
                }

                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("runfullupdate")]
    [Summary("Runs a full update for someone else")]
    public async Task RunFullUpdate([CommandParameter(Remainder = true)] string user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var userToUpdate = await settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent($"Running full update for '{userToUpdate.UserNameLastFM}'")
                    .WithAllowedMentions(AllowedMentionsProperties.None));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

                await indexService.IndexUser(userToUpdate);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("runtoplistupdate")]
    [Summary("Runs a toplist update for someone else")]
    public async Task RunTopListUpdate([CommandParameter(Remainder = true)] string user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var userToUpdate = await settingService.GetDifferentUser(user);

                if (userToUpdate == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .WithContent($"Running top list update for '{userToUpdate.UserNameLastFM}'")
                    .WithAllowedMentions(AllowedMentionsProperties.None));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

                await indexService.RecalculateTopLists(userToUpdate);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("supporterlink")]
    public async Task GetSupporterTestLink([CommandParameter(Remainder = true)] string user = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                var components = new ActionRowProperties()
                    .WithButton("Get .fmbot supporter",
                        $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:true:false:true:testlink",
                        ButtonStyle.Primary);

                var embed = new EmbedProperties();
                embed.WithDescription("Start the new purchase flow below");
                embed.WithColor(DiscordConstants.InformationColorBlue);

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .AddEmbeds(embed)
                    .WithComponents([components]));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("importdebug")]
    [Summary("Debug your import playcount")]
    [Options("Artist name")]
    public async Task ImportDebug([CommandParameter(Remainder = true)] string user = null)
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var userSettings = await settingService.GetUser(user, contextUser, this.Context);

            if (!SupporterService.IsSupporter(userSettings.UserType))
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You can only debug imports for supporters." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.SupporterRequired }, userService);
                return;
            }

            var dbUser = await settingService.GetDifferentUser(userSettings.DiscordUserId.ToString());

            if (dbUser == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            var allPlays = await playService.GetAllUserPlays(dbUser.UserId, false);
            var allFinalizedPlays = await playService.GetAllUserPlays(dbUser.UserId, true);

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

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("movedata")]
    [Summary("Move imports, streaks, featured logs and users that have them as friends from one user to another")]
    public async Task MoveData(string oldUserId = null, string newUserId = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (oldUserId == null || newUserId == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter the old and new id. For example, `.moveimports 125740103539621888 356268235697553409`" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var oldUser = await settingService.GetDifferentUser(oldUserId);
                var newUser = await settingService.GetDifferentUser(newUserId);

                if (oldUser == null || newUser == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "One or both users could not be found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                var embed = new EmbedProperties();

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

                var components = new ActionRowProperties().WithButton("Move data",
                    customId: $"move-user-data:{oldUser.UserId}:{newUser.UserId}", style: ButtonStyle.Danger);

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .AddEmbeds(embed)
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithComponents([components]));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("movesupporter")]
    [Summary("Move stripe supporter from one account to another")]
    public async Task MoveSupporter(string oldUserId = null, string newUserId = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                if (oldUserId == null || newUserId == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter the old and new id. For example, `.movesupporter 125740103539621888 356268235697553409`" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var oldUser = await settingService.GetDifferentUser(oldUserId);
                var newUser = await settingService.GetDifferentUser(newUserId);

                if (oldUser == null || newUser == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "One or both users could not be found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                if (oldUser.UserType != UserType.Supporter)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Old user is not a supporter" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                var stripeSupporter = await supporterService.GetStripeSupporter(oldUser.DiscordUserId);

                if (stripeSupporter == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Old user is not a Stripe supporter. At the moment only transferring Stripe supporters is supported." });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var embed = new EmbedProperties();

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

                var components = new ActionRowProperties().WithButton("Move supporter",
                    customId: $"move-supporter:{oldUser.DiscordUserId}:{newUser.DiscordUserId}", style: ButtonStyle.Danger);

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .AddEmbeds(embed)
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithComponents([components]));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("deleteuser", "removeuser")]
    [Summary("Remove a user")]
    public async Task DeleteUser(string userToDelete = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner) &&
                this.Context.Guild.Id == 821660544581763093)
            {
                if (userToDelete == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Enter the user to delete. For example, `.deleteuser 125740103539621888`" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                    return;
                }

                var user = await settingService.GetDifferentUser(userToDelete);

                if (user == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User could not be found. Are you sure they are registered in .fmbot?" });
                    await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                    return;
                }

                var embed = new EmbedProperties();

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

                var components = new ActionRowProperties().WithButton("Delete user",
                    customId: $"admin-delete-user:{user.UserId}", style: ButtonStyle.Danger);

                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                    .AddEmbeds(embed)
                    .WithAllowedMentions(AllowedMentionsProperties.None)
                    .WithComponents([components]));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You are not authorized to use this command, or you're in the wrong server." });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("lastfmissue", "lfmissue")]
    public async Task LastfmIssue(string _ = null)
    {
        if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            var embed = new EmbedProperties();
            embed.WithDescription(
                "It looks like you asked for help with a Last.fm issue, and not an .fmbot issue.\n\n" +
                ".fmbot is not affiliated with Last.fm, the bot and the website are two different things.\n\n" +
                "Generally speaking we can't help with Last.fm issues, but we and other members of the community might still be able to offer suggestions. You can also consider asking the two communities linked below.");

            var components = new ActionRowProperties()
                .WithButton("Last.fm support forums", url: "https://support.last.fm/")
                .WithButton("Last.fm Discord", url: "https://discord.gg/lastfm");

            embed.WithColor(DiscordConstants.LastFmColorRed);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties()
                .AddEmbeds(embed)
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithComponents([components]));

            if (this.Context.Channel is GuildThread threadChannel &&
                this.Context.Guild != null &&
                threadChannel.ParentId.HasValue &&
                this.Context.Guild.Channels.TryGetValue(threadChannel.ParentId.Value, out var parentChannel) &&
                parentChannel is ForumGuildChannel forumChannel &&
                forumChannel.AvailableTags.Any())
            {
                var tagToApply = forumChannel.AvailableTags.FirstOrDefault(f => f.Name == "Last.fm issue");
                if (tagToApply != null)
                {
                    await threadChannel.ModifyAsync(m => m.AppliedTags = [tagToApply.Id]);
                }
            }
        }
    }

    [Command("banshortlisteners")]
    public async Task BanShortListeners([CommandParameter(Remainder = true)] string trackValues = null)
    {
        try
        {
            if (await adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                var response = new ResponseModel
                {
                    ResponseType = ResponseType.Embed,
                };

                var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

                var shortTrack = await trackService.SearchTrack(response, this.Context.User, trackValues,
                    contextUser.UserNameLastFM);
                if (shortTrack.Track == null)
                {
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Couldn't find track" });
                    return;
                }

                var usersWithTrack = await whoKnowsTrackService.GetGlobalUsersForTrack(this.Context.Guild,
                    shortTrack.Track.ArtistName, shortTrack.Track.TrackName);

                var bannedUsers = new StringBuilder();
                var bannedUserCount = 0;
                var manualCheckUsers = new StringBuilder();

                const int playThreshold = 5000;

                usersWithTrack = usersWithTrack.Where(w => w.Playcount >= playThreshold).ToList();
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = $"Checking and banning {usersWithTrack.Count} users with over {playThreshold} plays on {shortTrack.Track.TrackName} by {shortTrack.Track.ArtistName}" });

                foreach (var userToCheck in usersWithTrack.Where(w => w.Playcount >= playThreshold))
                {
                    var bottedUser =
                        await adminService.GetBottedUserAsync(userToCheck.LastFMUsername,
                            userToCheck.RegisteredLastFm);
                    if (bottedUser != null)
                    {
                        continue;
                    }

                    var currentPlaycount = await dataSourceFactory.GetTrackInfoAsync(shortTrack.Track.TrackName,
                        shortTrack.Track.ArtistName,
                        userToCheck.LastFMUsername);
                    if (currentPlaycount.Success && currentPlaycount.Content.UserPlaycount >= playThreshold)
                    {
                        var reason = $"Semi-automated ban for short track spam\n" +
                                     $"{shortTrack.Track.TrackName} by {shortTrack.Track.ArtistName}\n" +
                                     $"{currentPlaycount.Content.UserPlaycount} plays";
                        await adminService.AddBottedUserAsync(userToCheck.LastFMUsername, reason);
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

                await this.Context.SendResponse(this.Interactivity, response, userService);
                await this.Context.LogCommandUsedAsync(response, userService);
            }
            else
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Only bot owners can do this command" });
                return;
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("postpendingreports")]
    public async Task PostPendingReports([CommandParameter(Remainder = true)] string trackValues = null)
    {
        if (!await adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Reposting open reports..." });

        await using var db = await contextFactory.CreateDbContextAsync();
        var musicReports = db.CensoredMusicReport
            .Where(w => w.ReportStatus == ReportStatus.Pending)
            .Include(i => i.Album)
            .Include(i => i.Artist)
            .ToList();

        foreach (var report in musicReports)
        {
            await censorService.PostReport(report);
        }

        var userReports = db.BottedUserReport.Where(w => w.ReportStatus == ReportStatus.Pending).ToList();
        foreach (var report in userReports)
        {
            await adminService.PostReport(report);
        }

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Done" });
    }
}
