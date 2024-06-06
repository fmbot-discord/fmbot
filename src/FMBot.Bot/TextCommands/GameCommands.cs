using System.Threading.Tasks;
using System;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Fergun.Interactive;
using Discord;
using System.Threading;

namespace FMBot.Bot.TextCommands;

//[Name("Games")]
public class GameCommands : BaseCommandModule
{
    private readonly UserService _userService;
    private readonly GameBuilders _gameBuilders;
    private readonly IPrefixService _prefixService;
    private readonly GameService _gameService;
    private readonly AdminService _adminService;

    private InteractiveService Interactivity { get; }

    public GameCommands(IOptions<BotSettings> botSettings, UserService userService, GameBuilders gameBuilders, IPrefixService prefixService, InteractiveService interactivity, GameService gameService, AdminService adminService) : base(botSettings)
    {
        this._userService = userService;
        this._gameBuilders = gameBuilders;
        this._prefixService = prefixService;
        this.Interactivity = interactivity;
        this._gameService = gameService;
        this._adminService = adminService;
    }

    [Command("jumble", RunMode = RunMode.Async)]
    [Summary("Play the Jumble game.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    [Alias("j", "jmbl", "jum", "jumbmle")]
    public async Task JumbleAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            var cancellationTokenSource = new CancellationTokenSource();
            var response = await this._gameBuilders.StartJumbleFirstWins(context, contextUser.UserId, cancellationTokenSource);

            if (response.CommandResponse == CommandResponse.Cooldown)
            {
                _ = Task.Run(() => this.Context.Message.AddReactionAsync(new Emoji("❌")));
            this.Context.LogCommandUsed(response.CommandResponse);
            return;
            }

            var responseId = await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);

            if (responseId.HasValue && response.GameSessionId.HasValue)
            {
                await this._gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Value);
                await JumbleTimeExpired(context, responseId.Value, cancellationTokenSource.Token, response.GameSessionId.Value);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    private async Task JumbleTimeExpired(ContextModel context, ulong responseId, CancellationToken cancellationToken,
        int gameSessionId)
    {
        await Task.Delay(GameService.SecondsToGuess * 1000, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var response = await this._gameBuilders.JumbleTimeExpired(context, gameSessionId);

        if (response == null)
        {
            return;
        }

        var msg = await this.Context.Channel.GetMessageAsync(responseId);
        if (msg is not IUserMessage message)
        {
            return;
        }

        await message.ModifyAsync(m =>
        {
            m.Components = null;
            m.Embed = response.Embed.Build();
        });
    }

}
