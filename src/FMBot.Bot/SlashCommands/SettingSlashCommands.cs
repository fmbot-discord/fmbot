using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Discord.Interactions;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;
using FMBot.Bot.Models;
using Fergun.Interactive;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using static FMBot.Bot.Builders.GuildSettingBuilder;
using Discord.WebSocket;
using Discord;
using FMBot.Bot.Models.Modals;
using FMBot.Domain.Enums;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using Discord.Commands;
using Genius.Models;
using SpotifyAPI.Web;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class SettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;

    private readonly CommandService _commands;

    private readonly ChannelDisabledCommandService _channelDisabledCommandService;
    private readonly DisabledChannelService _disabledChannelService;

    private InteractiveService Interactivity { get; }

    public SettingSlashCommands(
        GuildSettingBuilder guildSettingBuilder,
        InteractiveService interactivity,
        UserService userService,
        IPrefixService prefixService,
        GuildService guildService,
        CommandService commands,
        ChannelDisabledCommandService channelDisabledCommandService,
        DisabledChannelService disabledChannelService)
    {
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._commands = commands;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._disabledChannelService = disabledChannelService;
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    public async Task GetGuildSetting(string[] inputs)
    {
        var setting = inputs.First().Replace("gs-", "");

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
        {
            ResponseModel response;
            switch (guildSetting)
            {
                case GuildSetting.TextPrefix:
                    {
                        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
                        {
                            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
                            return;
                        }

                        await this.Context.Interaction.RespondWithModalAsync<PrefixModal>(InteractionConstants.TextPrefixModal);
                    }
                    break;
                case GuildSetting.EmoteReactions:
                    response = GuildReactionsAsync(new ContextModel(this.Context), prfx);

                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    return;
                case GuildSetting.DefaultEmbedType:
                    {
                        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
                        {
                            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
                            return;
                        }

                        response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownActivityThreshold:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.CrownBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings), true);
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownMinimumPlaycount:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.CrownsDisabled:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.DisabledCommands:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [ModalInteraction(InteractionConstants.TextPrefixModal)]
    public async Task SetNewTextPrefix(PrefixModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var response = await this._guildSettingBuilder.SetPrefix(this.Context, modal.NewPrefix);

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
    }

    [ComponentInteraction(InteractionConstants.FmGuildSettingType)]
    public async Task SetGuildEmbedType(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, embedType);
        }
        else
        {
            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, null);

        }

        var response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await message.ModifyAsync(m =>
        {
            m.Embed = response.Embed.Build();
            m.Components = response.Components.Build();
        });

        await this.Context.Interaction.DeferAsync();
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandMove}-*-*")]
    public async Task SetGuildEmbedType(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await message.ModifyAsync(m =>
        {
            m.Embed = response.Embed.Build();
            m.Components = response.Components.Build();
        });

        await this.Context.Interaction.DeferAsync();
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandAdd}-*-*")]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<AddDisabledChannelCommandModal>($"{InteractionConstants.ToggleCommandAddModal}-{channelId}-{categoryId}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommandAddModal}-*-*-*")]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId, string messageId, AddDisabledChannelCommandModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);
        var searchResult = this._commands.Search(modal.Command.ToLower());
        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        if (!searchResult.IsSuccess ||
            !searchResult.Commands.Any())
        {
            await RespondAsync($"The command `{modal.Command}` could not be found. Please try again.", ephemeral: true);
            return;
        }

        var commandToDisable = searchResult.Commands[0];

        if (commandToDisable.Command.Name is "serversettings" or "togglecommand" or "toggleservercommand")
        {
            await RespondAsync($"You can't disable this command. Please try a different command.", ephemeral: true);
            return;
        }

        var currentlyDisabledCommands = await this._guildService.GetDisabledCommandsForChannel(parsedChannelId);

        if (currentlyDisabledCommands != null &&
            currentlyDisabledCommands.Any(a => a == commandToDisable.Command.Name))
        {
            await RespondAsync($"The command `{commandToDisable.Command.Name}` is already disabled in <#{selectedChannel.Id}>.", ephemeral: true);
            return;
        }

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        await this._guildService
            .DisableChannelCommandsAsync(selectedChannel, guild.GuildId, new List<string> { commandToDisable.Command.Name }, this.Context.Guild.Id);

        await this._channelDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);

        await message.ModifyAsync(m =>
        {
            m.Components = response.Components.Build();
            m.Embed = response.Embed.Build();
        });

        await this.Context.Interaction.DeferAsync();
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandRemove}-*-*")]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<RemoveDisabledChannelCommandModal>($"{InteractionConstants.ToggleCommandRemoveModal}-{channelId}-{categoryId}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommandRemoveModal}-*-*-*")]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId, string messageId, RemoveDisabledChannelCommandModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);
        var searchResult = this._commands.Search(modal.Command.ToLower());
        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        if (!searchResult.IsSuccess ||
            !searchResult.Commands.Any())
        {
            await RespondAsync($"The command `{modal.Command}` could not be found. Please try again.", ephemeral: true);
            return;
        }

        var commandToEnable = searchResult.Commands[0];

        var currentlyDisabledCommands = await this._guildService.GetDisabledCommandsForChannel(parsedChannelId);

        if (currentlyDisabledCommands == null ||
            currentlyDisabledCommands.All(a => a != commandToEnable.Command.Name))
        {
            await RespondAsync($"The command `{commandToEnable.Command.Name}` is not disabled in <#{selectedChannel.Id}>.", ephemeral: true);
            return;
        }

        await this._guildService
            .EnableChannelCommandsAsync(selectedChannel, new List<string> { commandToEnable.Command.Name }, this.Context.Guild.Id);

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        await this._channelDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);

        await message.ModifyAsync(m =>
        {
            m.Components = response.Components.Build();
            m.Embed = response.Embed.Build();
        });

        await this.Context.Interaction.DeferAsync();
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandClear}-*-*")]
    public async Task ClearDisabledChannelCommand(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);
        await this._guildService.ClearDisabledChannelCommandsAsync(selectedChannel, this.Context.Guild.Id);

        await this._channelDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        await RespondWithToggleChannelCommandEmbed(parsedChannelId, parsedCategoryId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandDisableAll}-*-*")]
    public async Task DisableChannel(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        await this._guildService.DisableChannelAsync(selectedChannel, guild.GuildId, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        await RespondWithToggleChannelCommandEmbed(parsedChannelId, parsedCategoryId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommandEnableAll}-*-*")]
    public async Task EnableChannel(string channelId, string categoryId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        await this._guildService.EnableChannelAsync(selectedChannel, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        await RespondWithToggleChannelCommandEmbed(parsedChannelId, parsedCategoryId);
    }

    private async Task RespondWithToggleChannelCommandEmbed(ulong parsedChannelId, ulong parsedCategoryId)
    {
        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await message.ModifyAsync(m =>
        {
            m.Components = response.Components.Build();
            m.Embed = response.Embed.Build();
        });

        await this.Context.Interaction.DeferAsync();
    }
}
