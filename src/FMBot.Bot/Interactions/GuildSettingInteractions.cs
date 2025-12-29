using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class GuildSettingInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly GuildBuilders _guildBuilders;
    private readonly GuildDisabledCommandService _guildDisabledCommandService;
    private readonly ChannelToggledCommandService _channelToggledCommandService;
    private readonly DisabledChannelService _disabledChannelService;
    private readonly CrownService _crownService;
    private readonly UserService _userService;
    private readonly InteractiveService _interactivity;
    private readonly BotSettings _botSettings;

    public GuildSettingInteractions(
        GuildService guildService,
        IPrefixService prefixService,
        GuildSettingBuilder guildSettingBuilder,
        GuildBuilders guildBuilders,
        GuildDisabledCommandService guildDisabledCommandService,
        ChannelToggledCommandService channelToggledCommandService,
        DisabledChannelService disabledChannelService,
        CrownService crownService,
        UserService userService,
        InteractiveService interactivity,
        IOptions<BotSettings> botSettings)
    {
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._guildSettingBuilder = guildSettingBuilder;
        this._guildBuilders = guildBuilders;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelToggledCommandService = channelToggledCommandService;
        this._disabledChannelService = disabledChannelService;
        this._crownService = crownService;
        this._userService = userService;
        this._interactivity = interactivity;
        this._botSettings = botSettings.Value;
    }

    [ComponentInteraction(InteractionConstants.SetPrefix)]
    [ServerStaffOnly]
    public async Task SetPrefixButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetPrefixModal($"{InteractionConstants.SetPrefixModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetFmbotActivityThresholdButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetFmbotActivityThresholdModal($"{InteractionConstants.SetFmbotActivityThresholdModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task SetCrownActivityThresholdButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetCrownActivityThresholdModal($"{InteractionConstants.SetCrownActivityThresholdModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task SetCrownMinPlaycountButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateSetCrownMinPlaycountModal($"{InteractionConstants.SetCrownMinPlaycountModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandAdd)]
    [ServerStaffOnly]
    public async Task AddDisabledChannelCommandButton(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateAddDisabledChannelCommandModal(
                $"{InteractionConstants.ToggleCommand.ToggleCommandAddModal}:{channelId}:{categoryId}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandRemove)]
    [ServerStaffOnly]
    public async Task RemoveDisabledChannelCommandButton(string channelId, string categoryId)
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateRemoveDisabledChannelCommandModal(
                $"{InteractionConstants.ToggleCommand.ToggleCommandRemoveModal}:{channelId}:{categoryId}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandAdd)]
    [ServerStaffOnly]
    public async Task AddDisabledGuildCommandButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateAddDisabledGuildCommandModal($"{InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandRemove)]
    [ServerStaffOnly]
    public async Task RemoveDisabledGuildCommandButton()
    {
        var message = (this.Context.Interaction as MessageComponentInteraction)?.Message;
        if (message == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(
            ModalFactory.CreateRemoveDisabledGuildCommandModal($"{InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal}:{message.Id}")));
    }

    [ComponentInteraction(InteractionConstants.SetPrefixModal)]
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

    [ComponentInteraction(InteractionConstants.SetFmbotActivityThresholdModal)]
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

    [ComponentInteraction(InteractionConstants.SetCrownActivityThresholdModal)]
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

    [ComponentInteraction(InteractionConstants.SetCrownMinPlaycountModal)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandAddModal)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandRemoveModal)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal)]
    public async Task AddDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await this._guildService.AddGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await this._guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal)]
    public async Task RemoveDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await this._guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await this._guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.GuildMembers)]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync(params string[] inputs)
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
                    emote: EmojiProperties.Custom(DiscordConstants.Loading), disabled: true, style: ButtonStyle.Secondary);
            await message.ModifyAsync(m => m.Components = [components]);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await this._guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    [ServerStaffOnly]
    public async Task GetGuildSetting(params string[] inputs)
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
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.EmoteReactions:
                    response = GuildSettingBuilder.GuildReactionsAsync(new ContextModel(this.Context), prfx);

                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                    return;
                case GuildSetting.DefaultEmbedType:
                {
                    response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                {
                    response =
                        await this._guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                {
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context,
                        userSettings));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                }
                    break;
                case GuildSetting.CrownActivityThreshold:
                {
                    response =
                        await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownBlockedUsers:
                {
                    response = await this._guildSettingBuilder.BlockedUsersAsync(
                        new ContextModel(this.Context, userSettings), true);
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                }

                    break;
                case GuildSetting.CrownMinimumPlaycount:
                {
                    response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownSeeder:
                {
                    response = await this._guildSettingBuilder.CrownSeeder(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownsDisabled:
                {
                    response = await this._guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.DisabledCommands:
                {
                    response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
                        this.Context.Channel.Id);
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
                }
                    break;
                case GuildSetting.DisabledGuildCommands:
                {
                    response = await this._guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context));
                    await this.Context.SendResponse(this._interactivity, response, ephemeral: false);
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
    public async Task SetGuildEmbedType(params string[] inputs)
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandChannelFmType)]
    [ServerStaffOnly]
    public async Task SetChannelEmbedType(string channelId, string categoryId, params string[] inputs)
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

    [ComponentInteraction(InteractionConstants.RemoveCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task RemoveCrownMinPlaycount()
    {
        await this._guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, null);

        var response =
            await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandMove)]
    [ServerStaffOnly]
    public async Task ToggleChannelCommandMove(string channelId, string categoryId, string direction)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var response = await this._guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandClear)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandDisableAll)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandEnableAll)]
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

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandClear)]
    [ServerStaffOnly]
    public async Task ClearGuildDisabledCommands()
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
