using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using System.Text;
using FMBot.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using Discord.Commands;
using Serilog;

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

    public async Task<ResponseModel> StartJumbleFirstWins(ContextModel context, int userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (existingGame != null && !existingGame.DateEnded.HasValue)
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription($"There is already a game in progress. Finish it or give up before starting a new one.");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);
        var artist = await this._gameService.PickArtistForJumble(topArtists);

        var databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        if (databaseArtist == null)
        {
            // Pick someone else
            artist = await this._gameService.PickArtistForJumble(topArtists);
        }

        var game = await this._gameService.StartJumbleGame(userId, context, GameType.JumbleFirstWins, artist.artist);

        CountryInfo artistCountry = null;
        if (databaseArtist?.CountryCode != null)
        {
            artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
        }

        game.Hints = this._gameService.GetJumbleHints(databaseArtist, artist.userPlaycount, artistCountry);

        BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints);
        response.Components = BuildJumbleComponents(game.GameId);

        return response;
    }

    private void BuildJumbleEmbed(EmbedBuilder embed, string jumbledArtist, List<GameHintModel> hints, bool canBeAnswered = true)
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

    private static ComponentBuilder BuildJumbleComponents(int gameId)
    {
        return new ComponentBuilder()
            .WithButton("Add hint", $"{InteractionConstants.Game.AddJumbleHint}-{gameId}", ButtonStyle.Secondary)
            .WithButton("Reshuffle", $"{InteractionConstants.Game.JumbleReshuffle}-{gameId}", ButtonStyle.Secondary)
            .WithButton("Give up", $"{InteractionConstants.Game.JumbleGiveUp}-{gameId}", ButtonStyle.Secondary);
    }

    public async Task<ResponseModel> JumbleAddHint(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetGame(context.DiscordChannel.Id, parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        GameService.HintsToString(currentGame.Hints, currentGame.Hints.Count(w => w.HintShown) + 1);
        await this._gameService.JumbleStoreShowedHints(currentGame, currentGame.Hints);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints);
        response.Components = BuildJumbleComponents(currentGame.GameId);

        return response;
    }

    public async Task<ResponseModel> JumbleReshuffle(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetGame(context.DiscordChannel.Id, parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        await this._gameService.JumbleReshuffleArtist(currentGame);



        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints);
        response.Components = BuildJumbleComponents(currentGame.GameId);

        return response;
    }

    public async Task<ResponseModel> JumbleGiveUp(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetGame(context.DiscordChannel.Id, parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return response;
        }

        await this._gameService.JumbleEndGame(currentGame, context.DiscordChannel.Id);

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
            var currentGame = await this._gameService.GetGame(context.DiscordChannel.Id);
            if (currentGame == null || currentGame.DateEnded.HasValue)
            {
                return;
            }

            var answerIsRight = GameService.AnswerIsRight(currentGame, commandContext.Message.Content);
            var messageLength = commandContext.Message.Content.Length;
            var answerLength = currentGame.CorrectAnswer.Length;

            if (answerIsRight)
            {
                await commandContext.Message.AddReactionAsync(new Emoji("✅"));

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

                await this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, commandContext.Message.Content,
                    true);

                await this._gameService.JumbleEndGame(currentGame, context.DiscordChannel.Id);
            }
            else if (messageLength >= answerLength - 2 && messageLength <= answerLength + 2)
            {
                await commandContext.Message.AddReactionAsync(new Emoji("❌"));

                await this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, commandContext.Message.Content,
                    false);
            }
        }
        catch (Exception e)
        {
            Log.Error("Error in JumbleProcessAnswer: {exception}", e.Message, e);
        }
    }

    public async Task<ResponseModel> JumbleTimeExpired(ContextModel context, ulong responseId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var currentGame = await this._gameService.GetGame(context.DiscordChannel.Id);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return null;
        }

        await this._gameService.JumbleEndGame(currentGame, context.DiscordChannel.Id);
        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false);

        response.Embed.AddField("Time is up!", $"The correct answer was **{currentGame.CorrectAnswer}**");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        if (currentGame.Answers != null && currentGame.Answers.Count >= 1)
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
