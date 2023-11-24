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
    private readonly GuildService _guildService;
    private readonly UserService _userService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly GuildBuilders _guildBuilders;

    private readonly IMemoryCache _cache;

    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }

    public GuildCommands(IPrefixService prefixService,
        GuildService guildService,
        IOptions<BotSettings> botSettings,
        IMemoryCache cache,
        GuildSettingBuilder guildSettingBuilder,
        UserService userService,
        InteractiveService interactivity,
        GuildBuilders guildBuilders) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._cache = cache;
        this._guildSettingBuilder = guildSettingBuilder;
        this._userService = userService;
        this.Interactivity = interactivity;
        this._guildBuilders = guildBuilders;
    }

    [Command("configuration", RunMode = RunMode.Async)]
    [Summary("Shows server configuration for .fmbot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ServerSettings)]
    [Alias("ss", "config", "serversettings", "fmbotconfig", "serverconfig")]
    public async Task GuildSettingsAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);
            var response = await this._guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, prfx, contextUser), guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("members", RunMode = RunMode.Async)]
    [Summary("view members in your server that have an .fmbot account")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ServerSettings)]
    [Alias("mb", "users", "memberoverview", "mo")]
    public async Task MemberOverviewAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser), guild, GuildViewType.Overview);

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
        this._cache.Set($"{this.Context.Guild.Id}-keep-data", true, TimeSpan.FromMinutes(30));

        await ReplyAsync(
            "You can now kick this bot from your server in the next 30 minutes without losing the stored .fmbot data, like server settings and crown history.\n\n" +
            "If you still wish to remove all server data from the bot you can kick the bot after the time period is over.");
    }

    [Command("servermode", RunMode = RunMode.Async)]
    [Summary("Sets the forced .fm mode for the server.\n\n" +
             "To view current settings, use `{{prfx}}servermode info`")]
    [Options("Modes: embedtiny/embedmini/embedfull/textmini/textfull")]
    [Alias("guildmode")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetServerModeAsync([Remainder] string unused = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context, prfx));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("serverreactions", RunMode = RunMode.Async)]
    [Summary("Sets the automatic emoji reactions for the `fm` and `featured` command.\n\n" +
             "Use this command without any emojis to disable.")]
    [Examples("serverreactions :PagChomp: :PensiveBlob:", "serverreactions 😀 😯 🥵", "serverreactions 😀 😯 :PensiveBlob:", "serverreactions")]
    [Alias("serversetreactions", "serveremojis", "serverreacts")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetGuildReactionsAsync([Remainder] string emojis = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (string.IsNullOrWhiteSpace(emojis))
        {
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            await this._guildService.SetGuildReactionsAsync(this.Context.Guild, null);

            if (guild?.EmoteReactions == null || !guild.EmoteReactions.Any())
            {
                this._embed.WithDescription("Use this command with emojis to set the default reactions to `fm` and `featured`.\n\n" +
                                            "For example:\n" +
                                            $"`{prfx}serverreactions ⬆️ ⬇️`");
            }
            else
            {
                this._embed.WithDescription("Removed all server reactions!");
            }

            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            await ReplyAsync(embed: this._embed.Build());

            this.Context.LogCommandUsed();

            return;
        }

        emojis = emojis.Replace("><", "> <");
        var emoteArray = emojis.Split(" ");

        if (emoteArray.Count() > 3)
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, you can't set more then 3 emoji reacts. Please try again.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        if (!GuildService.ValidateReactions(emoteArray))
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                                        "Please check if you have a space between every emoji.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        await this._guildService.SetGuildReactionsAsync(this.Context.Guild, emoteArray);

        this._embed.WithTitle("Automatic emoji reactions set");
        this._embed.WithDescription("Please check if all reactions have been applied to this message correctly.");
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        var message = await ReplyAsync(embed: this._embed.Build());
        this.Context.LogCommandUsed();

        try
        {
            await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild);
        }
        catch (Exception e)
        {
            this._embed.WithTitle("Error in set emoji reactions");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);

            if (e.Message.ToLower().Contains("permission"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "The bot does not have the `Add Reactions` permission. Please make sure that your permissions for the bot and channel are set correctly.");
            }
            else if (e.Message.ToLower().Contains("unknown emoji"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "You've used an emoji from a different server. Make sure you only use emojis from this server, or from servers that have .fmbot.");
            }
            else
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "Make sure the permissions are set correctly and the emoji are from this server.");
            }

            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Error);
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
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.SetPrefix(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("toggleservercommand", RunMode = RunMode.Async)]
    [Summary("Enables or disables a command server-wide")]
    [Alias("toggleservercommands", "toggleserver", "servertoggle")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleGuildCommand(string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [GuildOnly]
    [RequiresIndex]
    [Command("togglecommand", RunMode = RunMode.Async)]
    [Summary("Enables or disables a command in a channel")]
    [Alias("togglecommands", "channeltoggle", "togglechannel", "togglechannelcommand", "togglechannelcommands", "channelmode", "channelfmmode")]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleChannelCommand(string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context, prfx), this.Context.Channel.Id);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
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
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id);

        int? newCooldown = null;

        if (int.TryParse(command, out var parsedNewCooldown))
        {
            if (parsedNewCooldown is > 1 and <= 1200)
            {
                newCooldown = parsedNewCooldown;
            }
        }

        var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel.Id);

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
}
