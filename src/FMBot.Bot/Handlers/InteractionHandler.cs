using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly DiscordShardedClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;
    private readonly GuildService _guildService;

    private readonly IGuildDisabledCommandService _guildDisabledCommandService;
    private readonly IChannelDisabledCommandService _channelDisabledCommandService;

    private readonly IMemoryCache _cache;

    public InteractionHandler(DiscordShardedClient client,
        InteractionService interactionService,
        IServiceProvider provider,
        UserService userService,
        GuildService guildService,
        IGuildDisabledCommandService guildDisabledCommandService,
        IChannelDisabledCommandService channelDisabledCommandService,
        IMemoryCache cache)
    {
        this._client = client;
        this._interactionService = interactionService;
        this._provider = provider;
        this._userService = userService;
        this._guildService = guildService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._cache = cache;
        this._client.SlashCommandExecuted += SlashCommandAsync;
        this._client.AutocompleteExecuted += AutoCompleteAsync;
        this._client.SelectMenuExecuted += SelectMenuExecuted;
        this._client.ModalSubmitted += ModalSubmitted;
        this._client.UserCommandExecuted += UserCommandAsync;
        this._client.ButtonExecuted += ButtonExecuted;
    }

    private async Task SlashCommandAsync(SocketInteraction socketInteraction)
    {
        if (socketInteraction is not SocketSlashCommand socketSlashCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var contextUser = await this._userService.GetUserAsync(context.User.Id);

        var commandSearch = this._interactionService.SearchSlashCommand(socketSlashCommand);

        if (!commandSearch.IsSuccess)
        {
            Log.Error("Someone tried to execute a non-existent slash command! {slashCommand}", socketSlashCommand.CommandName);
            return;
        }

        var command = commandSearch.Command;

        if (contextUser?.Blocked == true)
        {
            await UserBlockedResponse(context);
            return;
        }

        if (!await CommandDisabled(context, command))
        {
            return;
        }

        var keepGoing = await CheckAttributes(context, command.Attributes);

        if (!keepGoing)
        {
            return;
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.SlashCommandsExecuted.WithLabels(command.Name).Inc();
        _ = this._userService.UpdateUserLastUsedAsync(context.User.Id);
    }

    private async Task UserCommandAsync(SocketInteraction socketInteraction)
    {
        if (socketInteraction is not SocketUserCommand socketUserCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var commandSearch = this._interactionService.SearchUserCommand(socketUserCommand);

        if (!commandSearch.IsSuccess || commandSearch.Command == null)
        {
            Log.Error("Someone tried to execute a non-existent user command! {slashCommand}", socketUserCommand.CommandName);
            return;
        }

        var keepGoing = await CheckAttributes(context, commandSearch.Command.Attributes);

        if (!keepGoing)
        {
            return;
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.UserCommandsExecuted.Inc();
    }

    private async Task<bool> CheckAttributes(ShardedInteractionContext context, IReadOnlyCollection<Attribute> attributes)
    {
        var contextUser = await this._userService.GetUserAsync(context.User.Id);

        if (attributes.OfType<UsernameSetRequired>().Any())
        {
            if (contextUser == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                var userNickname = (context.User as SocketGuildUser)?.Nickname;
                embed.UsernameNotSetErrorResponse("/", userNickname ?? context.User.Username);

                await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return false;
            }
        }
        if (attributes.OfType<UserSessionRequired>().Any())
        {
            if (contextUser?.SessionKeyLastFm == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                embed.SessionRequiredResponse("/");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
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
            if (lastIndex < DateTime.UtcNow.AddDays(-120))
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("Server member data is out of date, it was last updated over 120 days ago.\n" +
                                      $"Please run `/refreshmembers` to update this server.");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return false;
            }
        }

        return true;
    }

    private async Task AutoCompleteAsync(SocketInteraction socketInteraction)
    {
        var context = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.AutoCompletesExecuted.Inc();
    }

    private async Task SelectMenuExecuted(SocketInteraction socketInteraction)
    {
        var context = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.SelectMenusExecuted.Inc();
    }

    private async Task ModalSubmitted(SocketModal socketModal)
    {
        var context = new ShardedInteractionContext(this._client, socketModal);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.ModalsExecuted.Inc();
    }

    private async Task ButtonExecuted(SocketMessageComponent arg)
    {
        var context = new ShardedInteractionContext(this._client, arg);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.ButtonExecuted.Inc();
    }

    private async Task<bool> CommandDisabled(ShardedInteractionContext context, SlashCommandInfo searchResult)
    {
        if (context.Guild != null)
        {
            var disabledGuildCommands = this._guildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
            if (disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Name.Equals))
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this server.",
                    ephemeral: true);
                return false;
            }

            var disabledChannelCommands = this._channelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
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
