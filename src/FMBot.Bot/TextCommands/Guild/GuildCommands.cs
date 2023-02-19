using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.Guild;

[Name("Server settings")]
[ServerStaffOnly]
public class GuildCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly GuildService _guildService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly CommandService _service;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IMemoryCache _cache;

    private readonly IPrefixService _prefixService;
    private readonly IGuildDisabledCommandService _guildDisabledCommandService;
    private readonly IChannelDisabledCommandService _channelDisabledCommandService;

    private readonly CommandService _commands;

    private InteractiveService Interactivity { get; }

    public GuildCommands(IPrefixService prefixService,
        GuildService guildService,
        CommandService commands,
        AdminService adminService,
        IGuildDisabledCommandService guildDisabledCommandService,
        IChannelDisabledCommandService channelDisabledCommandService,
        SettingService settingService,
        IOptions<BotSettings> botSettings,
        CommandService service,
        IMemoryCache cache,
        GuildSettingBuilder guildSettingBuilder,
        UserService userService,
        InteractiveService interactivity) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._commands = commands;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._settingService = settingService;
        this._service = service;
        this._cache = cache;
        this._guildSettingBuilder = guildSettingBuilder;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._adminService = adminService;
    }


    [Command("serversettings", RunMode = RunMode.Async)]
    [Summary("Shows all the server settings")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    [Alias("ss")]
    public async Task GuildSettingsAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(searchValues, contextUser, this.Context);

        try
        {
            var response = await this._guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, "/", contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("keepdata", RunMode = RunMode.Async)]
    [Summary("Allows you to keep your server data when removing the bot from your server")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task KeepDataAsync(params string[] otherSettings)
    {
        this._cache.Set($"{this.Context.Guild.Id}-keep-data", true, TimeSpan.FromMinutes(5));

        await ReplyAsync(
            "You can now kick this bot from your server in the next 5 minutes without losing the stored .fmbot data, like server settings and crown history.\n\n" +
            "If you still wish to remove all server data from the bot you can kick the bot after the time period is over.");
    }

    [Command("servermode", RunMode = RunMode.Async)]
    [Summary("Sets the forced .fm mode for the server.\n\n" +
             "To view current settings, use `{{prfx}}servermode info`")]
    [Options("Modes: embedtiny/embedmini/embedfull/textmini/textfull")]
    [Alias("guildmode")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetServerModeAsync(params string[] otherSettings)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (otherSettings != null && otherSettings.Any() && otherSettings.First() == "info")
        {
            var replyString = $"Use {prfx}mode to force an .fm mode for everyone in the server.";

            this._embed.AddField("Options",
                "**Modes**: `embedtiny/embedmini/embedfull/textmini/textfull`\n");

            this._embed.AddField("Examples",
                $"`{prfx}servermode embedmini` \n" +
                $"`{prfx}servermode` (no option disables it)");

            this._embed.WithTitle("Setting a server-wide .fm mode");
            this._embed.WithUrl($"{Constants.DocsUrl}/commands/");

            var guildMode = !guild.FmEmbedType.HasValue ? "No forced mode" : guild.FmEmbedType.ToString();
            this._embed.WithFooter($"Current .fm server mode: {guildMode}");
            this._embed.WithDescription(replyString);
            this._embed.WithColor(DiscordConstants.InformationColorBlue);

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        var newGuildSettings = this._guildService.SetSettings(guild, otherSettings);

        await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, newGuildSettings);

        if (newGuildSettings.FmEmbedType.HasValue)
        {
            await ReplyAsync($"The default .fm mode for your server has been set to {newGuildSettings.FmEmbedType}.\n\n" +
                             $"All .fm commands in this server will use this mode regardless of user settings, so make sure to inform your users of this change.");
        }
        else
        {
            await ReplyAsync(
                $"The default .fm mode has been disabled for this server. Users can now set their mode using `{prfx}mode`.\n\n" +
                $"To view all available modes, use `{prfx}servermode help`.");
        }
        this.Context.LogCommandUsed();
    }

    [Command("serverreactions", RunMode = RunMode.Async)]
    [Summary("Sets the automatic emote reactions for the `fm` command.\n\n" +
             "Use this command without any emotes to disable.")]
    [Examples("serverreactions :PagChomp: :PensiveBlob:", "serverreactions 😀 😯 🥵", "serverreactions 😀 😯 :PensiveBlob:", "serverreactions")]
    [Alias("serversetreactions")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetGuildReactionsAsync(params string[] emotes)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (emotes.Count() > 3)
        {
            await ReplyAsync("Sorry, max amount emote reactions you can set is 3!");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        if (emotes.Length == 0)
        {
            await this._guildService.SetGuildReactionsAsync(this.Context.Guild, null);
            await ReplyAsync(
                "Removed all server reactions!");
            this.Context.LogCommandUsed();
            return;
        }

        if (!this._guildService.ValidateReactions(emotes))
        {
            await ReplyAsync(
                "Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                "Please check if you have a space between every emote.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await this._guildService.SetGuildReactionsAsync(this.Context.Guild, emotes);

        var message = await ReplyAsync("Emote reactions have been set! \n" +
                                       "Please check if all reactions have been applied to this message correctly. If not, you might have used an emote from a different server.");
        await this._guildService.AddReactionsAsync(message, this.Context.Guild);
        this.Context.LogCommandUsed();
    }

    [Command("togglesupportermessages", RunMode = RunMode.Async)]
    [Summary("Enables/ disables the supporter messages on the `chart` command")]
    [Alias("togglesupporter", "togglesupporters", "togglesupport")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleSupportMessagesAsync()
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var messagesDisabled = await this._guildService.ToggleSupporterMessagesAsync(this.Context.Guild);

        if (messagesDisabled == true)
        {
            await ReplyAsync($".fmbot supporter messages have been disabled. Supporters are still visible in `{prfx}supporters`, but they will not be shown in `{prfx}chart` or other commands anymore.");
        }
        else
        {
            await ReplyAsync($".fmbot supporter messages have been re-enabled. These have a 1 in {Constants.SupporterMessageChance} chance of showing up on certain commands.");
        }

        this.Context.LogCommandUsed();
    }

    [Command("export", RunMode = RunMode.Async)]
    [Summary("Gets Last.fm usernames from your server members in json format.")]
    [Alias("getmembers", "exportmembers")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task GetMembersAsync()
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to use this command. For privacy reasons only server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        try
        {
            var serverUsers = await this._guildService.FindAllUsersFromGuildAsync(this.Context.Guild);

            if (serverUsers.Count == 0)
            {
                await ReplyAsync("No members found on this server.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var userJson = JsonSerializer.Serialize(serverUsers, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await this.Context.User.SendFileAsync(StringToStream(userJson),
                $"users_{this.Context.Guild.Name}_UTC-{DateTime.UtcNow:u}.json");

            await ReplyAsync("Check your DMs!");
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("prefix", RunMode = RunMode.Async)]
    [Summary("Changes the `.fm` prefix for your server.\n\n" +
             "For example, with the prefix `!` commands will be used as `!chart` and `!whoknows`\n\n" +
             "To restore the default prefix, use this command without an option")]
    [Examples("prefix", "prefix !")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    [RequiresIndex]
    public async Task SetPrefixAsync(string prefix = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to use this command. Only users with the 'Ban Members' permission or server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (string.IsNullOrEmpty(prefix) || prefix.ToLower() == "remove" || prefix.ToLower() == "delete" || prefix.ToLower() == ".")
        {
            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
            this._prefixService.RemovePrefix(this.Context.Guild.Id);
            await ReplyAsync("Reset to default prefix `.`! \n" +
                             "Commands prefixed with `.fm` and `.` will both work, so for example .fmbot will respond to `.fmwhoknows` and `.whoknows`.");
            this.Context.LogCommandUsed();
            return;
        }

        if (prefix.Length > 20)
        {
            await ReplyAsync("Max prefix length is 20 characters...");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }
        if (prefix.Contains("*") || prefix.Contains("`") || prefix.Contains("~") || prefix.Contains("|"))
        {
            await ReplyAsync("You can't have a custom prefix that contains ** * **, **`**, **~** or **|**");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await this._guildService.SetGuildPrefixAsync(this.Context.Guild, prefix);
        this._prefixService.StorePrefix(prefix, this.Context.Guild.Id);

        this._embed.WithTitle("Successfully added custom prefix!");
        this._embed.WithDescription("Examples:\n" +
                                    $"- `{prefix}fm`\n".Replace("fmfm", "fm") +
                                    $"- `{prefix}chart 8x8 monthly`\n" +
                                    $"- `{prefix}whoknows` \n \n" +
                                    "Reminder that you can always mention the bot followed by your command. \n" +
                                    $"The [.fmbot docs]({Constants.DocsUrl}) will still have the `.` prefix everywhere.\n\n" +
                                    $"To remove the custom prefix, do `{prefix}prefix remove`");

        await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        this.Context.LogCommandUsed();
    }


    [Command("toggleservercommand", RunMode = RunMode.Async)]
    [Summary("Enables or disables a command server-wide. Make sure to enter the command you want to disable without the `{{prfx}}` prefix.")]
    [Examples("toggleservercommand chart", "toggleservercommand whoknows")]
    [Alias("toggleservercommands", "toggleserver", "servertoggle")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleGuildCommand(string command = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var disabledCommandsForGuild = await this._guildService.GetDisabledCommandsForGuild(this.Context.Guild);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        this._embed.WithFooter(
            $"Toggling server-wide for all channels\n" +
            $"To toggle per channel use '{prfx}togglecommand'");

        if (string.IsNullOrEmpty(command))
        {
            var description = new StringBuilder();
            if (disabledCommandsForGuild != null && disabledCommandsForGuild.Length > 0)
            {
                description.AppendLine("Currently disabled commands in this server:");
                foreach (var disabledCommand in disabledCommandsForGuild)
                {
                    description.Append($"`{disabledCommand}` ");
                }
            }
            else
            {
                description.Append("This server currently has all commands enabled. \n" +
                                   $"To disable a command, enter the command name like this: `{prfx}toggleservercommand chart`");
            }

            this._embed.WithDescription(description.ToString());
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to toggle commands. Only users with the 'Ban Members' permission or server admins can disable/enable commands.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var searchResult = this._commands.Search(command.ToLower());

        if (searchResult.Commands == null || !searchResult.Commands.Any() || searchResult.Commands.Any(a => a.Command.Name == "toggleservercommand") || searchResult.Commands.Any(a => a.Command.Name == "togglecommand"))
        {
            this._embed.WithDescription("No commands found or command can't be disabled.\n" +
                                        "Remember to remove the `.fm` prefix.");
            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var foundCommand = searchResult.Commands.FirstOrDefault().Command;

        if (disabledCommandsForGuild != null && disabledCommandsForGuild.Any(a => a.Equals(foundCommand.Name.ToLower())))
        {
            var newDisabledCommands = await this._guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, foundCommand.Name.ToLower());

            this._guildDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

            this._embed.WithDescription($"Re-enabled command `{foundCommand.Name}` for this server.");
        }
        else
        {
            var newDisabledCommands = await this._guildService.AddGuildDisabledCommandAsync(this.Context.Guild, foundCommand.Name.ToLower());

            this._guildDisabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

            this._embed.WithDescription($"Disabled command `{foundCommand.Name}` for this server.");
        }

        await ReplyAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("togglecommand", RunMode = RunMode.Async)]
    [Summary("Enables or disables a command in this channel. Make sure to enter the command you want to disable without the `{{prfx}}` prefix.")]
    [Examples("togglecommand chart", "togglecommand whoknows chart taste", "togglecommand all")]
    [Alias("togglecommands", "channeltoggle", "togglechannel", "togglechannelcommand", "togglechannelcommands")]
    [GuildOnly]
    [RequiresIndex]
    [Options("All - Replaces your input with all commands to toggle them all at once")]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleChannelCommand(params string[] commands)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id, enableCache: false);

        var currentDisabledCommands = await this._guildService.GetDisabledCommandsForChannel(this.Context.Channel);

        this._embed.WithFooter(
            $"Toggling per channel\n" +
            $"To toggle server-wide use '{prfx}toggleservercommand'");

        if (commands == null || !commands.Any())
        {
            if (currentDisabledCommands != null && currentDisabledCommands.Any())
            {
                var channelDescription = new StringBuilder();
                foreach (var disabledCommand in currentDisabledCommands)
                {
                    channelDescription.Append($"`{disabledCommand}` ");
                }

                this._embed.AddField("Commands currently disabled in this channel", channelDescription.ToString());
            }

            var guildDescription = new StringBuilder();
            if (currentDisabledCommands == null || !currentDisabledCommands.Any())
            {
                guildDescription.AppendLine("This channel currently has all commands enabled. \n" +
                                            $"To disable a command, enter the command name like this: `{prfx}togglecommand chart`");
                this._embed.WithDescription(guildDescription.ToString());
            }

            if (guild.Channels != null && guild.Channels.Any() && guild.Channels.Any(a => a.DisabledCommands != null && a.DisabledCommands.Length > 0))
            {
                foreach (var channel in guild.Channels.Where(a => a.DisabledCommands is { Length: > 0 }))
                {
                    guildDescription.Append($"**<#{channel.DiscordChannelId}>** - ");
                    var maxCommandsToDisplay = channel.DisabledCommands.Length > 8 ? 8 : channel.DisabledCommands.Length;
                    for (var index = 0; index < maxCommandsToDisplay; index++)
                    {
                        var disabledCommand = channel.DisabledCommands[index];
                        guildDescription.Append($"`{disabledCommand}` ");
                    }

                    if (channel.DisabledCommands.Length > 8)
                    {
                        guildDescription.Append($" and {channel.DisabledCommands.Length - 8} other commands");
                    }

                    guildDescription.AppendLine();
                }

                this._embed.AddField("Currently disabled commands in this server per channel", guildDescription.ToString());
            }

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(
                "You are not authorized to toggle commands. Only users with the 'Ban Members' permission or server admins can use this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var enabledCommands = new List<string>();
        var unknownCommands = new List<string>();
        var disabledCommands = new List<string>();
        if (commands.Any(a => a.ToLower() == "all"))
        {
            var commandList = new List<string>();
            foreach (var module in this._service.Modules
                         .OrderByDescending(o => o.Commands.Count(w => !w.Attributes.OfType<ExcludeFromHelp>().Any() &&
                                                                       !w.Attributes.OfType<ServerStaffOnly>().Any()))
                         .Where(w =>
                             !w.Attributes.OfType<ExcludeFromHelp>().Any() &&
                             !w.Attributes.OfType<ServerStaffOnly>().Any()))
            {
                foreach (var cmd in module.Commands.Where(w =>
                             !w.Attributes.OfType<ExcludeFromHelp>().Any()))
                {
                    commandList.Add(cmd.Name.ToLower());
                }
            }

            commands = commandList.ToArray();
        }

        foreach (var command in commands)
        {
            var searchResult = this._commands.Search(command.ToLower());

            if (searchResult.Commands == null ||
                !searchResult.Commands.Any() ||
                searchResult.Commands.Any(a => a.Command.Name.ToLower() == "toggleservercommand") ||
                searchResult.Commands.Any(a => a.Command.Name.ToLower() == "togglecommand"))
            {
                unknownCommands.Add(command);
                continue;
            }

            var foundCommand = searchResult.Commands.FirstOrDefault().Command;

            if (currentDisabledCommands != null && currentDisabledCommands.Any(a => a.Equals(foundCommand.Name.ToLower())))
            {
                enabledCommands.Add(foundCommand.Name);
            }
            else
            {
                disabledCommands.Add(foundCommand.Name);
            }
        }

        try
        {
            if (enabledCommands.Any())
            {
                await this._guildService.EnableChannelCommandsAsync(this.Context.Channel, enabledCommands, this.Context.Guild.Id);

                var newlyEnabledCommands = new StringBuilder();
                var maxCommandsToDisplay = enabledCommands.Count > 8 ? 8 : enabledCommands.Count;
                for (var index = 0; index < maxCommandsToDisplay; index++)
                {
                    var enabledCommand = enabledCommands[index];
                    newlyEnabledCommands.Append($"`{enabledCommand}` ");
                }
                if (enabledCommands.Count > 8)
                {
                    newlyEnabledCommands.Append($" and {enabledCommands.Count - 8} other commands");
                }

                this._embed.AddField("Commands enabled", StringExtensions.TruncateLongString(newlyEnabledCommands.ToString(), 1010));
            }

            if (disabledCommands.Any())
            {
                await this._guildService.DisableChannelCommandsAsync(this.Context.Channel, guild.GuildId, disabledCommands, this.Context.Guild.Id);

                var newlyDisabledCommands = new StringBuilder();
                var maxCommandsToDisplay = disabledCommands.Count > 8 ? 8 : disabledCommands.Count;
                for (var index = 0; index < maxCommandsToDisplay; index++)
                {
                    var disabledCommand = disabledCommands[index];
                    newlyDisabledCommands.Append($"`{disabledCommand}` ");
                }
                if (disabledCommands.Count > 8)
                {
                    newlyDisabledCommands.Append($" and {disabledCommands.Count - 8} other commands");
                }

                this._embed.AddField("Commands disabled", StringExtensions.TruncateLongString(newlyDisabledCommands.ToString(), 1010));
            }

            if (unknownCommands.Any())
            {
                var unavailableCommands = new StringBuilder();
                foreach (var unknownCommand in unknownCommands)
                {
                    unavailableCommands.Append($"`{unknownCommand}` ");
                }

                this._embed.AddField("Unknown or unavailable commands", StringExtensions.TruncateLongString(unavailableCommands.ToString(), 1010));
            }

            var newDisabledCommands = await this._guildService.GetDisabledCommandsForChannel(this.Context.Channel);
            this._channelDisabledCommandService.StoreDisabledCommands(newDisabledCommands.ToArray(), this.Context.Channel.Id);

            var currentlyDisabled = new StringBuilder();
            var maxNewCommandsToDisplay = newDisabledCommands.Count > 32 ? 32 : newDisabledCommands.Count;
            for (var index = 0; index < maxNewCommandsToDisplay; index++)
            {
                var newDisabledCommand = newDisabledCommands[index];
                currentlyDisabled.Append($"`{newDisabledCommand}` ");
            }
            if (newDisabledCommands.Count > 32)
            {
                currentlyDisabled.Append($" and {newDisabledCommands.Count - 32} other commands");
            }

            this._embed.AddField("Commands currently disabled in this channel", currentlyDisabled.Length > 0 ? currentlyDisabled.ToString() : "All commands enabled.");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    [Command("cooldown", RunMode = RunMode.Async)]
    [Summary("Sets a cooldown for the `fm` command in a channel.\n\n" +
             "To pick a channel, simply use this command in the channel you want the cooldown in.")]
    [Options("Cooldown in seconds (Min 2 seconds - Max 1200 seconds)")]
    [Examples("cooldown 5", "cooldown 1000")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetFmCooldownCommand(string command = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id, enableCache: false);

        int? newCooldown = null;

        if (int.TryParse(command, out var parsedNewCooldown))
        {
            if (parsedNewCooldown is > 1 and <= 1200)
            {
                newCooldown = parsedNewCooldown;
            }
        }

        var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel.Id);

        var serverUser = (IGuildUser)this.Context.Message.Author;
        if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
            !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await ReplyAsync(Constants.ServerStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        this._embed.AddField("Previous .fm cooldown",
            existingFmCooldown.HasValue ? $"{existingFmCooldown.Value} seconds" : "No cooldown");

        var newFmCooldown = await this._guildService.SetChannelCooldownAsync(this.Context.Channel, guild.GuildId, newCooldown, this.Context.Guild.Id);

        this._embed.AddField("New .fm cooldown",
            newFmCooldown.HasValue ? $"{newFmCooldown.Value} seconds" : "No cooldown");

        this._embed.WithFooter($"Adjusting .fm cooldown for #{this.Context.Channel.Name}.\n" +
                               "Min 2 seconds - Max 1200 seconds - Cooldown is per-user.\n" +
                               "Note that this cooldown can also expire after a bot restart.");

        await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
        this.Context.LogCommandUsed();
    }

    private static Stream StringToStream(string str)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(str);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }
}
