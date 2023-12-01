using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using Discord.Commands;
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
using Discord.WebSocket;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Attributes;
using FMBot.Bot.Services.WhoKnows;
using Microsoft.Extensions.Options;
using FMBot.Domain.Enums;
using Discord;

namespace FMBot.Bot.SlashCommands;

public class GuildSettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;
    private readonly GuildBuilders _guildBuilders;

    private readonly CommandService _commands;

    private readonly ChannelDisabledCommandService _channelDisabledCommandService;
    private readonly DisabledChannelService _disabledChannelService;
    private readonly GuildDisabledCommandService _guildDisabledCommandService;
    private readonly CrownService _crownService;

    private readonly BotSettings _botSettings;

    private InteractiveService Interactivity { get; }

    public GuildSettingSlashCommands(
        GuildSettingBuilder guildSettingBuilder,
        InteractiveService interactivity,
        UserService userService,
        IPrefixService prefixService,
        GuildService guildService,
        CommandService commands,
        ChannelDisabledCommandService channelDisabledCommandService,
        DisabledChannelService disabledChannelService,
        GuildDisabledCommandService guildDisabledCommandService,
        IOptions<BotSettings> botSettings,
        CrownService crownService,
        GuildBuilders guildBuilders)
    {
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._commands = commands;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._disabledChannelService = disabledChannelService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._crownService = crownService;
        this._guildBuilders = guildBuilders;
        this._botSettings = botSettings.Value;
    }

    [SlashCommand("configuration", "Shows server configuration for .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task ServerSettingsAsync()
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);

            var response = await this._guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, contextUser), guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("members", "Shows server members that use .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync(
        [Discord.Interactions.Summary("View", "Statistic you want to view")] GuildViewType viewType)
    {
        try
        {
            _ = DeferAsync();

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild, viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildMembers)]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync(string[] inputs)
    {
        try
        {
            _ = DeferAsync();

            if (!Enum.TryParse(inputs.First(), out GuildViewType viewType))
            {
                return;
            }

            var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
            if (message == null)
            {
                return;
            }

            var name = viewType.GetAttribute<ChoiceDisplayAttribute>().Name;

            var components =
                new ComponentBuilder().WithButton($"Loading {name.ToLower()} view...", customId: "1", emote: Emote.Parse("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Secondary);
            await message.ModifyAsync(m => m.Components = components.Build());

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response = await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild, viewType);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    [ServerStaffOnly]
    public async Task GetGuildSetting(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

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
                        response = await this._guildSettingBuilder.SetPrefix(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.EmoteReactions:
                    response = GuildSettingBuilder.GuildReactionsAsync(new ContextModel(this.Context), prfx);

                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    return;
                case GuildSetting.DefaultEmbedType:
                    {
                        response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                    {
                        response = await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                    {
                        response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    }
                    break;
                case GuildSetting.CrownActivityThreshold:
                    {
                        response = await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.CrownBlockedUsers:
                    {
                        response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings), true);
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    }

                    break;
                case GuildSetting.CrownMinimumPlaycount:
                    {
                        response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.CrownSeeder:
                    {
                        response = await this._guildSettingBuilder.CrownSeeder(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.DisabledCommands:
                    {
                        response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), this.Context.Channel.Id);
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                case GuildSetting.DisabledGuildCommands:
                    {
                        response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context));
                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [ComponentInteraction(InteractionConstants.RunCrownseeder)]
    [ServerStaffOnly]
    public async Task RunCrownSeeder()
    {
        var response = GuildSettingBuilder.CrownSeederRunning(new ContextModel(this.Context));
        await this.Context.UpdateInteractionEmbed(response);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);

        var amountOfCrownsSeeded = await this._crownService.SeedCrownsForGuild(guild, guildCrowns);

        response = await this._guildSettingBuilder.CrownSeederDone(new ContextModel(this.Context), amountOfCrownsSeeded);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetPrefix)]
    [ServerStaffOnly]
    public async Task SetPrefix()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<SetPrefixModal>($"{InteractionConstants.SetPrefixModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.SetPrefixModal}-*")]
    [ServerStaffOnly]
    public async Task SetPrefix(string messageId, SetPrefixModal modal)
    {
        if (modal.NewPrefix == this._botSettings.Bot.Prefix)
        {
            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
            this._prefixService.StorePrefix(null, this.Context.Guild.Id);
        }
        else if (modal.NewPrefix.Contains("/") ||
                 modal.NewPrefix.Contains("*") ||
                 modal.NewPrefix.Contains("|") ||
                 modal.NewPrefix.Contains("`") ||
                 modal.NewPrefix.Contains("#") ||
                 modal.NewPrefix.Contains("_") ||
                 modal.NewPrefix.Contains("~"))
        {
            await RespondAsync($"Prefix contains disallowed characters. Please try a different prefix.", ephemeral: true);
            return;
        }
        else
        {
            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, modal.NewPrefix);
            this._prefixService.StorePrefix(modal.NewPrefix, this.Context.Guild.Id);
        }

        var response = await this._guildSettingBuilder.SetPrefix(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.RemovePrefix)]
    [ServerStaffOnly]
    public async Task RemovePrefix()
    {
        await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
        this._prefixService.StorePrefix(null, this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.SetPrefix(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.FmGuildSettingType)]
    [ServerStaffOnly]
    public async Task SetGuildEmbedType(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
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

        var response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandChannelFmType}-*-*")]
    [ServerStaffOnly]
    public async Task SetChannelEmbedType(string channelId, string categoryId, string[] inputs)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            await this._guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, embedType, this.Context.Guild.Id);
        }
        else
        {
            await this._guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, null, this.Context.Guild.Id);
        }

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetFmbotActivityThreshold()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<SetFmbotActivityThresholdModal>($"{InteractionConstants.SetFmbotActivityThresholdModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.SetFmbotActivityThresholdModal}-*")]
    [ServerStaffOnly]
    public async Task SetFmbotActivityThreshold(string messageId, SetFmbotActivityThresholdModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        if (!int.TryParse(modal.Amount, out var result) ||
            result < 2 ||
            result > 999)
        {
            await RespondAsync($"Please enter a valid number between `2` and `999`.", ephemeral: true);
            return;
        }

        await this._guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.RemoveFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveFmbotActivityThreshold()
    {
        await this._guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetCrownActivityThreshold()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<SetCrownActivityThresholdModal>($"{InteractionConstants.SetCrownActivityThresholdModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.SetCrownActivityThresholdModal}-*")]
    [ServerStaffOnly]
    public async Task SetCrownActivityThreshold(string messageId, SetCrownActivityThresholdModal modal)
    {
        if (!int.TryParse(modal.Amount, out var result) ||
            result < 2 ||
            result > 999)
        {
            await RespondAsync($"Please enter a valid number between `2` and `999`.", ephemeral: true);
            return;
        }

        await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveCrownActivityThreshold()
    {
        await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, null);

        var response = await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }


    [ComponentInteraction(InteractionConstants.SetCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task SetCrownMinPlaycount()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<SetCrownMinPlaycountModal>($"{InteractionConstants.SetCrownMinPlaycountModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.SetCrownMinPlaycountModal}-*")]
    [ServerStaffOnly]
    public async Task SetCrownMinPlaycount(string messageId, SetCrownMinPlaycountModal modal)
    {
        if (!int.TryParse(modal.Amount, out var result) ||
            result < 5 ||
            result > 1000)
        {
            await RespondAsync($"Please enter a valid number between `5` and `1000`.", ephemeral: true);
            return;
        }

        await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, result);

        var response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task RemoveCrownMinPlaycount()
    {
        await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, null);

        var response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandMove}-*-*")]
    [ServerStaffOnly]
    public async Task ToggleChannelCommandMove(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandAdd}-*-*")]
    [ServerStaffOnly]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<AddDisabledChannelCommandModal>($"{InteractionConstants.ToggleCommand.ToggleCommandAddModal}-{channelId}-{categoryId}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandAddModal}-*-*-*")]
    [ServerStaffOnly]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId, string messageId, AddDisabledChannelCommandModal modal)
    {
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

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);

        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandRemove}-*-*")]
    [ServerStaffOnly]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<RemoveDisabledChannelCommandModal>($"{InteractionConstants.ToggleCommand.ToggleCommandRemoveModal}-{channelId}-{categoryId}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandRemoveModal}-*-*-*")]
    [ServerStaffOnly]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId, string messageId, RemoveDisabledChannelCommandModal modal)
    {
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

        await this._channelDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);

        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandClear}-*-*")]
    [ServerStaffOnly]
    public async Task ClearDisabledChannelCommand(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);
        await this._guildService.ClearDisabledChannelCommandsAsync(selectedChannel, this.Context.Guild.Id);

        await this._channelDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandDisableAll}-*-*")]
    [ServerStaffOnly]
    public async Task DisableChannel(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        await this._guildService.DisableChannelAsync(selectedChannel, guild.GuildId, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandEnableAll}-*-*")]
    [ServerStaffOnly]
    public async Task EnableChannel(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var selectedChannel = await this.Context.Guild.GetChannelAsync(parsedChannelId);

        await this._guildService.EnableChannelAsync(selectedChannel, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAdd}")]
    [ServerStaffOnly]
    public async Task AddDisabledGuildCommand()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<AddDisabledGuildCommandModal>($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal}-*")]
    [ServerStaffOnly]
    public async Task AddDisabledGuildCommand(string messageId, AddDisabledGuildCommandModal modal)
    {
        var searchResult = this._commands.Search(modal.Command.ToLower());

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

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var currentlyDisabledCommands = guild.DisabledCommands?.ToList();

        if (currentlyDisabledCommands != null &&
            currentlyDisabledCommands.Any(a => a == commandToDisable.Command.Name))
        {
            await RespondAsync($"The command `{commandToDisable.Command.Name}` is already disabled in this server.", ephemeral: true);
            return;
        }

        await this._guildService.AddGuildDisabledCommandAsync(this.Context.Guild, commandToDisable.Command.Name);
        await this._guildDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemove}")]
    [ServerStaffOnly]
    public async Task RemoveDisabledGuildCommand()
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<RemoveDisabledGuildCommandModal>($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal}-{message.Id}");
    }

    [ModalInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal}-*")]
    [ServerStaffOnly]
    public async Task RemoveDisabledChannelCommand(string messageId, RemoveDisabledGuildCommandModal modal)
    {
        var searchResult = this._commands.Search(modal.Command.ToLower());

        if (!searchResult.IsSuccess ||
            !searchResult.Commands.Any())
        {
            await RespondAsync($"The command `{modal.Command}` could not be found. Please try again.", ephemeral: true);
            return;
        }

        var commandToEnable = searchResult.Commands[0];

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var currentlyDisabledCommands = guild.DisabledCommands?.ToList();

        if (currentlyDisabledCommands == null ||
            currentlyDisabledCommands.All(a => a != commandToEnable.Command.Name))
        {
            await RespondAsync($"The command `{commandToEnable.Command.Name}` is not disabled in this server.", ephemeral: true);
            return;
        }

        await this._guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, commandToEnable.Command.Name);
        await this._guildDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandClear}")]
    [ServerStaffOnly]
    public async Task ClearDisabledChannelCommand()
    {
        await this._guildService.ClearGuildDisabledCommandAsync(this.Context.Guild);
        await this._guildDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateInteractionEmbed(response);
    }
}
