using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;
using Prometheus;
using Serilog;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly ShardedGatewayClient _client;
    private readonly ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext> _appCommands;
    private readonly ComponentInteractionService<ComponentInteractionContext> _componentCommands;
    private readonly ComponentInteractionService<ModalInteractionContext> _modalCommands;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly CommandService<CommandContext> _commands;

    private readonly IMemoryCache _cache;
    private readonly InteractiveService _interactivity;

    public InteractionHandler(ShardedGatewayClient client,
        ApplicationCommandService<ApplicationCommandContext, AutocompleteInteractionContext> appCommands,
        ComponentInteractionService<ComponentInteractionContext> componentCommands,
        ComponentInteractionService<ModalInteractionContext> modalCommands,
        IServiceProvider provider,
        UserService userService,
        GuildService guildService,
        IMemoryCache cache,
        GuildSettingBuilder guildSettingBuilder,
        InteractiveService interactivity,
        CommandService<CommandContext> commands)
    {
        this._client = client;
        this._appCommands = appCommands;
        this._componentCommands = componentCommands;
        this._modalCommands = modalCommands;
        this._provider = provider;
        this._userService = userService;
        this._guildService = guildService;
        this._cache = cache;
        this._guildSettingBuilder = guildSettingBuilder;
        this._interactivity = interactivity;
        this._commands = commands;
        this._client.InteractionCreate += InteractionCreated;
    }

    private ValueTask InteractionCreated(GatewayClient client, Interaction interaction)
    {
        switch (interaction)
        {
            case SlashCommandInteraction slashCommand:
                Statistics.DiscordEvents.WithLabels("SlashCommandExecuted").Inc();
                _ = Task.Run(async () => await ExecuteSlashCommand(slashCommand, client));
                break;
            case UserCommandInteraction userCommand:
                Statistics.DiscordEvents.WithLabels("UserCommandExecuted").Inc();
                _ = Task.Run(async () => await ExecuteUserCommand(userCommand, client));
                break;
            case MessageCommandInteraction messageCommand:
                Statistics.DiscordEvents.WithLabels("MessageCommandExecuted").Inc();
                _ = Task.Run(async () => await ExecuteMessageCommand(messageCommand, client));
                break;
            case AutocompleteInteraction autocomplete:
                Statistics.DiscordEvents.WithLabels("AutoCompleteExecuted").Inc();
                _ = Task.Run(async () => await ExecuteAutocomplete(autocomplete, client));
                break;
            case ModalInteraction modal:
                Statistics.DiscordEvents.WithLabels("ModalSubmitted").Inc();
                _ = Task.Run(async () => await ExecuteModal(modal, client));
                break;
            case MessageComponentInteraction component:
                if (component.Data is ButtonInteractionData)
                {
                    Statistics.DiscordEvents.WithLabels("ButtonExecuted").Inc();
                    _ = Task.Run(async () => await ExecuteButton(component, client));
                }
                else
                {
                    Statistics.DiscordEvents.WithLabels("SelectMenuExecuted").Inc();
                    _ = Task.Run(async () => await ExecuteSelectMenu(component, client));
                }
                break;
        }
        return ValueTask.CompletedTask;
    }

    private async Task ExecuteSlashCommand(SlashCommandInteraction slashCommand, GatewayClient client)
    {
        using (Statistics.SlashCommandHandlerDuration.NewTimer())
        {
            var context = new ApplicationCommandContext(slashCommand, client);
            var contextUser = await this._userService.GetUserAsync(context.User.Id);

            var commandName = slashCommand.Data.Name;

            if (contextUser?.Blocked == true)
            {
                await UserBlockedResponse(context, commandName);
                return;
            }

            // Look up command info for attribute checking
            var commandInfo = _appCommands.GetCommands()
                .FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

            if (!await CommandEnabled(context, commandName))
            {
                return;
            }

            // Check custom attributes
            if (commandInfo != null)
            {
                var keepGoing = await CheckAttributes(context, commandInfo.Attributes, commandName);
                if (!keepGoing)
                {
                    return;
                }
            }

            _ = Task.Run(async () => await this._userService.InsertSlashCommandInteractionAsync(context, commandName));

            await this._appCommands.ExecuteAsync(context, this._provider);

            var integrationType =
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall)
                    ? "user_app"
                    : "guild_app";

            Statistics.SlashCommandsExecuted.WithLabels(commandName, integrationType).Inc();

            _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));
        }
    }

    private async Task ExecuteUserCommand(UserCommandInteraction userCommand, GatewayClient client)
    {
        var context = new ApplicationCommandContext(userCommand, client);
        var commandName = userCommand.Data.Name;

        var commandInfo = _appCommands.GetCommands()
            .FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (commandInfo != null)
        {
            var keepGoing = await CheckAttributes(context, commandInfo.Attributes, commandName);
            if (!keepGoing)
            {
                return;
            }
        }

        await this._appCommands.ExecuteAsync(context, this._provider);

        Statistics.UserCommandsExecuted.Inc();

        _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task ExecuteMessageCommand(MessageCommandInteraction messageCommand, GatewayClient client)
    {
        var context = new ApplicationCommandContext(messageCommand, client);
        var commandName = messageCommand.Data.Name;

        var commandInfo = _appCommands.GetCommands()
            .FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (commandInfo != null)
        {
            var keepGoing = await CheckAttributes(context, commandInfo.Attributes, commandName);
            if (!keepGoing)
            {
                return;
            }
        }

        await this._appCommands.ExecuteAsync(context, this._provider);

        Statistics.MessageCommandsExecuted.Inc();

        _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task ExecuteAutocomplete(AutocompleteInteraction autocomplete, GatewayClient client)
    {
        var context = new AutocompleteInteractionContext(autocomplete, client);
        await this._appCommands.ExecuteAutocompleteAsync(context, this._provider);

        Statistics.AutoCompletesExecuted.Inc();
    }

    private async Task ExecuteSelectMenu(MessageComponentInteraction component, GatewayClient client)
    {
        var context = new ComponentInteractionContext(component, client);
        await this._componentCommands.ExecuteAsync(context, this._provider);

        Statistics.SelectMenusExecuted.Inc();
    }

    private async Task ExecuteModal(ModalInteraction modal, GatewayClient client)
    {
        var context = new ComponentInteractionContext(modal, client);
        await this._componentCommands.ExecuteAsync(context, this._provider);

        Statistics.ModalsExecuted.Inc();
    }

    private async Task ExecuteButton(MessageComponentInteraction component, GatewayClient client)
    {
        var context = new ComponentInteractionContext(component, client);

        await this._componentCommands.ExecuteAsync(context, this._provider);

        Statistics.ButtonExecuted.Inc();

        _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task<bool> CheckAttributes(ApplicationCommandContext context,
        IReadOnlyDictionary<Type, IReadOnlyList<Attribute>> attributes, string commandName)
    {
        if (attributes == null || attributes.Count == 0)
        {
            return true;
        }

        // Helper to check if an attribute type exists
        bool HasAttribute<T>() where T : Attribute =>
            attributes.ContainsKey(typeof(T)) && attributes[typeof(T)].Count > 0;

        if (HasAttribute<ServerStaffOnly>())
        {
            if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(context)))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(context);
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, this._userService, commandName);
                return false;
            }
        }

        if (HasAttribute<UsernameSetRequired>())
        {
            var userIsRegistered = await this._userService.UserRegisteredAsync(context.User);

            if (!userIsRegistered)
            {
                var embed = new EmbedProperties()
                    .WithColor(DiscordConstants.LastFmColorRed);
                var guildUser = context.User as GuildUser;
                embed.UsernameNotSetErrorResponse("/", guildUser?.GetDisplayName() ?? context.User.GetDisplayName());

                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Embeds = [embed],
                    Flags = MessageFlags.Ephemeral,
                    Components = [GenericEmbedService.UsernameNotSetErrorComponents()]
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.UsernameNotSet }, this._userService, commandName);
                return false;
            }
        }

        if (HasAttribute<UserSessionRequired>())
        {
            var contextUser = await this._userService.GetUserAsync(context.User.Id);

            if (contextUser?.SessionKeyLastFm == null)
            {
                var embed = new EmbedProperties()
                    .WithColor(DiscordConstants.LastFmColorRed);
                embed.SessionRequiredResponse("/");
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Embeds = [embed],
                    Flags = MessageFlags.Ephemeral,
                    Components = [GenericEmbedService.ReconnectComponents()]
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.UsernameNotSet }, this._userService, commandName);
                return false;
            }
        }

        if (HasAttribute<GuildOnly>())
        {
            if (context.Guild == null)
            {
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Content = "This command is not supported in DMs."
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotSupportedInDm }, this._userService, commandName);
                return false;
            }
        }

        if (HasAttribute<RequiresIndex>() && context.Guild != null)
        {
            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
            if (lastIndex == null)
            {
                var embed = new EmbedProperties();
                embed.WithDescription(
                    "To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                    $"Please run `/refreshmembers` to create this.\n" +
                    $"Note that this can take some time on large servers.");
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Embeds = [embed]
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.IndexRequired }, this._userService, commandName);
                return false;
            }

            if (lastIndex < DateTime.UtcNow.AddDays(-180))
            {
                var embed = new EmbedProperties();
                embed.WithDescription("Server member cache is out of date, it was last updated over 180 days ago.\n" +
                                      $"Please run `/refreshmembers` to update the cached memberlist");
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Embeds = [embed]
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.IndexRequired }, this._userService, commandName);
                return false;
            }
        }

        return true;
    }

    private async Task<bool> CommandEnabled(ApplicationCommandContext context, string commandName)
    {
        if (context.Guild != null)
        {
            var channelDisabled = DisabledChannelService.GetDisabledChannel(context.Channel.Id);
            if (channelDisabled)
            {
                var toggledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
                if (toggledChannelCommands != null &&
                    toggledChannelCommands.Any() &&
                    toggledChannelCommands.Any(commandName.Equals) &&
                    context.Channel != null)
                {
                    return true;
                }

                if (toggledChannelCommands != null &&
                    toggledChannelCommands.Any())
                {
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                    {
                        Content = "The command you're trying to execute is not enabled in this channel.",
                        Flags = MessageFlags.Ephemeral
                    }));
                }
                else
                {
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                    {
                        Content = "The bot has been disabled in this channel.",
                        Flags = MessageFlags.Ephemeral
                    }));
                }

                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, this._userService, commandName);
                return false;
            }

            var disabledGuildCommands = GuildDisabledCommandService.GetToggledCommands(context.Guild?.Id);
            if (disabledGuildCommands != null &&
                disabledGuildCommands.Any(commandName.Equals))
            {
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Content = "The command you're trying to execute has been disabled in this server.",
                    Flags = MessageFlags.Ephemeral
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, this._userService, commandName);
                return false;
            }

            var disabledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
            if (disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(commandName.Equals) &&
                context.Channel != null)
            {
                await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties
                {
                    Content = "The command you're trying to execute has been disabled in this channel.",
                    Flags = MessageFlags.Ephemeral
                }));
                await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, this._userService, commandName);
                return false;
            }
        }

        return true;
    }

    private async Task UserBlockedResponse(ApplicationCommandContext context, string commandName)
    {
        var embed = new EmbedProperties().WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse("/");
        await context.Client.Rest.SendMessageAsync(context.Channel.Id, new MessageProperties
        {
            Embeds = [embed]
        });
        await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.UserBlocked }, this._userService, commandName);
    }
}
