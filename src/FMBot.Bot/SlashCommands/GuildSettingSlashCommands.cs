using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;
using FMBot.Bot.Models;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Attributes;
using FMBot.Bot.Factories;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Attributes;
using Microsoft.Extensions.Options;
using FMBot.Domain.Enums;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.SlashCommands;

public class GuildSettingSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly UserService _userService;

    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;
    private readonly GuildBuilders _guildBuilders;

    private readonly CommandService<CommandContext> _commands;

    private readonly ChannelToggledCommandService _channelToggledCommandService;
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
        CommandService<CommandContext> commands,
        ChannelToggledCommandService channelToggledCommandService,
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
        this._channelToggledCommandService = channelToggledCommandService;
        this._disabledChannelService = disabledChannelService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._crownService = crownService;
        this._guildBuilders = guildBuilders;
        this._botSettings = botSettings.Value;
    }

    [SlashCommand("configuration", "Server configuration for .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task ServerSettingsAsync()
    {
        try
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guildPermissions = await GuildService.GetGuildPermissionsAsync(this.Context);

            var response =
                await this._guildSettingBuilder.GetGuildSettings(new ContextModel(this.Context, contextUser),
                    guildPermissions);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("members", "Members in this server that use .fmbot")]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync(
        [SlashCommandParameter(Name = "View", Description = "Statistic you want to view")]
        GuildViewType viewType)
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage());

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

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
            await RespondAsync(InteractionCallback.DeferredMessage());

            if (!Enum.TryParse(inputs.First(), out GuildViewType viewType))
            {
                return;
            }

            var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
            if (message == null)
            {
                return;
            }

            var name = viewType.GetAttribute<OptionAttribute>().Name;

            var components =
                new ActionRowProperties().WithButton($"Loading {name.ToLower()} view...", customId: "1",
                    emote: EmojiProperties.Custom("<a:loading:821676038102056991>"), disabled: true, style: ButtonStyle.Secondary);
            await message.ModifyAsync(m => m.Components = [components]);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
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
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                {
                    response =
                        await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                {
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context,
                        userSettings));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                }
                    break;
                case GuildSetting.CrownActivityThreshold:
                {
                    response =
                        await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownBlockedUsers:
                {
                    response = await this._guildSettingBuilder.BlockedUsersAsync(
                        new ContextModel(this.Context, userSettings), true);
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
                case GuildSetting.CrownsDisabled:
                {
                    response = await this._guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.DisabledCommands:
                {
                    response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
                        this.Context.Channel.Id);
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

        response = await this._guildSettingBuilder.CrownSeederDone(new ContextModel(this.Context),
            amountOfCrownsSeeded);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetPrefix)]
    [ServerStaffOnly]
    public async Task SetPrefix()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateSetPrefixModal(
            $"{InteractionConstants.SetPrefixModal}:{message.Id}"));
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

        try
        {
            if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
            {
                await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, embedType);
            }
            else
            {
                await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, null);
            }

            var response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context), this.Context.User);

            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [response.Embed];
                e.Components = [response.Components];
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
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
        var selectedChannel = this.Context.Guild.Channels.TryGetValue(parsedChannelId, out var ch) ? ch : null;

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            await this._guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, embedType,
                this.Context.Guild.Id);
        }
        else
        {
            await this._guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, null, this.Context.Guild.Id);
        }

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetFmbotActivityThreshold()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateSetFmbotActivityThresholdModal(
            $"{InteractionConstants.SetFmbotActivityThresholdModal}:{message.Id}"));
    }

    [ComponentInteraction(InteractionConstants.RemoveFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveFmbotActivityThreshold()
    {
        await this._guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, null);

        var response =
            await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context),
                this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.SetCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetCrownActivityThreshold()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateSetCrownActivityThresholdModal(
            $"{InteractionConstants.SetCrownActivityThresholdModal}:{message.Id}"));
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveCrownActivityThreshold()
    {
        await this._guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, null);

        var response =
            await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context),
                this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }


    [ComponentInteraction(InteractionConstants.SetCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task SetCrownMinPlaycount()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateSetCrownMinPlaycountModal(
            $"{InteractionConstants.SetCrownMinPlaycountModal}:{message.Id}"));
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task RemoveCrownMinPlaycount()
    {
        await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, null);

        var response =
            await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandMove}-*-*-*")]
    [ServerStaffOnly]
    public async Task ToggleChannelCommandMove(string channelId, string categoryId, string direction)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandAdd}-*-*")]
    [ServerStaffOnly]
    public async Task AddDisabledChannelCommand(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateAddDisabledChannelCommandModal(
            $"{InteractionConstants.ToggleCommand.ToggleCommandAddModal}:{channelId}:{categoryId}:{message.Id}"));
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandRemove}-*-*")]
    [ServerStaffOnly]
    public async Task RemoveDisabledChannelCommand(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateRemoveDisabledChannelCommandModal(
            $"{InteractionConstants.ToggleCommand.ToggleCommandRemoveModal}:{channelId}:{categoryId}:{message.Id}"));
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandClear}-*-*")]
    [ServerStaffOnly]
    public async Task ClearDisabledChannelCommand(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var channels = await this.Context.Guild.GetChannelsAsync();
        var selectedChannel = channels.FirstOrDefault(f => f.Id == parsedChannelId);

        await this._guildService.ClearDisabledChannelCommandsAsync(selectedChannel, this.Context.Guild.Id);

        await this._channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandDisableAll}-*-*")]
    [ServerStaffOnly]
    public async Task DisableChannel(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = this.Context.Guild.Channels.TryGetValue(parsedChannelId, out var ch) ? ch : null;

        await this._guildService.DisableChannelAsync(selectedChannel, guild.GuildId, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleCommandEnableAll}-*-*")]
    [ServerStaffOnly]
    public async Task EnableChannel(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var selectedChannel = this.Context.Guild.Channels.TryGetValue(parsedChannelId, out var ch) ? ch : null;

        await this._guildService.EnableChannelAsync(selectedChannel, this.Context.Guild.Id);
        await this._disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAdd}")]
    [ServerStaffOnly]
    public async Task AddDisabledGuildCommand()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateAddDisabledGuildCommandModal(
            $"{InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal}:{message.Id}"));
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemove}")]
    [ServerStaffOnly]
    public async Task RemoveDisabledGuildCommand()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync(ModalFactory.CreateRemoveDisabledGuildCommandModal(
            $"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal}:{message.Id}"));
    }

    [ComponentInteraction($"{InteractionConstants.ToggleCommand.ToggleGuildCommandClear}")]
    [ServerStaffOnly]
    public async Task ClearDisabledChannelCommand()
    {
        await this._guildService.ClearGuildDisabledCommandAsync(this.Context.Guild);
        await this._guildDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response =
            await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCrowns.Enable)]
    [ServerStaffOnly]
    public async Task EnableCrowns()
    {
        var response = await this._guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context), false);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCrowns.Disable)]
    [ServerStaffOnly]
    public async Task DisableCrowns()
    {
        var response = await this._guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context), true);
        await this.Context.UpdateInteractionEmbed(response);
    }
}
