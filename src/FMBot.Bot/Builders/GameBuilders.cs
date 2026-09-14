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
            ResponseType = ResponseType.ComponentsV2,
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
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.gameInProgress"));
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (!GameService.TryClaimGameStart(context.DiscordChannel.Id))
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.gameInProgress"));
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        try
        {
            var recentJumblesTask = this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Artist);
            var topArtistsTask = this._artistsService.GetUserAllTimeTopArtists(userId, true).ObserveFaults();

            var recentJumbles = await recentJumblesTask;
            var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
            var premiumGuild = context.DiscordGuild != null &&
                               PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
            var jumbleLimit = premiumGuild ? Constants.PremiumServerJumbleDailyLimit : Constants.JumbleDailyLimit;
            if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
            {
                BuildDailyLimitContainer(response, context, premiumGuild,
                    context.Localize("jumble.dailyLimitReached", ("limit", jumbleLimit.ToString())),
                    "jumble-dailylimit");
                response.CommandResponse = CommandResponse.SupporterRequired;
                return response;
            }

            var topArtists = await topArtistsTask;
            var artistPopularities = await this._artistsService.GetArtistsPopularity(topArtists);
            var artist = GameService.PickArtistForJumble(topArtists, artistPopularities, recentJumbles);

            if (artist.artist == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.playedAllToday"));
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

            var hintContextTask = databaseArtist != null
                ? this._artistsService.GetArtistHintContext(databaseArtist.Id)
                : Task.FromResult<ArtistHintContext>(null);

            var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.Artist, artist.artist,
                cancellationTokenSource, artist.artist);

            CountryInfo artistCountry = null;
            if (databaseArtist?.CountryCode != null)
            {
                artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
            }

            var hints = GameService.GetJumbleArtistHints(databaseArtist, game.CorrectAnswer, artist.userPlaycount,
                context.Localizer, artistCountry, await hintContextTask);
            await this._gameService.JumbleStoreShowedHints(game, hints);

            BuildJumbleContainer(response, game.JumbledArtist, game.Hints, context.Localizer);
            response.ComponentsContainer.WithActionRow(
                BuildJumbleComponents(game.JumbleSessionId, game.Hints, context.Localizer,
                    shuffledHidden: game.JumbledArtist == null));
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
            ResponseType = ResponseType.ComponentsV2
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
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.gameInProgress"));
                response.CommandResponse = CommandResponse.Cooldown;
                return response;
            }
        }

        if (!GameService.TryClaimGameStart(context.DiscordChannel.Id))
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.gameInProgress"));
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        try
        {
            var recentJumblesTask = this._gameService.GetRecentJumbles(context.ContextUser.UserId, JumbleType.Pixelation);
            var topAlbumsTask = this._albumService.GetUserAllTimeTopAlbums(userId, true).ObserveFaults();

            var recentJumbles = await recentJumblesTask;
            var jumblesPlayedToday = recentJumbles.Count(c => c.DateStarted.Date == DateTime.Today);
            var premiumGuild = context.DiscordGuild != null &&
                               PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
            var jumbleLimit = premiumGuild ? Constants.PremiumServerJumbleDailyLimit : Constants.JumbleDailyLimit;
            if (!SupporterService.IsSupporter(context.ContextUser.UserType) && jumblesPlayedToday > jumbleLimit)
            {
                BuildDailyLimitContainer(response, context, premiumGuild,
                    context.Localize("jumble.dailyLimitReachedPixel", ("limit", jumbleLimit.ToString())),
                    "pixel-dailylimit");
                response.CommandResponse = CommandResponse.SupporterRequired;
                return response;
            }

            var topAlbums = await topAlbumsTask;

            await this._albumService.FillMissingAlbumCovers(topAlbums);
            topAlbums = await this._censorService.RemoveNsfwAlbums(topAlbums);
            var albumPopularities = await this._albumService.GetUserAllTimeTopAlbumsPopularity(userId, topAlbums);
            var album = GameService.PickAlbumForPixelation(topAlbums, albumPopularities, recentJumbles);

            if (album == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.playedAllToday"));
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

            var coverImageTask = this._gameService.FetchCoverImage(album.AlbumCoverUrl, album.AlbumName, album.ArtistName);
            var databaseArtistTask = this._artistsService.GetArtistFromDatabase(album.ArtistName);

            var game = await this._gameService.StartJumbleGame(userId, context, JumbleType.Pixelation, album.AlbumName,
                cancellationTokenSource, album.ArtistName, album.AlbumName);

            var databaseArtist = await databaseArtistTask;
            CountryInfo artistCountry = null;
            if (databaseArtist?.CountryCode != null)
            {
                artistCountry = this._countryService.GetValidCountry(databaseArtist.CountryCode);
            }

            var hintContext = databaseArtist != null
                ? await this._artistsService.GetArtistHintContext(databaseArtist.Id)
                : null;

            var hints = GameService.GetJumbleAlbumHints(databaseAlbum, databaseArtist, game.CorrectAnswer,
                album.UserPlaycount.GetValueOrDefault(), context.Localizer, artistCountry, hintContext);
            await this._gameService.JumbleStoreShowedHints(game, hints);

            var image = await coverImageTask;
            if (image == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                await this._gameService.JumbleEndSession(game);
                return response;
            }

            this._gameService.CacheSessionImage(game.JumbleSessionId, image);
            AttachCoverImage(response, game.JumbleSessionId, image, game.BlurLevel.GetValueOrDefault());

            BuildJumbleContainer(response, game.JumbledArtist, game.Hints, context.Localizer,
                jumbleType: JumbleType.Pixelation);
            response.ComponentsContainer.WithActionRow(
                BuildJumbleComponents(game.JumbleSessionId, game.Hints, context.Localizer, game.BlurLevel,
                    game.JumbledArtist == null));
            response.GameSessionId = game.JumbleSessionId;

            return response;
        }
        finally
        {
            GameService.ClearGameStartClaim(context.DiscordChannel.Id);
        }
    }

    private static void BuildDailyLimitContainer(ResponseModel response, ContextModel context, bool premiumGuild,
        string limitReachedText, string source)
    {
        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        var limitDescription = new StringBuilder();
        limitDescription.AppendLine(limitReachedText);

        var limitButtons = new ActionRowProperties()
            .WithButton(context.Localize("buttons.getFmbotSupporter"), style: ButtonStyle.Primary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: source));

        if (!premiumGuild)
        {
            limitDescription.AppendLine(context.Localize("jumble.premiumServerUpsell",
                ("premiumLimit", Constants.PremiumServerJumbleDailyLimit.Format(context.NumberFormat))));
            limitButtons.WithButton(context.Localize("buttons.premiumServer"), style: ButtonStyle.Secondary,
                customId: $"{InteractionConstants.PremiumServer.GetOverview}:{source}");
        }

        response.ComponentsContainer.WithTextDisplay(limitDescription.ToString().TrimEnd());
        response.ComponentsContainer.WithActionRow(limitButtons);
    }

    private static void BuildJumbleContainer(ResponseModel response, string jumbledArtist, List<JumbleSessionHint> hints,
        Localizer localizer, bool canBeAnswered = true, JumbleType jumbleType = JumbleType.Artist)
    {
        var hintsShown = hints.Count(w => w.HintShown);
        var hintString = GameService.HintsToString(hints, hintsShown);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        var isSingle = hints.Count != 0 &&
                       hints.Any(a =>
                           a.Type == JumbleHintType.Type &&
                           a.Content.Contains("single", StringComparison.OrdinalIgnoreCase));

        var hintTitle = jumbleType == JumbleType.Artist
            ? localizer.Translate("jumble.titleGuessArtist")
            : isSingle
                ? localizer.Translate("jumble.titleGuessSingle")
                : localizer.Translate("jumble.titleGuessAlbum");

        if (response.Stream != null)
        {
            container.AddComponent(new MediaGalleryProperties
            {
                new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            });
        }

        if (jumbledArtist != null)
        {
            container.WithTextDisplay($"### `{jumbledArtist}`");
            container.WithSeparator();
        }

        if (hintsShown > 3)
        {
            hintTitle += $" {localizer.TranslateCount("jumble.extraHints", hintsShown - 3)}";
        }

        container.WithTextDisplay($"**{hintTitle}**\n{hintString.TrimEnd()}");

        if (canBeAnswered)
        {
            container.WithSeparator();
            container.WithTextDisplay($"**{localizer.Translate("jumble.addAnswerTitle")}**\n" +
                                      localizer.Translate("jumble.addAnswerDescription",
                                          ("seconds", (jumbleType == JumbleType.Artist
                                              ? GameService.JumbleSecondsToGuess
                                              : GameService.PixelationSecondsToGuess).ToString())));
        }

        response.ResponseType = ResponseType.ComponentsV2;
    }

    private static void AttachCoverImage(ResponseModel response, int sessionId, SKBitmap image, float? blurLevel = null)
    {
        if (blurLevel.HasValue)
        {
            using var pixelated = GameService.PixelateCoverImage(image, blurLevel.Value);
            var encoded = pixelated.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"pixelation-{sessionId}-{(int)Math.Round(blurLevel.Value * 1000)}.png";
        }
        else
        {
            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"pixelation-{sessionId}.png";
        }
    }

    public async Task<ResponseModel> GetJumbleUserStats(ContextModel context, UserSettingsModel userSettings,
        JumbleType jumbleType, TimeSettingsModel timeSettings = null, JumbleStatsView view = JumbleStatsView.User)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var name = jumbleType == JumbleType.Artist ? "Jumble" : "Pixel Jumble";

        var page = view == JumbleStatsView.Server && context.DiscordGuild != null
            ? await BuildGuildStatsPage(context, jumbleType, name)
            : await BuildUserStatsPage(context, userSettings, jumbleType, timeSettings, name);

        response.Embed = BuildStatsEmbed(page);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);
        container.WithTextDisplay($"### {page.Title}");
        container.WithSeparator();

        if (page.Description != null)
        {
            container.WithTextDisplay(page.Description);
        }

        foreach (var (title, body) in page.Sections)
        {
            container.WithTextDisplay($"**{title}**\n{body.TrimEnd()}");
        }

        if (context.DiscordGuild != null)
        {
            container.WithSeparator();
            container.WithActionRow(BuildStatsTabs(context, userSettings, jumbleType, view));
        }

        return response;
    }

    private async Task<JumbleStatsPage> BuildUserStatsPage(ContextModel context, UserSettingsModel userSettings,
        JumbleType jumbleType, TimeSettingsModel timeSettings, string name)
    {
        var page = new JumbleStatsPage
        {
            Title = context.Localize("jumble.userStatsTitle", ("game", name),
                ("user", $"{userSettings.DisplayName}{userSettings.UserType.UserTypeToIcon()}"))
        };

        var userStats =
            await this._gameService.GetJumbleUserStats(userSettings.UserId, userSettings.DiscordUserId, jumbleType,
                timeSettings?.StartDateTime, timeSettings?.EndDateTime);

        if (userStats == null)
        {
            page.Description = userSettings.DifferentUser
                ? context.Localize("jumble.noStatsUser")
                : context.Localize("jumble.noStatsSelf");
            return page;
        }

        var gameStats = new StringBuilder();
        gameStats.AppendLine(context.LocalizeCount("jumble.statTotalGamesPlayed", userStats.TotalGamesPlayed));
        gameStats.AppendLine(context.LocalizeCount("jumble.statGamesStarted", userStats.GamesStarted));
        gameStats.AppendLine(context.LocalizeCount("jumble.statGamesAnswered", userStats.GamesAnswered));
        gameStats.AppendLine(context.LocalizeCount("jumble.statGamesWon", userStats.GamesWon));
        gameStats.AppendLine(context.Localize("jumble.statAvgHintsShown",
            ("avg", decimal.Round(userStats.AvgHintsShown, 1).ToString())));
        page.Sections.Add((context.Localize("jumble.fieldGames"), gameStats.ToString()));

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
        page.Sections.Add((context.Localize("jumble.fieldAnswers"), answerStats.ToString()));

        return page;
    }

    private async Task<JumbleStatsPage> BuildGuildStatsPage(ContextModel context, JumbleType jumbleType, string name)
    {
        var page = new JumbleStatsPage
        {
            Title = context.Localize("jumble.serverStatsTitle", ("game", name),
                ("server", context.DiscordGuild.Name))
        };

        var guildStats =
            await this._gameService.GetJumbleGuildStats(context.DiscordGuild.Id, jumbleType);

        if (guildStats == null)
        {
            page.Description = context.Localize("jumble.noStatsServer");
            return page;
        }

        var gameStats = new StringBuilder();
        gameStats.AppendLine(context.LocalizeCount("jumble.statTotalGamesPlayed", guildStats.TotalGamesPlayed));
        gameStats.AppendLine(context.LocalizeCount("jumble.statGamesSolved", guildStats.GamesSolved));
        gameStats.AppendLine(context.LocalizeCount("jumble.statTotalReshuffles", guildStats.TotalReshuffles));
        gameStats.AppendLine(context.Localize("jumble.statAvgHintsShown",
            ("avg", decimal.Round(guildStats.AvgHintsShown, 1).ToString())));
        page.Sections.Add((context.Localize("jumble.fieldGames"), gameStats.ToString()));

        var answerStats = new StringBuilder();
        answerStats.AppendLine(context.LocalizeCount("jumble.statTotalAnswers", guildStats.TotalAnswers));
        answerStats.AppendLine(context.Localize("jumble.statAvgAnswerTime",
            ("seconds", decimal.Round(guildStats.AvgAnsweringTime, 1).ToString())));
        answerStats.AppendLine(context.Localize("jumble.statAvgCorrectAnswerTime",
            ("seconds", decimal.Round(guildStats.AvgCorrectAnsweringTime, 1).ToString())));
        answerStats.AppendLine(context.Localize("jumble.statAvgAttempts",
            ("avg", decimal.Round(guildStats.AvgAttemptsUntilCorrect, 1).ToString())));
        page.Sections.Add((context.Localize("jumble.fieldAnswers"), answerStats.ToString()));

        var channels = new StringBuilder();
        var counter = 1;
        foreach (var channel in guildStats.Channels.Take(5))
        {
            channels.AppendLine(
                $"{counter}. <#{channel.Id}> - {context.LocalizeCount("shared.games", channel.Count)}");
            counter++;
        }

        page.Sections.Add((context.Localize("jumble.fieldTopChannels"), channels.ToString()));

        return page;
    }

    private static ActionRowProperties BuildStatsTabs(ContextModel context, UserSettingsModel userSettings,
        JumbleType jumbleType, JumbleStatsView view)
    {
        return new ActionRowProperties()
            .WithButton(context.Localize("jumble.tabUserStats", ("user", userSettings.DisplayName)),
                StatsTabId(context, userSettings, jumbleType, JumbleStatsView.User),
                view == JumbleStatsView.User ? ButtonStyle.Primary : ButtonStyle.Secondary,
                disabled: view == JumbleStatsView.User)
            .WithButton(context.Localize("jumble.tabServerStats"),
                StatsTabId(context, userSettings, jumbleType, JumbleStatsView.Server),
                view == JumbleStatsView.Server ? ButtonStyle.Primary : ButtonStyle.Secondary,
                disabled: view == JumbleStatsView.Server);
    }

    private static string StatsTabId(ContextModel context, UserSettingsModel userSettings, JumbleType jumbleType,
        JumbleStatsView view)
    {
        return $"{InteractionConstants.Game.JumbleStats}:{jumbleType}:{view}:{userSettings.DiscordUserId}:{context.ContextUser.DiscordUserId}";
    }

    private static EmbedProperties BuildStatsEmbed(JumbleStatsPage page)
    {
        var embed = new EmbedProperties();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithAuthor(page.Title);

        if (page.Description != null)
        {
            embed.WithDescription(page.Description);
        }

        foreach (var (title, body) in page.Sections)
        {
            embed.AddField(title, body);
        }

        return embed;
    }

    private sealed class JumbleStatsPage
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<(string Title, string Body)> Sections { get; } = [];
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

    public static ActionRowProperties BuildPlayAgainRow(Localizer localizer, JumbleType jumbleType)
    {
        return new ActionRowProperties()
            .WithButton(localizer.Translate("jumble.buttonPlayAgain"),
                $"{InteractionConstants.Game.JumblePlayAgain}:{jumbleType}", ButtonStyle.Primary)
            .WithButton(localizer.Translate("jumble.buttonStats"),
                $"{InteractionConstants.Game.JumbleShowStats}:{jumbleType}", ButtonStyle.Secondary);
    }

    private static void AddResult(ComponentContainerProperties container, string resultText,
        ActionRowProperties playAgainRow = null)
    {
        container.WithSeparator();
        container.WithTextDisplay(resultText);

        if (playAgainRow != null)
        {
            container.WithActionRow(playAgainRow);
        }
    }

    public async Task<ResponseModel> JumbleAddHint(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        GameService.HintsToString(currentGame.Hints, currentGame.Hints.Count(w => w.HintShown) + 1);
        await this._gameService.JumbleStoreShowedHints(currentGame, currentGame.Hints);

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                return response;
            }

            var blurLevel = GetNextBlurLevel(currentGame.BlurLevel.Value);

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            AttachCoverImage(response, currentGame.JumbleSessionId, image, blurLevel);
        }

        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, true,
            currentGame.JumbleType);
        response.ComponentsContainer.WithActionRow(
            BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
                currentGame.BlurLevel, currentGame.JumbledArtist == null));

        return response;
    }

    public async Task<ResponseModel> JumbleUnblur(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image == null)
            {
                response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
                response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.albumCoverError"));
                response.CommandResponse = CommandResponse.Error;
                return response;
            }

            var blurLevel = GetNextBlurLevel(currentGame.BlurLevel.Value);

            await this._gameService.JumbleStoreBlurLevel(currentGame, blurLevel);
            AttachCoverImage(response, currentGame.JumbleSessionId, image, blurLevel);
        }

        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints, context.Localizer,
            jumbleType: currentGame.JumbleType);
        response.ComponentsContainer.WithActionRow(
            BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
                currentGame.BlurLevel, currentGame.JumbledArtist == null));

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
            ResponseType = ResponseType.ComponentsV2,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        await this._gameService.JumbleReshuffleArtist(currentGame);

        if (currentGame.JumbleType == JumbleType.Pixelation && currentGame.BlurLevel.HasValue)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                AttachCoverImage(response, currentGame.JumbleSessionId, image, currentGame.BlurLevel.Value);
            }
        }

        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, true,
            currentGame.JumbleType);
        response.ComponentsContainer.WithActionRow(
            BuildJumbleComponents(currentGame.JumbleSessionId, currentGame.Hints, context.Localizer,
                currentGame.BlurLevel, currentGame.JumbledArtist == null));

        return response;
    }

    public async Task<ResponseModel> JumbleGiveUp(ContextModel context, int parsedGameId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(parsedGameId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        if (currentGame.StarterUserId != context.ContextUser.UserId)
        {
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ComponentsContainer.WithTextDisplay(context.Localize("jumble.giveUpNotYours"));
            response.CommandResponse = CommandResponse.NoPermission;
            return response;
        }

        var dayStreakTask = this._gameService.GetConsecutiveDaysStreak(context.DiscordUser.Id, currentGame.JumbleType);

        await this._gameService.JumbleEndSession(currentGame);
        await this._gameService.CancelToken(context.DiscordChannel.Id);

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                AttachCoverImage(response, currentGame.JumbleSessionId, image);
            }
        }

        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, false,
            currentGame.JumbleType);

        var userTitle = await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser);

        var resultText = $"**{context.Localize("jumble.gaveUpTitle", ("user", userTitle))}**\n" +
                         (currentGame.JumbleType == JumbleType.Artist
                             ? context.Localize("jumble.itWasArtist", ("answer", currentGame.CorrectAnswer))
                             : context.Localize("jumble.itWasAlbum", ("answer", currentGame.CorrectAnswer),
                                 ("artist", currentGame.ArtistName)));
        response.ComponentsContainer.WithAccentColor(DiscordConstants.AppleMusicRed);

        var playAgainRow = BuildPlayAgainRow(context.Localizer, currentGame.JumbleType);

        if (currentGame.Answers is { Count: >= 1 })
        {
            AddResult(response.ComponentsContainer, resultText);

            var dayCount = await dayStreakTask;
            var footer = new StringBuilder();
            if (dayCount > 1)
            {
                if (dayCount >= 10)
                {
                    footer.Append($"🔥");
                }

                footer.Append(context.LocalizeCount("jumble.dayStreak", dayCount));
            }

            var separateResponse = BuildSeparateResponse(currentGame.JumbleType == JumbleType.Artist
                    ? context.Localize("jumble.gaveUpDescriptionArtist", ("user", userTitle),
                        ("answer", currentGame.CorrectAnswer))
                    : context.Localize("jumble.gaveUpDescriptionAlbum", ("user", userTitle),
                        ("answer", currentGame.CorrectAnswer), ("artist", currentGame.ArtistName)),
                footer.ToString(), DiscordConstants.AppleMusicRed, playAgainRow);

            if (context.DiscordChannel is TextGuildChannel msgChannel)
            {
                _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse,
                    new ReferencedMusic
                    {
                        Artist = currentGame.ArtistName,
                        Album = currentGame.AlbumName
                    }));
            }
        }
        else
        {
            AddResult(response.ComponentsContainer, resultText, playAgainRow);
        }

        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = currentGame.ArtistName,
            Album = currentGame.AlbumName
        };

        return response;
    }

    private static ComponentContainerProperties BuildSeparateResponse(string description, string footer,
        Color accentColor, ActionRowProperties playAgainRow)
    {
        var container = new ComponentContainerProperties();
        container.WithAccentColor(accentColor);

        var text = string.IsNullOrEmpty(footer)
            ? description
            : $"{description}\n-# {footer}";

        container.WithTextDisplay(text);
        container.WithActionRow(playAgainRow);

        return container;
    }

    private static async Task SendSeparateResponse(TextGuildChannel msgChannel, ComponentContainerProperties container,
        ReferencedMusic referencedMusic)
    {
        var msg = await msgChannel.SendMessageAsync(new MessageProperties
        {
            Components = [container],
            Flags = MessageFlags.IsComponentsV2,
            AllowedMentions = AllowedMentionsProperties.None
        });

        PublicProperties.UsedCommandsReferencedMusic.TryAdd(msg.Id, referencedMusic);
    }

    public async Task JumbleProcessAnswer(ContextModel context, CommandContext commandContext)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
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

                    var separateResponse = BuildSeparateResponse(currentGame.JumbleType == JumbleType.Artist
                            ? context.Localize("jumble.gotItArtist", ("user", userTitle),
                                ("answer", currentGame.CorrectAnswer))
                            : context.Localize("jumble.gotItAlbum", ("user", userTitle),
                                ("answer", currentGame.CorrectAnswer), ("artist", currentGame.ArtistName)),
                        footer.ToString(), DiscordConstants.SpotifyColorGreen,
                        BuildPlayAgainRow(context.Localizer, currentGame.JumbleType));

                    if (context.DiscordChannel is TextGuildChannel msgChannel)
                    {
                        _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse,
                            new ReferencedMusic
                            {
                                Artist = currentGame.ArtistName,
                                Album = currentGame.AlbumName
                            }));
                    }

                    if (currentGame.DiscordResponseId.HasValue)
                    {
                        var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
                        if (image != null)
                        {
                            AttachCoverImage(response, currentGame.JumbleSessionId, image);
                        }

                        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints,
                            context.Localizer, false, currentGame.JumbleType);
                        response.ComponentsContainer.WithAccentColor(DiscordConstants.SpotifyColorGreen);

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
                            m.AllowedMentions = AllowedMentionsProperties.None;
                            m.Flags = MessageFlags.IsComponentsV2;
                            m.Embeds = [];
                            m.Components = response.GetComponentsV2();
                            m.Attachments = response.Stream != null
                                ? [new AttachmentProperties(response.FileName, response.Stream)]
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
            ResponseType = ResponseType.ComponentsV2,
        };

        var currentGame = await this._gameService.GetJumbleSessionForSessionId(gameSessionId);
        if (currentGame == null || currentGame.DateEnded.HasValue)
        {
            return null;
        }

        await this._gameService.JumbleEndSession(currentGame);

        if (currentGame.JumbleType == JumbleType.Pixelation)
        {
            var image = await this._gameService.GetImageFromCache(currentGame.JumbleSessionId);
            if (image != null)
            {
                AttachCoverImage(response, currentGame.JumbleSessionId, image);
            }
        }

        BuildJumbleContainer(response, currentGame.JumbledArtist, currentGame.Hints, context.Localizer, false,
            currentGame.JumbleType);

        var resultText = $"**{context.Localize("jumble.timeUpTitle")}**\n" +
                         (currentGame.JumbleType == JumbleType.Artist
                             ? context.Localize("jumble.itWasArtist", ("answer", currentGame.CorrectAnswer))
                             : context.Localize("jumble.itWasAlbum", ("answer", currentGame.CorrectAnswer),
                                 ("artist", currentGame.ArtistName)));
        response.ComponentsContainer.WithAccentColor(DiscordConstants.AppleMusicRed);

        var playAgainRow = BuildPlayAgainRow(context.Localizer, currentGame.JumbleType);

        if (currentGame.Answers is { Count: >= 1 })
        {
            AddResult(response.ComponentsContainer, resultText);

            var separateResponse = BuildSeparateResponse(currentGame.JumbleType == JumbleType.Artist
                    ? context.Localize("jumble.nobodyGuessedArtist", ("answer", currentGame.CorrectAnswer))
                    : context.Localize("jumble.nobodyGuessedAlbum", ("answer", currentGame.CorrectAnswer),
                        ("artist", currentGame.ArtistName)),
                null, DiscordConstants.AppleMusicRed, playAgainRow);

            if (context.DiscordChannel is TextGuildChannel msgChannel)
            {
                _ = Task.Run(() => SendSeparateResponse(msgChannel, separateResponse,
                    new ReferencedMusic
                    {
                        Artist = currentGame.ArtistName,
                        Album = currentGame.AlbumName
                    }));
            }
        }
        else
        {
            AddResult(response.ComponentsContainer, resultText, playAgainRow);
        }

        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = currentGame.ArtistName,
            Album = currentGame.AlbumName
        };

        return response;
    }
}
