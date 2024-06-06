using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using System.Text;
using System.Threading;
using FMBot.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using Discord.Commands;
using FMBot.Domain;
using Serilog;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class GameBuilders
{
    private readonly UserService _userService;
    private readonly GameService _gameService;
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;

    public GameBuilders(UserService userService, GameService gameService, ArtistsService artistsService, CountryService countryService)
    {
        this._userService = userService;
        this._gameService = gameService;
        this._artistsService = artistsService;
        this._countryService = countryService;
    }

    public static ResponseModel GameModePick(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Components = new ComponentBuilder()
                .WithButton("First correct answer wins", InteractionConstants.Game.StartJumbleFirstWins)
                .WithButton("Play as group", InteractionConstants.Game.StartJumbleGroup)
        };

        var description = new StringBuilder();

        description.AppendLine("Start jumble game");

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> StartJumbleFirstWins(ContextModel context, int userId,
        CancellationTokenSource cancellationTokenSource)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetJumbleSessionForChannelId(context.DiscordChannel.Id);
        if (existingGame != null && !existingGame.DateEnded.HasValue)
        {
            if (existingGame.DateStarted <= DateTime.UtcNow.AddSeconds(-(GameService.SecondsToGuess + 10)))
            {
                await this._gameService.JumbleEndSession(existingGame);
            }
            else
            {
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        var sessionCount = await this._gameService.GetJumbleSessionsCountToday(context.ContextUser.UserId);
        const int jumbleLimit = 30;
        if (!SupporterService.IsSupporter(context.ContextUser.UserType) && sessionCount > jumbleLimit)
        {
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.Embed.WithDescription($"You've used up all your {jumbleLimit} jumbles of today. [Get supporter]({Constants.GetSupporterDiscordLink}) to play unlimited jumble games and much more.");
            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
            response.CommandResponse = CommandResponse.SupporterRequired;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);

        if (topArtists.Count(c => c.UserPlaycount > 50) <= 3)
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription($"Sorry, you haven't listened to enough artists yet to use this command. Please try again later.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }
        
        var artist = await this._gameService.PickArtistForJumble(topArtists, sessionCount);

        var databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        if (databaseArtist == null)
        {
            // Pick someone else and hope for the best
            artist = await this._gameService.PickArtistForJumble(topArtists);
            databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        }

        var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.JumbleFirstWins, artist.artist, cancellationTokenSource);

        CountryInfo artistCountry = null;
        if (databaseArtist?.CountryCode != null)
        {
            artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
        }

        var hints = this._gameService.GetJumbleHints(databaseArtist, artist.userPlaycount, artistCountry);
        await this._gameService.JumbleStoreShowedHints(game, hints);

        BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints);
        response.Components = BuildJumbleComponents(game.JumbleSessionId, game.Hints);
        response.GameSessionId = game.JumbleSessionId;

        return response;
    }

    private static void BuildJumbleEmbed(EmbedBuilder embed, string jumbledArtist, List<JumbleSessionHint> hints, bool canBeAnswered = true)
    {
        var hintsShown = hints.Count(w => w.HintShown);
        var hintString = GameService.HintsToString(hints, hintsShown);

        embed.WithColor(DiscordConstants.InformationColorBlue);

        embed.WithAuthor("Guess the artist - Jumble");

        embed.WithDescription($"### `{jumbledArtist}`");

        var hintTitle = "Hints";
        if (hintsShown > 3)
        {
            hintTitle = $"Hints + {hintsShown - 3} extra {StringExtensions.GetHintsString(hintsShown - 3)}";
        }
        embed.AddField(hintTitle, hintString);

        if (canBeAnswered)
        {
            embed.AddField("Add answer", $"Type your answer within {GameService.SecondsToGuess} seconds to make a guess");
        }
    }

    private static ComponentBuilder BuildJumbleComponents(int gameId, List<JumbleSessionHint> hints)
    {
        var addHintDisabled = hints.Count(c => c.HintShown) == hints.Count;
        
        return new ComponentBuilder()
            .WithButton("Add hint", $"{InteractionConstants.Game.AddJumbleHint}-{gameId}", ButtonStyle.Secondary, disabled: addHintDisabled)
            .WithButton("Reshuffle", $"{InteractionConstants.Game.JumbleReshuffle}-{gameId}", ButtonStyle.Secondary)
            .WithButton("Give up", $"{InteractionConstants.Game.JumbleGiveUp}-{gameId}", ButtonStyle.Secondary);
    }

    public async Task<ResponseModel> JumbleAddHint(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        GameService.HintsToString(currentGame.Hints, currentGame.Hints.Count(w => w.HintShown) + 1);
        await this._gameService.JumbleStoreShowedHints(currentGame, currentGame.Hints);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints);

        return response;
    }

    public async Task<ResponseModel> JumbleReshuffle(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        await this._gameService.JumbleReshuffleArtist(currentGame);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints);

        return response;
    }

    public async Task<ResponseModel> JumbleGiveUp(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        await this._gameService.JumbleEndSession(currentGame);
        await this._gameService.CancelToken(context.DiscordChannel.Id);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false);

        response.Embed.AddField("You gave up!", $"The correct answer was **{currentGame.CorrectAnswer}**");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        return response;
    }

    public async Task JumbleProcessAnswer(ContextModel context, ICommandContext commandContext)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        try
        {
            var currentGame = await this._gameService.GetJumbleSessionForChannelId(context.DiscordChannel.Id);
            if (currentGame == null || currentGame.DateEnded.HasValue)
            {
                return;
            }

            var answerIsRight = GameService.AnswerIsRight(currentGame, commandContext.Message.Content);
            var messageLength = commandContext.Message.Content.Length;
            var answerLength = currentGame.CorrectAnswer.Length;

            if (answerIsRight)
            {

                _ = Task.Run(() => commandContext.Message.AddReactionAsync(new Emoji("✅")));

                _ = Task.Run(() => this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, commandContext.Message.Content, true));

                _ = Task.Run(() => this._gameService.JumbleEndSession(currentGame));

                var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
                response.Embed.WithDescription($"**{userTitle}** got it right! The answer was `{currentGame.CorrectAnswer}`");
                response.Embed.WithColor(DiscordConstants.SuccessColorGreen);
                await commandContext.Channel.SendMessageAsync(embed: response.Embed.Build());

                if (currentGame.DiscordResponseId.HasValue)
                {
                    BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false);
                    response.Components = null;
                    response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

                    var msg = await commandContext.Channel.GetMessageAsync(currentGame.DiscordResponseId.Value);
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
            else if (messageLength >= answerLength - 5 && messageLength <= answerLength + 2)
            {
                _ = Task.Run(() => commandContext.Message.AddReactionAsync(new Emoji("❌")));

                await this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, commandContext.Message.Content, false);
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in JumbleProcessAnswer: {exception}", e.Message, e);
        }
    }

    public async Task<ResponseModel> JumbleTimeExpired(ContextModel context, int gameSessionId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(gameSessionId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return null;
        }

        await this._gameService.JumbleEndSession(currentGame);
        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false);

        response.Embed.AddField("Time is up!", $"The correct answer was **{currentGame.CorrectAnswer}**");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        if (currentGame.Answers is { Count: >= 1 })
        {
            var separateResponse = new EmbedBuilder();
            separateResponse.WithDescription($"Nobody guessed it right. The answer was `{currentGame.CorrectAnswer}`");
            separateResponse.WithColor(DiscordConstants.AppleMusicRed);
            if (context.DiscordChannel is IMessageChannel msgChannel)
            {
                await msgChannel.SendMessageAsync(embed: separateResponse.Build());
            }
        }

        return response;
    }
}
