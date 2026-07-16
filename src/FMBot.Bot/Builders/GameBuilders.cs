using System;
using System.Collections.Generic;
using System.Linq;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using System.Text;
using System.Threading;
using FMBot.Domain.Models;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using Serilog;
using FMBot.Persistence.Domain.Models;
using SkiaSharp;
using FMBot.Domain.Extensions;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.Commands;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class GameBuilders
{
    private readonly UserService _userService;
    private readonly GameService _gameService;
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;
    private readonly AlbumService _albumService;
    private readonly CensorService _censorService;

    public GameBuilders(UserService userService, GameService gameService, ArtistsService artistsService,
        CountryService countryService, AlbumService albumService, CensorService censorService)
    {
        this._userService = userService;
        this._gameService = gameService;
        this._artistsService = artistsService;
        this._countryService = countryService;
        this._albumService = albumService;
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
        if (existingGame is { DateEnded: null })
        {
            if (existingGame.DateStarted <= DateTime.UtcNow.AddSeconds(-(GameService.JumbleSecondsToGuess + 10)))
            {
                await this._gameService.JumbleEndSession(existingGame);
            }
            else
            {
                response.Embed.WithDescription(context.Localize("jumble.gameInProgress"));
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (!GameService.TryClaimGameStart(context.DiscordChannel.Id))
        {
            response.Embed.WithDescription(context.Localize("jumble.gameInProgress"));
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        try
        {
            var recentJumbles = await this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Artist);
            var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
            var premiumGuild = context.DiscordGuild != null &&
                               PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
            var jumbleLimit = premiumGuild ? Constants.PremiumServerJumbleDailyLimit : Constants.JumbleDailyLimit;
            if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
            {
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                var limitDescription = new StringBuilder();
                limitDescription.AppendLine(context.Localize("jumble.dailyLimitReached",
                    ("limit", jumbleLimit.ToString())));
                response.Components = new ActionRowProperties()
                    .WithButton(context.Localize("buttons.getFmbotSupporter"), style: ButtonStyle.Primary,
                        customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "jumble-dailylimit"));

                if (!premiumGuild)
                {
                    limitDescription.AppendLine(context.Localize("jumble.premiumServerUpsell"));
                    response.Components.WithButton(context.Localize("buttons.premiumServer"), style: ButtonStyle.Secondary,
                        customId: $"{InteractionConstants.PremiumServer.GetOverview}:jumble-dailylimit");
                }

                response.Embed.WithDescription(limitDescription.ToString());
                response.CommandResponse = CommandResponse.SupporterRequired;
                return response;
            }

            var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userId, true);
            var artistPopularities = await this._artistsService.GetArtistsPopularity(topArtists);
            var artist = GameService.PickArtistForJumble(topArtists, artistPopularities, recentJumbles);

            if (artist.artist == null)
            {
                response.Embed.WithDescription(context.Localize("jumble.playedAllToday"));
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            var databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
            if (databaseArtist == null)
            {
                // Pick someone else and hope for the best
                artist = GameService.PickArtistForJumble(topArtists, artistPopularities);
                databaseArtist = await this._artistsService.GetArtistFromDatabase(artist.artist);
            }

            var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.Artist, artist.artist,
                cancellationTokenSource, artist.artist);

            CountryInfo artistCountry = null;
            if (databaseArtist?.CountryCode != null)
            {
                artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
            }

            var hints = GameService.GetJumbleArtistHints(databaseArtist, artist.userPlaycount, context.Localizer,
                artistCountry);
            await this._gameService.JumbleStoreShowedHints(game, hints);

            BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints, context.Localizer);
            response.Components =
                BuildJumbleComponents(game.JumbleSessionId, game.Hints, context.Localizer,
                    shuffledHidden: game.JumbledArtist == null);
            response.GameSessionId = game.JumbleSessionId;

            return response;
        }
        finally
        {
            GameService.ClearGameStartClaim(context.DiscordChannel.Id);
        }
    }

    public async Task<ResponseModel> StartPixelJumble(ContextModel context, int userId,
        CancellationTokenSource cancellationTokenSource)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var existingGame = await this._gameService.GetJumbleSessionForChannelId(context.DiscordChannel.Id);
        if (existingGame is { DateEnded: null })
        {
            if (existingGame.DateStarted <= DateTime.UtcNow.AddSeconds(-(GameService.PixelationSecondsToGuess + 10)))
            {
                await this._gameService.JumbleEndSession(existingGame);
            }
            else
            {
                response.Embed.WithDescription(context.Localize("jumble.gameInProgress"));
                response.ResponseType = ResponseType.Embed;
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (!GameService.TryClaimGameStart(context.DiscordChannel.Id))
        {
            response.Embed.WithDescription(context.Localize("jumble.gameInProgress"));
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        try
        {
            var recentJumbles = await this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Pixelation);
            var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
            var premiumGuild = context.DiscordGuild != null &&
                               PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
            var jumbleLimit = premiumGuild ? Constants.PremiumServerJumbleDailyLimit : Constants.JumbleDailyLimit;
            if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
            {
                response.ResponseType = ResponseType.Embed;
                response.Embed.WithColor(DiscordConstants.InformationColorBlue);
                var limitDescription = new StringBuilder();
                limitDescription.AppendLine(context.Localize("jumble.dailyLimitReachedPixel",
                    ("limit", jumbleLimit.ToString())));
                response.Components = new ActionRowProperties()
                    .WithButton(context.Localize("buttons.getFmbotSupporter"), style: ButtonStyle.Primary,
                        customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "pixel-dailylimit"));

                if (!premiumGuild)
                {
                    limitDescription.AppendLine(context.Localize("jumble.premiumServerUpsell"));
                    response.Components.WithButton(context.Localize("buttons.premiumServer"), style: ButtonStyle.Secondary,
                        customId: $"{InteractionConstants.PremiumServer.GetOverview}:pixel-dailylimit");
                }

                response.Embed.WithDescription(limitDescription.ToString());
                response.CommandResponse = CommandResponse.SupporterRequired;
                return response;
            }

            var topAlbums = await this._albumService.GetUserAllTimeTopAlbums(userId, true);

            await this._albumService.FillMissingAlbumCovers(topAlbums);
            topAlbums = await this._censorService.RemoveNsfwAlbums(topAlbums);
            var albumPopularities = await this._albumService.GetAlbumsPopularity(topAlbums);
            var album = GameService.PickAlbumForPixelation(topAlbums, albumPopularities, recentJumbles);

            if (album == null)
            {
                response.ResponseType = ResponseType.Embed;
                response.Embed.WithDescription(context.Localize("jumble.playedAllToday"));
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            var databaseAlbum = await this._albumService.GetAlbumFromDatabase(album.ArtistName, album.AlbumName);
            if (databaseAlbum == null)
            {
                // Pick someone else and hope for the best
                album = GameService.PickAlbumForPixelation(topAlbums, albumPopularities);
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

            var hints = GameService.GetJumbleAlbumHints(databaseAlbum, databaseArtist,
                album.UserPlaycount.GetValueOrDefault(), context.Localizer, artistCountry);
            await this._gameService.JumbleStoreShowedHints(game, hints);

            BuildJumbleEmbed(response.Embed, game.JumbledArtist, game.Hints, context.Localizer,
                jumbleType: JumbleType.Pixelation);

            var image = await this._gameService.GetSkImage(album.AlbumCoverUrl, album.AlbumName, album.ArtistName,
                game.JumbleSessionId);
            if (image == null)
            {
                response.ResponseType = ResponseType.Embed;
                response.Embed.WithDescription(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                response.ResponseType = ResponseType.Embed;
                await this._gameService.JumbleEndSession(game);
                return response;
            }

            using var pixelated = GameService.PixelateCoverImage(image, game.BlurLevel.GetValueOrDefault());

            var encoded = pixelated.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"pixelation-{game.JumbleSessionId}-{game.BlurLevel.GetValueOrDefault()}.png";

            response.Components =
                BuildJumbleComponents(game.JumbleSessionId, game.Hints, context.Localizer, game.BlurLevel,
                    game.JumbledArtist == null);
            response.GameSessionId = game.JumbleSessionId;

            return response;
        }
        finally
        {
            GameService.ClearGameStartClaim(context.DiscordChannel.Id);
        }
    }

    private static void BuildJumbleEmbed(EmbedProperties embed, string jumbledArtist, List<JumbleSessionHint> hints,
        Localizer localizer, bool canBeAnswered = true, JumbleType jumbleType = JumbleType.Artist)
    {
        var hintsShown = hints.Count(w => w.HintShown);
        var hintString = GameService.HintsToString(hints, hintsShown);

        embed.WithColor(DiscordConstants.InformationColorBlue);

        var isSingle = hints.Count != 0 &&
                       hints.Any(a =>
                           a.Type == JumbleHintType.Type &&
                           a.Content.Contains("single", StringComparison.OrdinalIgnoreCase));

        var hintTitle = jumbleType == JumbleType.Artist
            ? localizer.Translate("jumble.titleGuessArtist")
            : isSingle
                ? localizer.Translate("jumble.titleGuessSingle")
                : localizer.Translate("jumble.titleGuessAlbum");

        if (jumbledArtist != null)
        {
            embed.WithDescription($"### `{jumbledArtist}`");
        }

        if (hintsShown > 3)
        {
            hintTitle += $" {localizer.TranslateCount("jumble.extraHints", hintsShown - 3)}";
        }

        embed.AddField(hintTitle, hintString);

        if (canBeAnswered)
        {
            embed.AddField(localizer.Translate("jumble.addAnswerTitle"),
                localizer.Translate("jumble.addAnswerDescription",
                    ("seconds", (jumbleType == JumbleType.Artist
                        ? GameService.JumbleSecondsToGuess
                        : GameService.PixelationSecondsToGuess).ToString())));
        }
    }

    public async Task<ResponseModel> GetJumbleUserStats(ContextModel context, UserSettingsModel userSettings,
        JumbleType jumbleType, TimeSettingsModel timeSettings = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var name = jumbleType == JumbleType.Artist ? "Jumble" : "Pixel Jumble";
        var pages = new List<PageBuilder>();

        var userPage = new PageBuilder();
        userPage.WithColor(DiscordConstants.InformationColorBlue);
        userPage.WithAuthor(context.Localize("jumble.userStatsTitle", ("game", name),
            ("user", $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}")));

        var userStats =
            await this._gameService.GetJumbleUserStats(userSettings.UserId, userSettings.DiscordUserId, jumbleType,
                timeSettings?.StartDateTime, timeSettings?.EndDateTime);

        if (userStats == null)
        {
            userPage.WithDescription(userSettings.DifferentUser
                ? context.Localize("jumble.noStatsUser")
                : context.Localize("jumble.noStatsSelf"));
        }
        else
        {
            var gameStats = new StringBuilder();
            gameStats.AppendLine(context.LocalizeCount("jumble.statTotalGamesPlayed", userStats.TotalGamesPlayed));
            gameStats.AppendLine(context.LocalizeCount("jumble.statGamesStarted", userStats.GamesStarted));
            gameStats.AppendLine(context.LocalizeCount("jumble.statGamesAnswered", userStats.GamesAnswered));
            gameStats.AppendLine(context.LocalizeCount("jumble.statGamesWon", userStats.GamesWon));
            gameStats.AppendLine(context.Localize("jumble.statAvgHintsShown",
                ("avg", decimal.Round(userStats.AvgHintsShown, 1).ToString())));
            userPage.AddField(context.Localize("jumble.fieldGames"), gameStats.ToString());

            var answerStats = new StringBuilder();
            answerStats.AppendLine(context.LocalizeCount("jumble.statTotalAnswers", userStats.TotalAnswers));
            answerStats.AppendLine(context.Localize("jumble.statAvgAnswerTime",
                ("seconds", decimal.Round(userStats.AvgAnsweringTime, 1).ToString())));
            answerStats.AppendLine(context.Localize("jumble.statAvgCorrectAnswerTime",
                ("seconds", decimal.Round(userStats.AvgCorrectAnsweringTime, 1).ToString())));
            answerStats.AppendLine(context.Localize("jumble.statAvgAttempts",
                ("avg", decimal.Round(userStats.AvgAttemptsUntilCorrect, 1).ToString())));
            answerStats.AppendLine(context.Localize("jumble.statWinRate",
                ("percentage", decimal.Round(userStats.WinRate, 1).ToString())));
            userPage.AddField(context.Localize("jumble.fieldAnswers"), answerStats.ToString());
        }

        if (timeSettings == null)
        {
            userPage.WithFooter(context.Localize("jumble.serverStatsFooter"));
        }

        pages.Add(userPage);
        response.Embed = userPage.GetEmbedProperties();

        var guildPage = new PageBuilder();
        guildPage.WithAuthor(context.Localize("jumble.serverStatsTitle", ("game", name),
            ("server", context.DiscordGuild.Name)));
        guildPage.WithColor(DiscordConstants.InformationColorBlue);

        var guildStats =
            await this._gameService.GetJumbleGuildStats(context.DiscordGuild.Id, jumbleType);

        if (guildStats == null)
        {
            guildPage.WithDescription(context.Localize("jumble.noStatsServer"));
        }
        else
        {
            var gameStats = new StringBuilder();
            gameStats.AppendLine(context.LocalizeCount("jumble.statTotalGamesPlayed", guildStats.TotalGamesPlayed));
            gameStats.AppendLine(context.LocalizeCount("jumble.statGamesSolved", guildStats.GamesSolved));
            gameStats.AppendLine(context.LocalizeCount("jumble.statTotalReshuffles", guildStats.TotalReshuffles));
            gameStats.AppendLine(context.Localize("jumble.statAvgHintsShown",
                ("avg", decimal.Round(guildStats.AvgHintsShown, 1).ToString())));
            guildPage.AddField(context.Localize("jumble.fieldGames"), gameStats.ToString());

            var answerStats = new StringBuilder();
            answerStats.AppendLine(context.LocalizeCount("jumble.statTotalAnswers", guildStats.TotalAnswers));
            answerStats.AppendLine(context.Localize("jumble.statAvgAnswerTime",
                ("seconds", decimal.Round(guildStats.AvgAnsweringTime, 1).ToString())));
            answerStats.AppendLine(context.Localize("jumble.statAvgCorrectAnswerTime",
                ("seconds", decimal.Round(guildStats.AvgCorrectAnsweringTime, 1).ToString())));
            answerStats.AppendLine(context.Localize("jumble.statAvgAttempts",
                ("avg", decimal.Round(guildStats.AvgAttemptsUntilCorrect, 1).ToString())));
            guildPage.AddField(context.Localize("jumble.fieldAnswers"), answerStats.ToString());

            var channels = new StringBuilder();
            var counter = 1;
            foreach (var channel in guildStats.Channels.Take(5))
            {
                channels.AppendLine(
                    $"{counter}. <#{channel.Id}> - {context.LocalizeCount("shared.games", channel.Count)}");
                counter++;
            }

            guildPage.AddField(context.Localize("jumble.fieldTopChannels"), channels.ToString());
        }

        guildPage.WithFooter(context.Localize("jumble.userStatsFooter", ("user", userSettings.DisplayName)));

        pages.Add(guildPage);

        response.ComponentPaginator = StringService.BuildSimpleComponentPaginator(pages);

        return response;
    }

    private static ActionRowProperties BuildJumbleComponents(int gameId, List<JumbleSessionHint> hints,
        Localizer localizer,
        float? blur = null,
        bool shuffledHidden = false)
    {
        var addHintDisabled = hints.Count(c => c.HintShown) == hints.Count;
        var offerUnblur = blur is > 0.01f;

        return new ActionRowProperties()
            .WithButton(addHintDisabled && offerUnblur
                    ? localizer.Translate("jumble.buttonUnblur")
                    : localizer.Translate("jumble.buttonAddHint"),
                addHintDisabled && offerUnblur
                    ? $"{InteractionConstants.Game.JumbleUnblur}:{gameId}"
                    : $"{InteractionConstants.Game.AddJumbleHint}:{gameId}",
                ButtonStyle.Secondary,
                disabled: addHintDisabled && !offerUnblur)
            .WithButton(shuffledHidden
                    ? localizer.Translate("jumble.buttonJumbledName")
                    : localizer.Translate("jumble.buttonReshuffle"),
                $"{InteractionConstants.Game.JumbleReshuffle}:{gameId}", ButtonStyle.Secondary)
            .WithButton(localizer.Translate("jumble.buttonGiveUp"),
                $"{InteractionConstants.Game.JumbleGiveUp}:{gameId}", ButtonStyle.Secondary);
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
                response.Embed.WithDescription(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            var blurLevel = GetNextBlurLevel(currentGame.BlurLevel.Value);

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            using var pixelated = GameService.PixelateCoverImage(image, blurLevel);

            var encoded = pixelated.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"pixelation-{currentGame.JumbleSessionId}-{blurLevel}.png";
        }

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, true,
            currentGame.JumbleType);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
            currentGame.BlurLevel, currentGame.JumbledArtist == null);

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
                response.Embed.WithDescription(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            var blurLevel = GetNextBlurLevel(currentGame.BlurLevel.Value);

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            using var pixelated = GameService.PixelateCoverImage(image, blurLevel);

            var encoded = pixelated.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"pixelation-{currentGame.JumbleSessionId}-{blurLevel}.png";
        }

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, context.Localizer,
            jumbleType: currentGame.JumbleType);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
            currentGame.BlurLevel, currentGame.JumbledArtist == null);

        return response;
    }

    private static float GetNextBlurLevel(float currentBlurLevel)
    {
        return currentBlurLevel switch
        {
            0.125f => 0.085f,
            0.085f => 0.05f,
            0.05f => 0.03f,
            0.03f => 0.02f,
            0.02f => 0.015f,
            0.015f => 0.01f,
            _ => currentBlurLevel
        };
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

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, true,
            currentGame.JumbleType);
        response.Components = BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
            currentGame.BlurLevel, currentGame.JumbledArtist == null);

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                using var pixelated = GameService.PixelateCoverImage(image, currentGame.BlurLevel.Value);

                var encoded = pixelated.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream(true);
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
            response.Embed.WithDescription(context.Localize("jumble.giveUpNotYours"));
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoPermission;
            return response;
        }

        var dayStreakTask = this._gameService.GetConsecutiveDaysStreak(context.DiscordUser.Id, currentGame.JumbleType);

        await this._gameService.JumbleEndSession(currentGame);
        await this._gameService.CancelToken(context.DiscordChannel.Id);

        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, false,
            currentGame.JumbleType);

        var userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

        response.Embed.AddField(context.Localize("jumble.gaveUpTitle", ("user", userTitle)),
            currentGame.JumbleType == JumbleType.Artist
                ? context.Localize("jumble.itWasArtist", ("answer", currentGame.CorrectAnswer))
                : context.Localize("jumble.itWasAlbum", ("answer", currentGame.CorrectAnswer),
                    ("artist", currentGame.ArtistName)));
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        var playAgainButton = new ActionRowProperties().WithButton(context.Localize("jumble.buttonPlayAgain"),
            $"{InteractionConstants.Game.JumblePlayAgain}:{currentGame.JumbleType}",
            ButtonStyle.Secondary);

        if (currentGame.Answers is { Count: >= 1 })
        {
            var dayCount = await dayStreakTask;
            var separateResponse = new EmbedProperties();
            separateResponse.WithDescription(currentGame.JumbleType == JumbleType.Artist
                ? context.Localize("jumble.gaveUpDescriptionArtist", ("user", userTitle),
                    ("answer", currentGame.CorrectAnswer))
                : context.Localize("jumble.gaveUpDescriptionAlbum", ("user", userTitle),
                    ("answer", currentGame.CorrectAnswer), ("artist", currentGame.ArtistName)));
            separateResponse.WithColor(DiscordConstants.AppleMusicRed);
            if (dayCount > 1)
            {
                var footer = new StringBuilder();
                if (dayCount >= 10)
                {
                    footer.Append($"🔥");
                }

                footer.Append(context.LocalizeCount("jumble.dayStreak", dayCount));
                separateResponse.WithFooter(footer.ToString());
            }

            if (context.DiscordChannel is TextGuildChannel msgChannel)
            {
                _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse, playAgainButton,
                    new ReferencedMusic
                    {
                        Artist = currentGame.ArtistName,
                        Album = currentGame.AlbumName
                    }));
            }
        }
        else
        {
            response.Components = playAgainButton;
        }

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream(true);
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

    private static async Task SendSeparateResponse(TextGuildChannel msgChannel, EmbedProperties separateResponse,
        ActionRowProperties components, ReferencedMusic referencedMusic)
    {
        var msg = await msgChannel.SendMessageAsync(new MessageProperties
        {
            Embeds = [separateResponse],
            Components = [components]
        });

        PublicProperties.UsedCommandsReferencedMusic.TryAdd(msg.Id, referencedMusic);
    }

    public async Task JumbleProcessAnswer(ContextModel context, CommandContext commandContext)
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

            var uncleanedAnswer = currentGame.JumbleType == JumbleType.Artist
                ? currentGame.ArtistName
                : currentGame.AlbumName;

            if (messageLength >= answerLength / 2 &&
                messageLength <= Math.Max(Math.Min(answerLength + answerLength / 2, answerLength + 15),
                    uncleanedAnswer.Length + 2))
            {
                var answerIsRight =
                    GameService.AnswerIsRight(currentGame, commandContext.Message.Content);

                if (answerIsRight)
                {
                    var dayStreakTask =
                        this._gameService.GetConsecutiveDaysStreak(context.DiscordUser.Id, currentGame.JumbleType);

                    _ = Task.Run(() => commandContext.Message.AddReactionAsync(new ReactionEmojiProperties("✅")));

                    _ = Task.Run(() => this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, true));

                    _ = Task.Run(() => this._gameService.JumbleEndSession(currentGame));

                    var userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

                    var separateResponse = new EmbedProperties();
                    separateResponse.WithDescription(
                        currentGame.JumbleType == JumbleType.Artist
                            ? context.Localize("jumble.gotItArtist", ("user", userTitle),
                                ("answer", currentGame.CorrectAnswer))
                            : context.Localize("jumble.gotItAlbum", ("user", userTitle),
                                ("answer", currentGame.CorrectAnswer), ("artist", currentGame.ArtistName)));
                    var timeTaken = DateTime.UtcNow - currentGame.DateStarted;

                    var footer = new StringBuilder();
                    footer.Append(context.Localize("jumble.answeredIn",
                        ("seconds", timeTaken.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))));

                    var dayCount = await dayStreakTask;
                    if (dayCount > 1)
                    {
                        footer.Append($" — ");
                        if (dayCount >= 10)
                        {
                            footer.Append($"🔥");
                        }

                        footer.Append(context.LocalizeCount("jumble.dayStreak", dayCount));
                    }

                    separateResponse.WithFooter(footer.ToString());
                    separateResponse.WithColor(DiscordConstants.SpotifyColorGreen);
                    var playAgainComponent = new ActionRowProperties().WithButton(
                        context.Localize("jumble.buttonPlayAgain"),
                        $"{InteractionConstants.Game.JumblePlayAgain}:{currentGame.JumbleType}",
                        ButtonStyle.Secondary);
                    if (context.DiscordChannel is TextGuildChannel msgChannel)
                    {
                        _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse, playAgainComponent,
                            new ReferencedMusic
                            {
                                Artist = currentGame.ArtistName,
                                Album = currentGame.AlbumName
                            }));
                    }

                    if (currentGame.DiscordResponseId.HasValue)
                    {
                        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints,
                            context.Localizer, false, currentGame.JumbleType);
                        response.Components = null;
                        response.Embed.WithColor(DiscordConstants.SpotifyColorGreen);

                        var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
                        if (image != null)
                        {
                            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                            response.Stream = encoded.AsStream(true);
                            response.FileName = $"pixelation-{currentGame.JumbleSessionId}.png";
                        }

                        var msg = await commandContext.Channel.GetMessageAsync(currentGame.DiscordResponseId.Value);
                        if (msg is not RestMessage message)
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
                            m.Components = [];
                            m.Embeds = [response.Embed];
                            m.Attachments = response.Stream != null
                                ? [new AttachmentProperties(response.Spoiler
                                    ? $"SPOILER_{response.FileName}"
                                    : response.FileName, response.Stream)]
                                : null;
                        });
                    }
                }
                else
                {
                    var levenshteinDistance =
                        GameService.GetLevenshteinDistance(currentGame.CorrectAnswer.ToLower(),
                            commandContext.Message.Content.ToLower());

                    if (levenshteinDistance == 1 ||
                        levenshteinDistance == 2 && commandContext.Message.Content.Length > 4)
                    {
                        await commandContext.Message.AddReactionAsync(new ReactionEmojiProperties("🤏"));
                    }
                    else
                    {
                        await commandContext.Message.AddReactionAsync(new ReactionEmojiProperties("❌"));
                    }

                    await this._gameService.JumbleAddAnswer(currentGame, commandContext.User.Id, false);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error in JumbleProcessAnswer: {exception}", e.Message);
            if (e.Message.Contains("Missing Permissions", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("Missing Access", StringComparison.OrdinalIgnoreCase))
            {
                await commandContext.Client.Rest.SendMessageAsync(commandContext.Message.ChannelId, new MessageProperties
                {
                    Content = context.Localize("jumble.reactionPermissionError")
                });
            }
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
        BuildJumbleEmbed(response.Embed, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, false,
            currentGame.JumbleType);

        response.Embed.AddField(context.Localize("jumble.timeUpTitle"),
            currentGame.JumbleType == JumbleType.Artist
                ? context.Localize("jumble.itWasArtist", ("answer", currentGame.CorrectAnswer))
                : context.Localize("jumble.itWasAlbum", ("answer", currentGame.CorrectAnswer),
                    ("artist", currentGame.ArtistName)));
        response.Components = null;
        response.Embed.WithColor(DiscordConstants.AppleMusicRed);

        var playAgainComponent = new ActionRowProperties().WithButton(context.Localize("jumble.buttonPlayAgain"),
            $"{InteractionConstants.Game.JumblePlayAgain}:{currentGame.JumbleType}",
            ButtonStyle.Secondary);

        if (currentGame.Answers is { Count: >= 1 })
        {
            var separateResponse = new EmbedProperties();
            separateResponse.WithDescription(currentGame.JumbleType == JumbleType.Artist
                ? context.Localize("jumble.nobodyGuessedArtist", ("answer", currentGame.CorrectAnswer))
                : context.Localize("jumble.nobodyGuessedAlbum", ("answer", currentGame.CorrectAnswer),
                    ("artist", currentGame.ArtistName)));
            separateResponse.WithColor(DiscordConstants.AppleMusicRed);
            if (context.DiscordChannel is TextGuildChannel msgChannel)
            {
                _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse, playAgainComponent,
                    new ReferencedMusic
                    {
                        Artist = currentGame.ArtistName,
                        Album = currentGame.AlbumName
                    }));
            }
        }
        else
        {
            response.Components = playAgainComponent;
        }

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                response.Stream = encoded.AsStream(true);
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
