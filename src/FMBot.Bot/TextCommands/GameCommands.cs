using System.Threading.Tasks;
using System;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using System.Threading;
using FMBot.Domain;
using NetCord.Services.Commands;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands;

[ModuleName("Games")]
public class GameCommands(
    IOptions<BotSettings> botSettings,
    UserService userService,
    GameBuilders gameBuilders,
    IPrefixService prefixService,
    InteractiveService interactivity,
    GameService gameService,
    SettingService settingService)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;

    [Command("jumble", "j", "jmbl", "jum", "jumbmle")]
    [Summary("Play the Jumble game.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Games)]
    [Options("stats")]
    [SupporterEnhanced("Supporters can play unlimited Jumble games without a daily limit")]
    public async Task JumbleAsync([CommandParameter(Remainder = true)] string options = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            if (options != null && (options.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
                                    options.Contains("statistics", StringComparison.OrdinalIgnoreCase)))
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                var userSettings = await settingService.GetUser(options, contextUser, this.Context);

                var statResponse = await gameBuilders.GetJumbleUserStats(context, userSettings, JumbleType.Artist);
                await this.Context.SendResponse(this.Interactivity, statResponse, userService);
                await this.Context.LogCommandUsedAsync(statResponse, userService);
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var response = await gameBuilders.StartArtistJumble(context, contextUser.UserId, cancellationTokenSource);

            if (response.CommandResponse == CommandResponse.Cooldown)
            {
                _ = Task.Run(() => this.Context.Message.AddReactionAsync(new ReactionEmojiProperties("❌")));
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var responseId = await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
            Statistics.JumblesPlayed.WithLabels(nameof(JumbleType.Artist)).Inc();

            if (responseId?.Id != null && response.GameSessionId.HasValue)
            {
                await gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Id);
                await JumbleTimeExpired(context, responseId.Id, cancellationTokenSource.Token, response.GameSessionId.Value, GameService.JumbleSecondsToGuess);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task JumbleTimeExpired(ContextModel context, ulong responseId, CancellationToken cancellationToken,
        int gameSessionId, int secondsToGuess)
    {
        await Task.Delay(secondsToGuess * 1000, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var response = await gameBuilders.JumbleTimeExpired(context, gameSessionId);

        if (response == null)
        {
            return;
        }

        await this.Context.Client.Rest.ModifyMessageAsync(context.DiscordChannel.Id, responseId, m =>
        {
            m.Components = [];
            m.Embeds = [response.Embed];
            m.Attachments = response.Stream != null
                ? [new AttachmentProperties(response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName, response.Stream)]
                : null;
        });

        if (PublicProperties.UsedCommandsResponseContextId.TryGetValue(responseId, out var contextId))
        {
            await userService.UpdateInteractionContext(contextId, response.ReferencedMusic);
        }
    }

    [Command("pixel", "px", "pixelation", "aj", "abj", "popidle", "pixeljumble", "pxj")]
    [Summary("Play the pixel jumble game with albums.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Games)]
    [Options("stats")]
    [SupporterEnhanced("Supporters can play unlimited Pixel Jumble games without a daily limit")]
    public async Task PixelAsync([CommandParameter(Remainder = true)] string options = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var context = new ContextModel(this.Context, prfx, contextUser);
            if (options != null && (options.Contains("stats", StringComparison.OrdinalIgnoreCase) ||
                                    options.Contains("statistics", StringComparison.OrdinalIgnoreCase)))
            {
                _ = this.Context.Channel?.TriggerTypingStateAsync()!;

                var userSettings = await settingService.GetUser(options, contextUser, this.Context);

                var statResponse = await gameBuilders.GetJumbleUserStats(context, userSettings, JumbleType.Pixelation);
                await this.Context.SendResponse(this.Interactivity, statResponse, userService);
                await this.Context.LogCommandUsedAsync(statResponse, userService);
                return;
            }

            var cancellationTokenSource = new CancellationTokenSource();
            var response = await gameBuilders.StartPixelJumble(context, contextUser.UserId, cancellationTokenSource);

            if (response.CommandResponse == CommandResponse.Cooldown)
            {
                _ = Task.Run(() => this.Context.Message.AddReactionAsync(new ReactionEmojiProperties("❌")));
                await this.Context.LogCommandUsedAsync(response, userService);
                return;
            }

            var responseId = await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
            Statistics.JumblesPlayed.WithLabels(nameof(JumbleType.Pixelation)).Inc();

            if (responseId?.Id != null && response.GameSessionId.HasValue)
            {
                await gameService.JumbleAddResponseId(response.GameSessionId.Value, responseId.Id);
                await JumbleTimeExpired(context, responseId.Id, cancellationTokenSource.Token, response.GameSessionId.Value,
                    GameService.PixelationSecondsToGuess);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
