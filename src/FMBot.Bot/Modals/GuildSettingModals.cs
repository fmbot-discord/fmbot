using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class GuildSettingModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly GuildDisabledCommandService _guildDisabledCommandService;
    private readonly ChannelToggledCommandService _channelToggledCommandService;
    private readonly BotSettings _botSettings;

    public GuildSettingModals(
        GuildService guildService,
        IPrefixService prefixService,
        GuildSettingBuilder guildSettingBuilder,
        GuildDisabledCommandService guildDisabledCommandService,
        ChannelToggledCommandService channelToggledCommandService,
        IOptions<BotSettings> botSettings)
    {
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._guildSettingBuilder = guildSettingBuilder;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelToggledCommandService = channelToggledCommandService;
        this._botSettings = botSettings.Value;
    }

    [ComponentInteraction($"{InteractionConstants.SetPrefixModal}:*")]
    public async Task SetPrefix(string messageId)
    {
        var newPrefix = this.Context.GetModalValue("new_prefix");

        if (newPrefix == this._botSettings.Bot.Prefix)
        {
            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
            this._prefixService.StorePrefix(null, this.Context.Guild.Id);
        }
        else if (newPrefix.Contains('/') || newPrefix.Contains('*') || newPrefix.Contains('|') ||
                 newPrefix.Contains('`') || newPrefix.Contains('#') || newPrefix.Contains('_') ||
                 newPrefix.Contains('~'))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Prefix contains disallowed characters. Please try a different prefix.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }
        else
        {
            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, newPrefix);
            this._prefixService.StorePrefix(newPrefix, this.Context.Guild.Id);
        }

        var response = await this._guildSettingBuilder.SetPrefix(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.SetFmbotActivityThresholdModal}:*")]
    public async Task SetFmbotActivityThreshold(string messageId)
    {
        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 999)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `999`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await this._guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.SetCrownActivityThresholdModal}:*")]
    public async Task SetCrownActivityThreshold(string messageId)
    {
        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 999)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `999`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.SetCrownMinPlaycountModal}:*")]
    public async Task SetCrownMinPlaycount(string messageId)
    {
        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 10000)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `10000`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandAddModal}:*:*:*")]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId, string messageId)
    {
        var command = this.Context.GetModalValue("command");
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var channels = await this.Context.Guild.GetChannelsAsync();
        var selectedChannel = channels.FirstOrDefault(f => f.Id == parsedChannelId);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        await this._guildService.DisableChannelCommandsAsync(selectedChannel, guild.GuildId,
            new List<string> { command.ToLower() }, this.Context.Guild.Id);

        await this._channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandRemoveModal}:*:*:*")]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId, string messageId)
    {
        var command = this.Context.GetModalValue("command");
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var channels = await this.Context.Guild.GetChannelsAsync();
        var selectedChannel = channels.FirstOrDefault(f => f.Id == parsedChannelId);

        await this._guildService.EnableChannelCommandsAsync(selectedChannel, new List<string> { command.ToLower() },
            this.Context.Guild.Id);

        await this._channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal}:*")]
    public async Task AddDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await this._guildService.AddGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await this._guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal}:*")]
    public async Task RemoveDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await this._guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await this._guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }
}
