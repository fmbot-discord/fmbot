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
using FMBot.Domain.Interfaces;
using Serilog;
using FMBot.Persistence.Domain.Models;
using SkiaSharp;
using Fergun.Interactive;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class GameBuilders
{
    private readonly UserService _userService;
    private readonly GameService _gameService;
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;
    private readonly AlbumService _albumService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly CensorService _censorService;

    public GameBuilders(UserService userService, GameService gameService, ArtistsService artistsService, CountryService countryService, AlbumService albumService, IDataSourceFactory dataSourceFactory, CensorService censorService)
    {
        this._userService = userService;
        this._gameService = gameService;
        this._artistsService = artistsService;
        this._countryService = countryService;
        this._albumService = albumService;
        this._dataSourceFactory = dataSourceFactory;
        this._censorService = censorService;
    }

    public async Task<ResponseModel> StartArtistJumble(ContextModel context, int userId,
        CancellationTokenSource cancellationTokenSource)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var existingGame = await this._gameService.GetJumbleSessionForChannelId(context.DiscordChannel.Id);
        if (existingGame != null && !existingGame.DateEnded.HasValue)
        {
            if (existingGame.DateStarted <= DateTime.UtcNow.AddSeconds(-(GameService.JumbleSecondsToGuess + 10)))
            {
                await this._gameService.JumbleEndSession(existingGame);
            }
            else
            {
                response.Embed.WithDescription("Sorry, there is already a game in progress in this channel");
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (this._gameService.GameStartingAlready(context.DiscordChannel.Id))
        {
            response.Embed.WithDescription("Sorry, there is already a game in progress in this channel");
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var recentJumbles = await this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Artist);
        var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
        const int jumbleLimit = 30;
        if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
        {
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.Embed.WithDescription($"You've used up all your {jumbleLimit} jumbles of today. [Get supporter]({Constants.GetSupporterDiscordLink}) to play unlimited jumble games and much more.");
            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
            response.CommandResponse = CommandResponse.SupporterRequired;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);
        var artist = GameService.PickArtistForJumble(topArtists, recentJumbles);

        if (artist.artist == null)
        {
            response.Embed.WithDescription($"You've played all jumbles that are available for you today. Come back tomorrow or scrobble more music to play again.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        this._gameService.GameStartInProgress(context.DiscordChannel.Id);

        var databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        if (databaseArtist == null)
        {
            // Pick someone else and hope for the best
            artist = GameService.PickArtistForJumble(topArtists);
            databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
        }

        var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.Artist, artist.artist, cancellationTokenSource, artist.artist);

        CountryInfo artistCountry = null;
        if (databaseArtist?.CountryCode != null)
        {
            artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
        }

        var hints = GameService.GetJumbleArtistHints(databaseArtist, artist.userPlaycount, artistCountry);
        await this._gameService.JumbleStoreShowedHints(game, hints);

        BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints);
        response.Components = BuildJumbleComponents(game.JumbleSessionId, game.Hints, shuffledHidden: game.JumbledArtist == null);
        response.GameSessionId = game.JumbleSessionId;

        return response;
    }

    public async Task<ResponseModel> StartPixelJumble(ContextModel context, int userId,
   CancellationTokenSource cancellationTokenSource)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var existingGame = await this._gameService.GetJumbleSessionForChannelId(context.DiscordChannel.Id);
        if (existingGame != null && !existingGame.DateEnded.HasValue)
        {
            if (existingGame.DateStarted <= DateTime.UtcNow.AddSeconds(-(GameService.PixelationSecondsToGuess + 10)))
            {
                await this._gameService.JumbleEndSession(existingGame);
            }
            else
            {
                response.Embed.WithDescription("Sorry, there is already a game in progress in this channel");
                response.ResponseType = ResponseType.Embed;
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (this._gameService.GameStartingAlready(context.DiscordChannel.Id))
        {
            response.Embed.WithDescription("Sorry, there is already a game in progress in this channel");
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        var recentJumbles = await this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Pixelation);
        var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
        const int jumbleLimit = 30;
        if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.Embed.WithDescription($"You've used up all your {jumbleLimit} pixel jumbles of today. [Get supporter]({Constants.GetSupporterDiscordLink}) to play unlimited jumble games and much more.");
            response.Components = new ComponentBuilder()
                .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);
            response.CommandResponse = CommandResponse.SupporterRequired;
            return response;
        }

        this._gameService.GameStartInProgress(context.DiscordChannel.Id);
        var topAlbums = await this._albumService.GetUserAllTimeTopAlbums(userId, true);

        await this._albumService.FillMissingAlbumCovers(topAlbums);
        topAlbums = await this._censorService.RemoveNsfwAlbums(topAlbums);
        var album = GameService.PickAlbumForPixelation(topAlbums, recentJumbles);

        if (album == null)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithDescription($"You've played all jumbles that are available for you today. Come back tomorrow or scrobble more music to play again.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var databaseAlbum = await this._albumService.GetAlbumFromDatabase(album.ArtistName, album.AlbumName);
        if (databaseAlbum == null)
        {
            // Pick someone else and hope for the best
            album = GameService.PickAlbumForPixelation(topAlbums, null);
            databaseAlbum = await this._albumService.GetAlbumFromDatabase(album.ArtistName, album.AlbumName);
        }

        var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.Pixelation, album.AlbumName,
            cancellationTokenSource, album.ArtistName, album.AlbumName);

        var databaseArtist = await this._artistsService.GetArtistFromDatabase(album.ArtistName);
        CountryInfo artistCountry = null;
        if (databaseArtist?.CountryCode != null)
        {
            artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
        }

        var hints = GameService.GetJumbleAlbumHints(databaseAlbum, databaseArtist, album.UserPlaycount.GetValueOrDefault(), artistCountry);
        await this._gameService.JumbleStoreShowedHints(game, hints);

        BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints, jumbleType: JumbleType.Pixelation);

        var image = await this._gameService.GetSkImage(album.AlbumCoverUrl, album.AlbumName, album.ArtistName, game.JumbleSessionId);
        if (image == null)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithDescription("Sorry, something went wrong while getting album cover for your album.");
            response.CommandResponse = CommandResponse.Error;
            response.ResponseType = ResponseType.Embed;
            await this._gameService.JumbleEndSession(game);
            return response;
        }

        image = GameService.BlurCoverImage(image, game.BlurLevel.GetValueOrDefault());

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.FileName = $"pixelation-{game.JumbleSessionId}-{game.BlurLevel.GetValueOrDefault()}.png";

        response.Components = BuildJumbleComponents(game.JumbleSessionId, game.Hints, game.BlurLevel, game.JumbledArtist == null);
        response.GameSessionId = game.JumbleSessionId;

        return response;
    }

    private static void BuildJumbleEmbed(EmbedBuilder embed, string jumbledArtist, List<JumbleSessionHint> hints, bool canBeAnswered = true, JumbleType jumbleType = JumbleType.Artist)
    {
        var hintsShown = hints.Count(w => w.HintShown);
        var hintString = GameService.HintsToString(hints, hintsShown);

        embed.WithColor(DiscordConstants.InformationColorBlue);

        var hintTitle = jumbleType == JumbleType.Artist ? "Jumble - Guess the artist" : "Pixel Jumble - Guess the album";

        if (jumbledArtist != null)
        {
            embed.WithDescription($"### `{jumbledArtist}`");
        }

        if (hintsShown > 3)
        {
            hintTitle += $" ({hintsShown - 3} extra {StringExtensions.GetHintsString(hintsShown - 3)})";
        }
        embed.AddField(hintTitle, hintString);

        if (canBeAnswered)
        {
            embed.AddField("Add answer",
                jumbleType == JumbleType.Artist
                    ? $"Type your answer within {GameService.JumbleSecondsToGuess} seconds to make a guess"
                    : $"Type your answer within {GameService.PixelationSecondsToGuess} seconds to make a guess");
        }
    }

    public async Task<ResponseModel> GetJumbleUserStats(ContextModel context, UserSettingsModel userSettings, JumbleType jumbleType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var name = jumbleType == JumbleType.Artist ? "Jumble" : "Pixel Jumble";
        var pages = new List<PageBuilder>();

        var userPage = new PageBuilder();
        userPage.WithColor(DiscordConstants.InformationColorBlue);
        userPage.WithAuthor($"{name} user stats - {userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}");

        var userStats =
            await this._gameService.GetJumbleUserStats(userSettings.UserId, userSettings.DiscordUserId, jumbleType);

        if (userStats == null)
        {
            userPage.WithDescription(userSettings.DifferentUser
                ? "No stats available for this user."
                : "No stats available for you yet.");
        }
        else
        {
            var gameStats = new StringBuilder();
            gameStats.AppendLine($"- **{userStats.TotalGamesPlayed}** total games played");
            gameStats.AppendLine($"- **{userStats.GamesStarted}** games started");
            gameStats.AppendLine($"- **{userStats.GamesAnswered}** games answered");
            gameStats.AppendLine($"- **{userStats.GamesWon}** games won");
            gameStats.AppendLine($"- **{decimal.Round(userStats.AvgHintsShown, 1)}** average hints shown");
            userPage.AddField("Games", gameStats.ToString());

            var answerStats = new StringBuilder();
            answerStats.AppendLine($"- **{userStats.TotalAnswers}** total answers");
            answerStats.AppendLine($"- **{decimal.Round(userStats.AvgAnsweringTime, 1)}s** average answer time");
            answerStats.AppendLine($"- **{decimal.Round(userStats.AvgCorrectAnsweringTime, 1)}s** average correct answer time");
            answerStats.AppendLine($"- **{decimal.Round(userStats.AvgAttemptsUntilCorrect, 1)}** average attempts before win");
            answerStats.AppendLine($"- **{decimal.Round(userStats.WinRate, 1)}%** winrate");
            userPage.AddField("Answers", answerStats.ToString());
        }

        userPage.WithFooter("➡️ Server stats");

        pages.Add(userPage);

        var guildPage = new PageBuilder();
        guildPage.WithAuthor($"{name} server stats - {context.DiscordGuild.Name}");
        guildPage.WithColor(DiscordConstants.InformationColorBlue);

        var guildStats =
            await this._gameService.GetJumbleGuildStats(context.DiscordGuild.Id, jumbleType);

        if (guildStats == null)
        {
            guildPage.WithDescription("No stats available for this server.");
        }
        else
        {
            var gameStats = new StringBuilder();
            gameStats.AppendLine($"- **{guildStats.TotalGamesPlayed}** total games played");
            gameStats.AppendLine($"- **{guildStats.GamesSolved}** games solved");
            gameStats.AppendLine($"- **{guildStats.TotalReshuffles}** total reshuffles");
            gameStats.AppendLine($"- **{decimal.Round(guildStats.AvgHintsShown, 1)}** average hints shown");
            guildPage.AddField("Games", gameStats.ToString());

            var answerStats = new StringBuilder();
            answerStats.AppendLine($"- **{guildStats.TotalAnswers}** total answers");
            answerStats.AppendLine($"- **{decimal.Round(guildStats.AvgAnsweringTime, 1)}s** average answer time");
            answerStats.AppendLine($"- **{decimal.Round(guildStats.AvgCorrectAnsweringTime, 1)}s** average correct answer time");
            answerStats.AppendLine($"- **{decimal.Round(guildStats.AvgAttemptsUntilCorrect, 1)}** average attempts before win");
            guildPage.AddField("Answers", answerStats.ToString());

            var channels = new StringBuilder();
            var counter = 1;
            foreach (var channel in guildStats.Channels.Take(5))
            {
                channels.AppendLine($"{counter}. <#{channel.Id}> - {channel.Count} {StringExtensions.GetGamesString(channel.Count)}");
                counter++;
            }
            guildPage.AddField("Top channels", channels.ToString());

        }

        guildPage.WithFooter($"⬅️ {userSettings.DisplayName}'s stats");

        pages.Add(guildPage);

        response.StaticPaginator = StringService.BuildSimpleStaticPaginator(pages);

        return response;
    }

    private static ComponentBuilder BuildJumbleComponents(int gameId, List<JumbleSessionHint> hints, float? blur = null, bool shuffledHidden = false)
    {
        var addHintDisabled = hints.Count(c => c.HintShown) == hints.Count;
        var offerUnblur = blur is > 0.01f;

        return new ComponentBuilder()
            .WithButton(addHintDisabled && offerUnblur ? "Unblur" : "Add hint",
                addHintDisabled && offerUnblur ? $"{InteractionConstants.Game.JumbleUnblur}-{gameId}" : $"{InteractionConstants.Game.AddJumbleHint}-{gameId}",
                ButtonStyle.Secondary,
                disabled: addHintDisabled && !offerUnblur)
            .WithButton(shuffledHidden ? "Jumbled name" : "Reshuffle", $"{InteractionConstants.Game.JumbleReshuffle}-{gameId}", ButtonStyle.Secondary)
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

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image == null)
            {
                response.Embed.WithDescription("Sorry, something went wrong while getting album cover for your album.");
                response.CommandResponse = CommandResponse.Error;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            var blurLevel = currentGame.BlurLevel.Value switch
            {
                0.10f => 0.06f,
                0.06f => 0.04f,
                0.04f => 0.03f,
                0.03f => 0.02f,
                0.02f => 0.015f,
                0.015f => 0.01f,
                _ => currentGame.BlurLevel.Value
            };

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            image = GameService.BlurCoverImage(image, blurLevel);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"pixelation-{currentGame.JumbleSessionId}-{blurLevel}.png";
        }

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, true, currentGame.JumbleType);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, currentGame.BlurLevel, currentGame.JumbledArtist == null);

        return response;
    }

    public async Task<ResponseModel> JumbleUnblur(ContextModel context, int parsedGameId)
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

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image == null)
            {
                response.Embed.WithDescription("Sorry, something went wrong while getting album cover for your album.");
                response.CommandResponse = CommandResponse.Error;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            var blurLevel = currentGame.BlurLevel.Value switch
            {
                0.10f => 0.06f,
                0.06f => 0.04f,
                0.04f => 0.03f,
                0.03f => 0.02f,
                0.02f => 0.015f,
                0.015f => 0.01f,
                _ => currentGame.BlurLevel.Value
            };

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            image = GameService.BlurCoverImage(image, blurLevel);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"pixelation-{currentGame.JumbleSessionId}-{blurLevel}.png";
        }

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, currentGame.BlurLevel, currentGame.JumbledArtist == null);

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

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, true, currentGame.JumbleType);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, currentGame.BlurLevel, currentGame.JumbledArtist == null);

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                image = GameService.BlurCoverImage(image, currentGame.BlurLevel.Value);

                var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream();
                response.FileName = $"pixelation-{currentGame.JumbleSessionId}-{currentGame.BlurLevel.Value}.png";
            }
        }

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

        if (currentGame.StarterUserId != context.ContextUser.UserId)
        {
            response.Embed.WithDescription("You can't give up on someone else their game.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoPermission;
            return response;
        }

        await this._gameService.JumbleEndSession(currentGame);
        await this._gameService.CancelToken(context.DiscordChannel.Id);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false, currentGame.JumbleType);

        var userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

        response.Embed.AddField($"{userTitle} gave up!",
            currentGame.JumbleType == JumbleType.Artist ?
            $"It was **{currentGame.CorrectAnswer}**" :
            $"It was **{currentGame.CorrectAnswer}** by {currentGame.ArtistName}");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        if (currentGame.Answers is { Count: >= 1 })
        {
            var separateResponse = new EmbedBuilder();
            separateResponse.WithDescription(currentGame.JumbleType == JumbleType.Artist ?
                $"**{userTitle}** gave up! It was `{currentGame.CorrectAnswer}`" :
                $"**{userTitle}** gave up! It was `{currentGame.CorrectAnswer}` by {currentGame.ArtistName}");
            separateResponse.WithColor(DiscordConstants.AppleMusicRed);
            var components = new ComponentBuilder().WithButton("Play again",
                $"{InteractionConstants.Game.JumblePlayAgain}-{currentGame.JumbleType}",
                ButtonStyle.Secondary);
            if (context.DiscordChannel is IMessageChannel msgChannel)
            {
                _ = Task.Run(() => msgChannel.SendMessageAsync(embed: separateResponse.Build(), components: components.Build()));
            }
        }

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream();
                response.FileName = $"pixelation-{currentGame.JumbleSessionId}.png";
            }
        }

        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = currentGame.ArtistName,
            Album = currentGame.AlbumName
        };

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
            
            var messageLength = commandContext.Message.Content.Length;
            var answerLength = currentGame.CorrectAnswer.Length;
            
            if (messageLength >= answerLength / 2 &&
                messageLength <= Math.Min(answerLength + answerLength / 2, answerLength + 15))
            {
                var answerIsRight = GameService.AnswerIsRight(currentGame, commandContext.Message.Content);

                if (answerIsRight)
                {

                    _ = Task.Run(() => commandContext.Message.AddReactionAsync(new Emoji("✅")));

                    _ = Task.Run(() => this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, true));

                    _ = Task.Run(() => this._gameService.JumbleEndSession(currentGame));

                    var userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

                    var separateResponse = new EmbedBuilder();
                    separateResponse.WithDescription(
                        currentGame.JumbleType == JumbleType.Artist ?
                        $"**{userTitle}** got it! It was `{currentGame.CorrectAnswer}`" :
                        $"**{userTitle}** got it! It was `{currentGame.CorrectAnswer}` by {currentGame.ArtistName}");
                    var timeTaken = DateTime.UtcNow - currentGame.DateStarted;
                    separateResponse.WithFooter($"Answered in {timeTaken.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}s");
                    separateResponse.WithColor(DiscordConstants.SpotifyColorGreen);
                    var components = new ComponentBuilder().WithButton("Play again",
                        $"{InteractionConstants.Game.JumblePlayAgain}-{currentGame.JumbleType}",
                        ButtonStyle.Secondary);
                    if (context.DiscordChannel is IMessageChannel msgChannel)
                    {
                        _ = Task.Run(() => msgChannel.SendMessageAsync(embed: separateResponse.Build(), components: components.Build()));
                    }

                    if (currentGame.DiscordResponseId.HasValue)
                    {
                        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false, currentGame.JumbleType);
                        response.Components = null;
                        response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

                        var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
                        if (image != null)
                        {
                            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                            response.Stream = encoded.AsStream();
                            response.FileName = $"pixelation-{currentGame.JumbleSessionId}.png";
                        }

                        var msg = await commandContext.Channel.GetMessageAsync(currentGame.DiscordResponseId.Value);
                        if (msg is not IUserMessage message)
                        {
                            return;
                        }

                        if (PublicProperties.UsedCommandsResponseContextId.TryGetValue(message.Id, out var contextId))
                        {
                            await this._userService.UpdateInteractionContext(contextId, new ReferencedMusic
                            {
                                Artist = currentGame.ArtistName,
                                Album = currentGame.AlbumName
                            });
                        }

                        await message.ModifyAsync(m =>
                        {
                            m.Components = null;
                            m.Embed = response.Embed.Build();
                            m.Attachments = response.Stream != null ? new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>
                            {
                            new(response.Stream, response.Spoiler ? $"SPOILER_{response.FileName}.png" : $"{response.FileName}.png")
                            }) : null;
                        });
                    }
                }
                else
                {
                    var levenshteinDistance =
                        GameService.GetLevenshteinDistance(currentGame.CorrectAnswer.ToLower(), commandContext.Message.Content.ToLower());

                    if (levenshteinDistance == 1)
                    {
                        await commandContext.Message.AddReactionAsync(new Emoji("🤏"));
                    }
                    else
                    {
                        await commandContext.Message.AddReactionAsync(new Emoji("❌"));
                    }

                    await this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, false);
                }
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
        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, false, currentGame.JumbleType);

        response.Embed.AddField("Time is up!",
            currentGame.JumbleType == JumbleType.Artist ?
                $"It was **{currentGame.CorrectAnswer}**" :
                $"It was **{currentGame.CorrectAnswer}** by {currentGame.ArtistName}");
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        if (currentGame.Answers is { Count: >= 1 })
        {
            var separateResponse = new EmbedBuilder();
            separateResponse.WithDescription(currentGame.JumbleType == JumbleType.Artist ?
                $"Nobody guessed it right. It was `{currentGame.CorrectAnswer}`" :
                $"Nobody guessed it right. It was `{currentGame.CorrectAnswer}` by {currentGame.ArtistName}");
            separateResponse.WithColor(DiscordConstants.AppleMusicRed);
            var components = new ComponentBuilder().WithButton("Play again",
                $"{InteractionConstants.Game.JumblePlayAgain}-{currentGame.JumbleType}",
                ButtonStyle.Secondary);
            if (context.DiscordChannel is IMessageChannel msgChannel)
            {
                _ = Task.Run(() => msgChannel.SendMessageAsync(embed: separateResponse.Build(), components: components.Build()));
            }
        }

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream();
                response.FileName = $"pixelation-{currentGame.JumbleSessionId}.png";
            }
        }

        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = currentGame.ArtistName,
            Album = currentGame.AlbumName
        };

        return response;
    }
}
