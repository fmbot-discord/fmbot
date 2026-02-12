using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands.Guild;

[ModuleName("Server settings")]
[ServerStaffOnly]
public class GuildCommands(
    IPrefixService prefixService,
    GuildService guildService,
    IOptions<BotSettings> botSettings,
    IMemoryCache cache,
    GuildSettingBuilder guildSettingBuilder,
    UserService userService,
    InteractiveService interactivity,
    GuildBuilders guildBuilders)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("configuration", "ss", "config", "serversettings", "fmbotconfig", "serverconfig")]
    [Summary("Shows server configuration for .fmbot")]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task GuildSettingsAsync([CommandParameter(Remainder = true)] string searchValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);
            var response =
                await guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, prfx, contextUser), guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("members", "mb", "users", "memberoverview", "mo")]
    [Summary("view members in your server that have an .fmbot account")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task MemberOverviewAsync([CommandParameter(Remainder = true)] string searchValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser), guild,
                GuildViewType.Overview);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("keepdata")]
    [Summary("Allows you to keep your server data when removing the bot from your server")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task KeepDataAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        cache.Set($"{this.Context.Guild.Id}-keep-data", true, TimeSpan.FromMinutes(30));

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "You can now kick this bot from your server in the next 30 minutes without losing the stored .fmbot data, like server settings and crown history.\n\nIf you still wish to remove all server data from the bot you can kick the bot after the time period is over." });
    }

    [Command("language", "lang", "locale")]
    [Summary("Sets the language for .fmbot responses in this server.")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    [RequiresIndex]
    public async Task SetLanguageAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        try
        {
            var locale = await guildService.GetGuildLocaleAsync(this.Context.Guild.Id);
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.SetLanguage(new ContextModel(this.Context, prfx) { Locale = locale });

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("servermode", "guildmode")]
    [Summary("Sets the forced .fm mode for the server.\n\n" +
             "To view current settings, use `{{prfx}}servermode info`")]
    [Options("Modes: embedtiny/embedmini/embedfull/textmini/textfull")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetServerModeAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = await guildSettingBuilder.GuildMode(new ContextModel(this.Context, prfx));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("serverreactions", "serversetreactions", "serveremojis", "serverreacts")]
    [Summary("Sets the automatic emoji reactions for the `fm` and `featured` command.\n\n" +
             "Use this command without any emojis to disable.")]
    [Examples("serverreactions :PagChomp: :PensiveBlob:", "serverreactions 😀 😯 🥵", "serverreactions 😀 😯 :PensiveBlob:",
        "serverreactions")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetGuildReactionsAsync([CommandParameter(Remainder = true)] string emojis = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        if (string.IsNullOrWhiteSpace(emojis))
        {
            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

            await guildService.SetGuildReactionsAsync(this.Context.Guild, null);

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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

            return;
        }

        emojis = emojis.Replace("><", "> <");
        var emoteArray = emojis.Split(" ");

        if (emoteArray.Count() > 3)
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, you can't set more than 3 emoji reacts. Please try again.");
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);

            return;
        }

        if (!GuildService.ValidateReactions(emoteArray))
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, one or more of your reactions seem invalid. Please try again.\n" +
                                        "Please check if you have a space between every emoji.");
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);

            return;
        }

        await guildService.SetGuildReactionsAsync(this.Context.Guild, emoteArray);


        this._embed.WithTitle("Automatic server emoji reactions set");
        var description = new StringBuilder();
        description.AppendLine("Please check if all reactions have been applied to this message correctly.");
        var user = await userService.GetUserAsync(this.Context.User.Id);
        if (user != null)
        {
            description.AppendLine();
            description.AppendLine("Use `.userreactions` to set your own personal emoji reactions.");
        }
        this._embed.WithDescription(description.ToString());
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        var message = await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);

        try
        {
            await guildService.AddGuildReactionsAsync(message, this.Context.Guild);
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

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Error }, userService);
        }
    }

    [Command("prefix")]
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
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.SetPrefix(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("toggleservercommand", "toggleservercommands", "toggleserver", "servertoggle")]
    [Summary("Enables or disables a command server-wide")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleGuildCommand(string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [GuildOnly]
    [RequiresIndex]
    [Command("togglecommand", "togglecommands", "channeltoggle", "togglechannel", "togglechannelcommand", "togglechannelcommands", "channelmode",
        "channelfmmode")]
    [Summary("Enables or disables a command in a channel")]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task ToggleChannelCommand(string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var id = this.Context.Channel.Id;
            if (this.Context.Channel is GuildThread threadChannel)
            {
                id = threadChannel.ParentId ?? this.Context.Channel.Id;
            }

            var response =
                await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context, prfx), id);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("cooldown")]
    [Summary("Sets a cooldown for the `fm` command in a channel.\n\n" +
             "To pick a channel, simply use this command in the channel you want the cooldown in.")]
    [Options("Cooldown in seconds (Min 2 seconds - Max 1200 seconds)")]
    [Examples("cooldown 5", "cooldown 1000")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task SetFmCooldownCommand(string command = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var guild = await guildService.GetFullGuildAsync(this.Context.Guild.Id);

        int? newCooldown = null;

        if (int.TryParse(command, out var parsedNewCooldown))
        {
            if (parsedNewCooldown is > 1 and <= 1200)
            {
                newCooldown = parsedNewCooldown;
            }
        }

        var existingFmCooldown = await guildService.GetChannelCooldown(this.Context.Channel.Id);

        this._embed.AddField("Previous .fm cooldown",
            existingFmCooldown.HasValue ? $"{existingFmCooldown.Value} seconds" : "No cooldown");

        this.Context.Guild.Channels.TryGetValue(this.Context.Channel.Id, out var guildChannel);
        var newFmCooldown =
            await guildService.SetChannelCooldownAsync(guildChannel, guild.GuildId, newCooldown, this.Context.Guild.Id);

        this._embed.AddField("New .fm cooldown",
            newFmCooldown.HasValue ? $"{newFmCooldown.Value} seconds" : "No cooldown");

        this._embed.WithFooter($"Adjusting .fm cooldown for #{guildChannel?.Name ?? "unknown"}.\n" +
                               "Min 2 seconds - Max 1200 seconds - Cooldown is per-user.\n" +
                               "Note that this cooldown can also expire after a bot restart.");

        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }
}
