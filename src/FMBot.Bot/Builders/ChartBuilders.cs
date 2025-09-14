using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class ChartBuilders
{
    private readonly ChartService _chartService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly AlbumService _albumService;
    private readonly SupporterService _supporterService;
    private readonly ArtistsService _artistService;
    private readonly MusicDataFactory _musicDataFactory;

    public ChartBuilders(ChartService chartService,
        IDataSourceFactory dataSourceFactory,
        AlbumService albumService,
        SupporterService supporterService,
        ArtistsService artistService,
        MusicDataFactory musicDataFactory)
    {
        this._chartService = chartService;
        this._dataSourceFactory = dataSourceFactory;
        this._albumService = albumService;
        this._supporterService = supporterService;
        this._artistService = artistService;
        this._musicDataFactory = musicDataFactory;
    }

    public async Task<ResponseModel> AlbumChartAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        ChartSettings chartSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed,
        };

        if (chartSettings.ImagesNeeded > 100)
        {
            response.Embed.Description = $"You can't create a chart with more than 100 images (10x10).\n" +
                                         $"Please try a smaller size.";
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var extraAlbums = 0;
        if (chartSettings.SkipWithoutImage)
        {
            extraAlbums = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
        }

        if (chartSettings.SkipNsfw)
        {
            extraAlbums = chartSettings.Height;
        }

        Response<TopAlbumList> albums = null;

        if (chartSettings.FilteredArtist != null && chartSettings.TimeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var artistTopAlbums = await this._artistService.GetTopAlbumsForArtist(userSettings.UserId,
                chartSettings.FilteredArtist.Name);
            if (artistTopAlbums.TopAlbums.Count != 0)
            {
                albums = new Response<TopAlbumList>
                {
                    Content = artistTopAlbums,
                    Success = true
                };
            }
        }
        else
        {
            var imagesToGet = chartSettings.ReleaseYearFilter.HasValue ||
                              chartSettings.ReleaseDecadeFilter.HasValue ||
                              chartSettings.FilteredArtist != null
                ? 1000
                : 250;
            albums = await this._dataSourceFactory.GetTopAlbumsAsync(userSettings.UserNameLastFm,
                chartSettings.TimeSettings, imagesToGet, useCache: true);

            if (chartSettings.FilteredArtist != null)
            {
                albums.Content.TopAlbums = albums.Content.TopAlbums
                    .Where(f => f.ArtistName.Equals(chartSettings.FilteredArtist.Name,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        if (albums?.Content?.TopAlbums == null || albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
        {
            var count = albums?.Content?.TopAlbums?.Count ?? 0;
            var reply =
                $"Not enough scrobbled albums ({count} of required {chartSettings.ImagesNeeded}) in {chartSettings.TimeSettings.Description} time period.\n\n" +
                $"Try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList}).";

            if (chartSettings.SkipWithoutImage && chartSettings.FilteredArtist == null)
            {
                reply += "\n\n" +
                         $"Note that {extraAlbums} extra albums are required because you are skipping albums without an image.";
            }

            response.Embed.Description = reply;
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        if ((chartSettings.ReleaseYearFilter.HasValue || chartSettings.ReleaseDecadeFilter.HasValue) &&
            chartSettings.TimeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var topAllTimeDb = await this._albumService.GetUserAllTimeTopAlbums(userSettings.UserId);
            if (topAllTimeDb.Count > 1000)
            {
                albums.Content.TopAlbums = topAllTimeDb;
                albums.Content.TotalAmount = topAllTimeDb.Count;
            }
        }

        albums.Content.TopAlbums = await this._albumService.FillMissingAlbumCovers(albums.Content.TopAlbums);

        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseYear(albums, chartSettings.ReleaseYearFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                response.Embed.Description =
                    $"Sorry, you haven't listened to enough albums released in {chartSettings.ReleaseYearFilter} ({albums.Content.TopAlbums.Count} of required {chartSettings.ImagesNeeded}) to generate a chart.\n" +
                    $"Please try a smaller chart, a different year or a bigger time period ({Constants.CompactTimePeriodList})";
                response.ResponseType = ResponseType.Embed;
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }
        }
        else if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseDecade(albums,
                chartSettings.ReleaseDecadeFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                response.Embed.Description =
                    $"Sorry, you haven't listened to enough albums released in the {chartSettings.ReleaseDecadeFilter}s ({albums.Content.TopAlbums.Count} of required {chartSettings.ImagesNeeded}) to generate a chart.\n" +
                    $"Please try a smaller chart, a different year or a bigger time period ({Constants.CompactTimePeriodList})";
                response.ResponseType = ResponseType.Embed;
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }
        }

        var topAlbums = albums.Content.TopAlbums;

        var imagesToRequest = chartSettings.ImagesNeeded + extraAlbums;
        topAlbums = topAlbums.Take(imagesToRequest).ToList();

        var albumsWithoutImage = topAlbums.Where(f => f.AlbumCoverUrl == null).ToList();

        var amountToFetch = albumsWithoutImage.Count > 3 ? 3 : albumsWithoutImage.Count;
        for (var i = 0; i < amountToFetch; i++)
        {
            var albumWithoutImage = albumsWithoutImage[i];
            var albumCall = await this._dataSourceFactory.GetAlbumInfoAsync(albumWithoutImage.ArtistName,
                albumWithoutImage.AlbumName, userSettings.UserNameLastFm);
            if (albumCall.Success && albumCall.Content?.AlbumUrl != null)
            {
                var spotifyArtistImage = await this._musicDataFactory.GetOrStoreAlbumAsync(albumCall.Content);
                if (spotifyArtistImage?.SpotifyImageUrl != null)
                {
                    var index = topAlbums.FindIndex(f => f.ArtistName == albumWithoutImage.ArtistName &&
                                                         f.AlbumName == albumWithoutImage.AlbumName);
                    topAlbums[index].AlbumCoverUrl = spotifyArtistImage.SpotifyImageUrl;
                }
            }
        }

        chartSettings.Albums = topAlbums;

        var url =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/albums?{chartSettings.TimespanUrlString}";
        var embedTitle = new StringBuilder();
        embedTitle.Append(
            $"[{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Chart]({url}) for {userSettings.DisplayName}");

        var embedDescription = new StringBuilder();

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix);

        var nsfwAllowed = context.DiscordGuild == null || ((SocketTextChannel)context.DiscordChannel).IsNsfw;
        var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                $"{chartSettings.CensoredItems.Value} {StringExtensions.GetAlbumsString(chartSettings.CensoredItems.Value)} filtered due to images that are not allowed to be posted on Discord.");
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayBuilder("**⚠️ Contains NSFW covers - Click to reveal**"));
        }

        response.FileDescription = StringExtensions.TruncateLongString(chartSettings.FileDescription.ToString(), 1024);
        response.FileName =
            $"album-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";

        response.ComponentsContainer.AddComponent(new MediaGalleryBuilder().AddItem($"attachment://{response.FileName}",
            StringExtensions.TruncateLongString(response.FileDescription, 256),
            isSpoiler: chartSettings.ContainsNsfw));

        response.ComponentsContainer.AddComponent(new TextDisplayBuilder($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(embedDescription.ToString()));
        }

        if (!userSettings.DifferentUser)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(
                $"-# {userSettings.UserNameLastFm} has {context.ContextUser.TotalPlaycount.Format(context.NumberFormat)} scrobbles"));
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(
                $"-# Chart requested by {await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)}"));
        }

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.Components = new ComponentBuilder().WithSelectMenu(context.SelectMenu);
        }

        if (supporter != null)
        {
            var actionRow = new ActionRowBuilder();
            actionRow.WithButton(Constants.GetSupporterButton,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "chart-broughtby"),
                style: ButtonStyle.Secondary);
            response.ComponentsV2.AddComponent(actionRow);
        }

        return response;
    }

    public async Task<ResponseModel> ArtistChartAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        ChartSettings chartSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed,
        };

        if (chartSettings.ImagesNeeded > 100)
        {
            response.Embed.Description = $"You can't create a chart with more than 100 images (10x10).\n" +
                                         $"Please try a smaller size.";
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var extraArtists = 0;
        if (chartSettings.SkipWithoutImage)
        {
            extraArtists = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
        }

        var imagesToRequest = chartSettings.ImagesNeeded + extraArtists;

        var artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
            chartSettings.TimeSettings, imagesToRequest, useCache: true);

        if (artists.Content.TopArtists == null || artists.Content.TopArtists.Count < chartSettings.ImagesNeeded)
        {
            var count = artists.Content.TopArtists?.Count ?? 0;

            var reply =
                $"User hasn't listened to enough artists ({count} of required {chartSettings.ImagesNeeded}) for a chart this size. \n" +
                $"Please try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList}).";

            if (chartSettings.SkipWithoutImage)
            {
                reply += "\n\n" +
                         $"Note that {extraArtists} extra albums are required because you are skipping artists without an image.";
            }

            response.Embed.Description = reply;
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return response;
        }

        var topArtists = artists.Content.TopArtists;

        topArtists = await this._artistService.FillArtistImages(topArtists);

        var artistsWithoutImages = topArtists.Where(w => w.ArtistImageUrl == null).ToList();

        var amountToFetch = artistsWithoutImages.Count > 3 ? 3 : artistsWithoutImages.Count;
        for (int i = 0; i < amountToFetch; i++)
        {
            var artistWithoutImage = artistsWithoutImages[i];

            var artistCall =
                await this._dataSourceFactory.GetArtistInfoAsync(artistWithoutImage.ArtistName,
                    userSettings.UserNameLastFm);
            if (artistCall.Success && artistCall.Content?.ArtistUrl != null)
            {
                var spotifyArtistImage = await this._musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);
                if (spotifyArtistImage != null)
                {
                    var index = topArtists.FindIndex(f => f.ArtistName == artistWithoutImage.ArtistName);
                    topArtists[index].ArtistImageUrl = spotifyArtistImage.SpotifyImageUrl;
                }
            }
        }

        chartSettings.Artists = topArtists;

        var url =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{chartSettings.TimespanUrlString}";

        var embedTitle = new StringBuilder();

        embedTitle.Append(
            $"[{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Artist Chart]({url}) for {userSettings.DisplayName}");

        var embedDescription = new StringBuilder();

        var footer = new StringBuilder();
        if (!userSettings.DifferentUser)
        {
            footer.AppendLine(
                $"-# {userSettings.UserNameLastFm} has {context.ContextUser.TotalPlaycount.Format(context.NumberFormat)} scrobbles");
        }
        else
        {
            footer.AppendLine(
                $"-# Chart requested by {await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)}");
        }

        footer.AppendLine("-# Image source: Spotify | Use 'skip' to skip artists without images");

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix);
        if (supporter != null)
        {
            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
                style: ButtonStyle.Secondary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "chart-broughtby"));
        }

        var nsfwAllowed = context.DiscordGuild == null || ((SocketTextChannel)context.DiscordChannel).IsNsfw;
        var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                $"{chartSettings.CensoredItems.Value} {StringExtensions.GetArtistsString(chartSettings.CensoredItems.Value)} filtered due to images that are not allowed to be posted on Discord.");
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayBuilder("**⚠️ Contains NSFW images - Click to reveal**"));
        }

        response.FileName =
            $"artist-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";

        response.ComponentsContainer.AddComponent(new MediaGalleryBuilder().AddItem($"attachment://{response.FileName}",
            isSpoiler: chartSettings.ContainsNsfw));

        response.ComponentsContainer.AddComponent(new TextDisplayBuilder($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayBuilder(embedDescription.ToString()));
        }

        response.ComponentsContainer.AddComponent(new TextDisplayBuilder(footer.ToString()));

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.Components = new ComponentBuilder().WithSelectMenu(context.SelectMenu);
        }

        return response;
    }
}
