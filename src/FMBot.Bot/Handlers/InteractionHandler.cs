using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
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
using Prometheus;
using Serilog;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly DiscordShardedClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IMemoryCache _cache;

    public InteractionHandler(DiscordShardedClient client,
        InteractionService interactionService,
        IServiceProvider provider,
        UserService userService,
        GuildService guildService,
        IMemoryCache cache,
        GuildSettingBuilder guildSettingBuilder)
    {
        this._client = client;
        this._interactionService = interactionService;
        this._provider = provider;
        this._userService = userService;
        this._guildService = guildService;
        this._cache = cache;
        this._guildSettingBuilder = guildSettingBuilder;
        this._client.SlashCommandExecuted += SlashCommandExecuted;
        this._client.AutocompleteExecuted += AutoCompleteExecuted;
        this._client.SelectMenuExecuted += SelectMenuExecuted;
        this._client.ModalSubmitted += ModalSubmitted;
        this._client.UserCommandExecuted += UserCommandExecuted;
        this._client.MessageCommandExecuted += MessageCommandExecuted;
        this._client.ButtonExecuted += ButtonExecuted;
    }

    private async Task SlashCommandExecuted(SocketInteraction socketInteraction)
    {
        Statistics.DiscordEvents.WithLabels(nameof(SlashCommandExecuted)).Inc();

        if (socketInteraction is not SocketSlashCommand socketSlashCommand)
        {
            return;
        }

        _ = Task.Run(() => ExecuteSlashCommand(socketInteraction, socketSlashCommand));
    }

    private async Task ExecuteSlashCommand(SocketInteraction socketInteraction, SocketSlashCommand socketSlashCommand)
    {
        using (Statistics.SlashCommandHandlerDuration.NewTimer())
        {
            var context = new ShardedInteractionContext(this._client, socketInteraction);
            var contextUser = await this._userService.GetUserAsync(context.User.Id);

            var commandSearch = this._interactionService.SearchSlashCommand(socketSlashCommand);

            if (!commandSearch.IsSuccess)
            {
                Log.Error("Someone tried to execute a non-existent slash command! {slashCommand}",
                    socketSlashCommand.CommandName);
                return;
            }

            var command = commandSearch.Command;

            if (contextUser?.Blocked == true)
            {
                await UserBlockedResponse(context);
                return;
            }

            if (!await CommandEnabled(context, command))
            {
                return;
            }

            var keepGoing = await CheckAttributes(context, command.Attributes);

            if (!keepGoing)
            {
                return;
            }

            await this._interactionService.ExecuteCommandAsync(context, this._provider);

            var integrationType =
                context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.IntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall)
                    ? "user_app"
                    : "guild_app";

            Statistics.SlashCommandsExecuted.WithLabels(command.Name, integrationType).Inc();

            _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));
            _ = Task.Run(() => this._userService.AddUserSlashCommandInteraction(context, command.Name));
        }
    }

    private async Task UserCommandExecuted(SocketInteraction socketInteraction)
    {
        Statistics.DiscordEvents.WithLabels(nameof(UserCommandExecuted)).Inc();

        if (socketInteraction is not SocketUserCommand socketUserCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var commandSearch = this._interactionService.SearchUserCommand(socketUserCommand);

        if (!commandSearch.IsSuccess)
        {
            return;
        }

        var keepGoing = await CheckAttributes(context, commandSearch.Command?.Attributes);

        if (!keepGoing)
        {
            return;
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.UserCommandsExecuted.Inc();

        _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task MessageCommandExecuted(SocketInteraction socketInteraction)
    {
        Statistics.DiscordEvents.WithLabels(nameof(MessageCommandExecuted)).Inc();

        if (socketInteraction is not SocketMessageCommand socketMessageCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var commandSearch = this._interactionService.SearchMessageCommand(socketMessageCommand);

        if (!commandSearch.IsSuccess)
        {
            return;
        }

        var keepGoing = await CheckAttributes(context, commandSearch.Command?.Attributes);

        if (!keepGoing)
        {
            return;
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.MessageCommandsExecuted.Inc();

        _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task AutoCompleteExecuted(SocketInteraction socketInteraction)
    {
        Statistics.DiscordEvents.WithLabels(nameof(AutoCompleteExecuted)).Inc();

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.AutoCompletesExecuted.Inc();
    }

    private async Task SelectMenuExecuted(SocketInteraction socketInteraction)
    {
        Statistics.DiscordEvents.WithLabels(nameof(SelectMenuExecuted)).Inc();

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.SelectMenusExecuted.Inc();
    }

    private async Task ModalSubmitted(SocketModal socketModal)
    {
        Statistics.DiscordEvents.WithLabels(nameof(ModalSubmitted)).Inc();

        var context = new ShardedInteractionContext(this._client, socketModal);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.ModalsExecuted.Inc();
    }

    private async Task ButtonExecuted(SocketMessageComponent socketMessageComponent)
    {
        Statistics.DiscordEvents.WithLabels(nameof(ButtonExecuted)).Inc();

        var context = new ShardedInteractionContext(this._client, socketMessageComponent);

        var commandSearch = this._interactionService.SearchComponentCommand(socketMessageComponent);

        if (!commandSearch.IsSuccess)
        {
            return;
        }

        var keepGoing = await CheckAttributes(context, commandSearch.Command.Attributes);

        if (!keepGoing)
        {
            return;
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.ButtonExecuted.Inc();

        _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));
    }

    private async Task<bool> CheckAttributes(ShardedInteractionContext context, IReadOnlyCollection<Attribute> attributes)
    {
        if (attributes == null)
        {
            return true;
        }

        if (attributes.OfType<ServerStaffOnly>().Any())
        {
            if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(context)))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(context);
                context.LogCommandUsed(CommandResponse.NoPermission);
                return false;
            }
        }
        if (attributes.OfType<UsernameSetRequired>().Any())
        {
            var userIsRegistered = await this._userService.UserRegisteredAsync(context.User);

            if (!userIsRegistered)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                var userNickname = (context.User as SocketGuildUser)?.DisplayName;
                embed.UsernameNotSetErrorResponse("/", userNickname ?? context.User.Username);

                await context.Interaction.RespondAsync(null, [embed.Build()], ephemeral: true, components: GenericEmbedService.UsernameNotSetErrorComponents().Build());
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return false;
            }
        }
        if (attributes.OfType<UserSessionRequired>().Any())
        {
            var contextUser = await this._userService.GetUserAsync(context.User.Id);

            if (contextUser?.SessionKeyLastFm == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                embed.SessionRequiredResponse("/");
                await context.Interaction.RespondAsync(null, [embed.Build()], ephemeral: true, components: GenericEmbedService.ReconnectComponents().Build());
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return false;
            }
        }
        if (attributes.OfType<GuildOnly>().Any())
        {
            if (context.Guild == null)
            {
                await context.Interaction.RespondAsync("This command is not supported in DMs.");
                context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return false;
            }
        }
        if (attributes.OfType<RequiresIndex>().Any() && context.Guild != null)
        {
            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
            if (lastIndex == null)
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                                      $"Please run `/refreshmembers` to create this.\n" +
                                      $"Note that this can take some time on large servers.");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return false;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-180))
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("Server member cache is out of date, it was last updated over 180 days ago.\n" +
                                      $"Please run `/refreshmembers` to update the cached memberlist");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return false;
            }
        }

        return true;
    }

    private static async Task<bool> CommandEnabled(ShardedInteractionContext context, ICommandInfo searchResult)
    {
        if (context.Guild != null)
        {
            var channelDisabled = DisabledChannelService.GetDisabledChannel(context.Channel.Id);
            if (channelDisabled)
            {
                var toggledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
                if (toggledChannelCommands != null &&
                    toggledChannelCommands.Any() &&
                    toggledChannelCommands.Any(searchResult.Name.Equals) &&
                    context.Channel != null)
                {
                    return true;
                }

                if (toggledChannelCommands != null &&
                    toggledChannelCommands.Any())
                {
                    await context.Interaction.RespondAsync(
                        "The command you're trying to execute is not enabled in this channel.",
                        ephemeral: true);
                }
                else
                {
                    await context.Interaction.RespondAsync(
                        "The bot has been disabled in this channel.",
                        ephemeral: true);
                }

                return false;
            }

            var disabledGuildCommands = GuildDisabledCommandService.GetToggledCommands(context.Guild?.Id);
            if (disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Name.Equals))
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this server.",
                    ephemeral: true);
                return false;
            }

            var disabledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
            if (disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Name.Equals) &&
                context.Channel != null)
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this channel.",
                    ephemeral: true);
                return false;
            }
        }

        return true;
    }

    private static async Task UserBlockedResponse(ShardedInteractionContext shardedCommandContext)
    {
        var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse("/");
        await shardedCommandContext.Channel.SendMessageAsync("", false, embed.Build());
        shardedCommandContext.LogCommandUsed(CommandResponse.UserBlocked);
        return;
    }
}
