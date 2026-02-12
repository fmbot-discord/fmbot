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

public class GuildSettingInteractions(
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
    : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly BotSettings _botSettings = botSettings.Value;

    [ComponentInteraction(InteractionConstants.GuildLanguageSetting)]
    [ServerStaffOnly]
    public async Task SetGuildLanguage()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedLocale = stringMenuInteraction.Data.SelectedValues[0];

        await guildService.SetGuildLocaleAsync(this.Context.Guild, selectedLocale);

        var contextModel = new ContextModel(this.Context) { Locale = selectedLocale };
        var response = await guildSettingBuilder.SetLanguage(contextModel, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
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
            await guildService.SetGuildPrefixAsync(this.Context.Guild, null);
            prefixService.StorePrefix(null, this.Context.Guild.Id);
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
            await guildService.SetGuildPrefixAsync(this.Context.Guild, newPrefix);
            prefixService.StorePrefix(newPrefix, this.Context.Guild.Id);
        }

        var response = await guildSettingBuilder.SetPrefix(new ContextModel(this.Context), this.Context.User);
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

        await guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context), this.Context.User);
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

        await guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context), this.Context.User);
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

        await guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, result);

        var response = await guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
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
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        await guildService.DisableChannelCommandsAsync(selectedChannel, guild.GuildId,
            new List<string> { command.ToLower() }, this.Context.Guild.Id);

        await channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);
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

        await guildService.EnableChannelCommandsAsync(selectedChannel, new List<string> { command.ToLower() },
            this.Context.Guild.Id);

        await channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context), parsedChannelId, parsedCategoryId);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandAddModal)]
    public async Task AddDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await guildService.AddGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandRemoveModal)]
    public async Task RemoveDisabledGuildCommand(string messageId)
    {
        var command = this.Context.GetModalValue("command");

        await guildService.RemoveGuildDisabledCommandAsync(this.Context.Guild, command.ToLower());
        GuildDisabledCommandService.StoreDisabledCommands((await guildService.GetGuildAsync(this.Context.Guild.Id)).DisabledCommands, this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }

    [ComponentInteraction(InteractionConstants.GuildMembers)]
    [RequiresIndex]
    [GuildOnly]
    public async Task MemberOverviewAsync()
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedValue = stringMenuInteraction.Data.SelectedValues[0];

            if (!Enum.TryParse(selectedValue, out GuildViewType viewType))
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

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

            var response =
                await guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                    viewType);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    [ServerStaffOnly]
    public async Task GetGuildSetting()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var setting = stringMenuInteraction.Data.SelectedValues[0].Replace("gs-", "");

        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
        {
            ResponseModel response;
            switch (guildSetting)
            {
                case GuildSetting.TextPrefix:
                {
                    response = await guildSettingBuilder.SetPrefix(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.EmoteReactions:
                    response = GuildSettingBuilder.GuildReactionsAsync(new ContextModel(this.Context), prfx);

                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                    return;
                case GuildSetting.DefaultEmbedType:
                {
                    response = await guildSettingBuilder.GuildMode(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                {
                    response =
                        await guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                {
                    response = await guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context,
                        userSettings));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
                }
                    break;
                case GuildSetting.CrownActivityThreshold:
                {
                    response =
                        await guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownBlockedUsers:
                {
                    response = await guildSettingBuilder.BlockedUsersAsync(
                        new ContextModel(this.Context, userSettings), true);
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
                }

                    break;
                case GuildSetting.CrownMinimumPlaycount:
                {
                    response = await guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownSeeder:
                {
                    response = await guildSettingBuilder.CrownSeeder(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.CrownsDisabled:
                {
                    response = await guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.DisabledCommands:
                {
                    response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
                        this.Context.Channel.Id);
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.DisabledGuildCommands:
                {
                    response = await guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context));
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
                }
                    break;
                case GuildSetting.Language:
                {
                    var locale = await guildService.GetGuildLocaleAsync(this.Context.Guild.Id);
                    response = await guildSettingBuilder.SetLanguage(new ContextModel(this.Context) { Locale = locale });
                    await this.Context.SendResponse(interactivity, response, userService, ephemeral: false);
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

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var guildCrowns = await crownService.GetAllCrownsForGuild(guild.GuildId);

        var amountOfCrownsSeeded = await crownService.SeedCrownsForGuild(guild, guildCrowns);

        response = await guildSettingBuilder.CrownSeederDone(new ContextModel(this.Context),
            amountOfCrownsSeeded);
        await this.Context.UpdateInteractionEmbed(response, defer: false);
    }

    [ComponentInteraction(InteractionConstants.RemovePrefix)]
    [ServerStaffOnly]
    public async Task RemovePrefix()
    {
        await guildService.SetGuildPrefixAsync(this.Context.Guild, null);
        prefixService.StorePrefix(null, this.Context.Guild.Id);

        var response = await guildSettingBuilder.SetPrefix(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.FmGuildSettingType)]
    [ServerStaffOnly]
    public async Task SetGuildEmbedType()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            if (selectedValues.Count > 0 && Enum.TryParse(selectedValues[0], out FmEmbedType embedType))
            {
                await guildService.ChangeGuildSettingAsync(this.Context.Guild, embedType);
            }
            else
            {
                await guildService.ChangeGuildSettingAsync(this.Context.Guild, null);
            }

            var response = await guildSettingBuilder.GuildMode(new ContextModel(this.Context), this.Context.User);

            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandChannelFmType)]
    [ServerStaffOnly]
    public async Task SetChannelEmbedType(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = this.Context.Guild.Channels.TryGetValue(parsedChannelId, out var ch) ? ch : null;

        if (selectedValues.Count > 0 && Enum.TryParse(selectedValues[0], out FmEmbedType embedType))
        {
            await guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, embedType,
                this.Context.Guild.Id);
        }
        else
        {
            await guildService.SetChannelEmbedType(selectedChannel, guild.GuildId, null, this.Context.Guild.Id);
        }

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveFmbotActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveFmbotActivityThreshold()
    {
        await guildService.SetFmbotActivityThresholdDaysAsync(this.Context.Guild, null);

        var response =
            await guildSettingBuilder.SetFmbotActivityThreshold(new ContextModel(this.Context),
                this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownActivityThreshold)]
    [ServerStaffOnly]
    public async Task RemoveCrownActivityThreshold()
    {
        await guildService.SetCrownActivityThresholdDaysAsync(this.Context.Guild, null);

        var response =
            await guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context),
                this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.RemoveCrownMinPlaycount)]
    [ServerStaffOnly]
    public async Task RemoveCrownMinPlaycount()
    {
        await guildService.SetMinimumCrownPlaycountThresholdAsync(this.Context.Guild, null);

        var response =
            await guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandMove)]
    [ServerStaffOnly]
    public async Task ToggleChannelCommandMove(string channelId, string categoryId, string direction)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
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

        await guildService.ClearDisabledChannelCommandsAsync(selectedChannel, this.Context.Guild.Id);

        await channelToggledCommandService.ReloadToggledCommands(this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleCommandDisableAll)]
    [ServerStaffOnly]
    public async Task DisableChannel(string channelId, string categoryId)
    {
        var parsedChannelId = ulong.Parse(channelId);
        var parsedCategoryId = ulong.Parse(categoryId);

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
        var selectedChannel = this.Context.Guild.Channels.TryGetValue(parsedChannelId, out var ch) ? ch : null;

        await guildService.DisableChannelAsync(selectedChannel, guild.GuildId, this.Context.Guild.Id);
        await disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
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

        await guildService.EnableChannelAsync(selectedChannel, this.Context.Guild.Id);
        await disabledChannelService.ReloadDisabledChannels(this.Context.Guild.Id);

        var response = await guildSettingBuilder.ToggleChannelCommand(new ContextModel(this.Context),
            parsedChannelId, parsedCategoryId, this.Context.User);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCommand.ToggleGuildCommandClear)]
    [ServerStaffOnly]
    public async Task ClearGuildDisabledCommands()
    {
        await guildService.ClearGuildDisabledCommandAsync(this.Context.Guild);
        await guildDisabledCommandService.ReloadDisabledCommands(this.Context.Guild.Id);

        var response =
            await guildSettingBuilder.ToggleGuildCommand(new ContextModel(this.Context), this.Context.User);

        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCrowns.Enable)]
    [ServerStaffOnly]
    public async Task EnableCrowns()
    {
        var response = await guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context), false);
        await this.Context.UpdateInteractionEmbed(response);
    }

    [ComponentInteraction(InteractionConstants.ToggleCrowns.Disable)]
    [ServerStaffOnly]
    public async Task DisableCrowns()
    {
        var response = await guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context), true);
        await this.Context.UpdateInteractionEmbed(response);
    }
}
