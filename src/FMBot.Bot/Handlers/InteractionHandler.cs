using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Services;
using FMBot.Domain;

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly DiscordShardedClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;

    public InteractionHandler(DiscordShardedClient client, InteractionService interactionService, IServiceProvider provider, UserService userService)
    {
        this._client = client;
        this._interactionService = interactionService;
        this._provider = provider;
        this._userService = userService;
        this._client.SlashCommandExecuted += HandleInteractionAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction socketInteraction)
    {
        var interactionContext = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(interactionContext, this._provider);

        Statistics.SlashCommandsExecuted.Inc();
        _ = this._userService.UpdateUserLastUsedAsync(interactionContext.User.Id);
    }
}
