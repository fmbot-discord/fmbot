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
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class ChartBuilders
{
    private readonly ChartService _chartService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly AlbumService _albumService;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;
    private readonly SpotifyService _spotifyService;
    private readonly ArtistsService _artistService;
    private readonly MusicDataFactory _musicDataFactory;

    public ChartBuilders(ChartService chartService,
        IDataSourceFactory dataSourceFactory,
        AlbumService albumService,
        UserService userService,
        SupporterService supporterService,
        SpotifyService spotifyService,
        ArtistsService artistService,
        MusicDataFactory musicDataFactory)
    {
        this._chartService = chartService;
        this._dataSourceFactory = dataSourceFactory;
        this._albumService = albumService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._spotifyService = spotifyService;
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
            response.Text = $"You can't create a chart with more than 100 images (10x10).\n" +
                            $"Please try a smaller size.";
            response.ResponseType = ResponseType.Text;
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

        var albums = await this._dataSourceFactory.GetTopAlbumsAsync(userSettings.UserNameLastFm,
            chartSettings.TimeSettings,
            chartSettings.ReleaseYearFilter.HasValue || chartSettings.ReleaseDecadeFilter.HasValue ? 1000 : 250,
            useCache: true);

        if (albums.Content?.TopAlbums == null || albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
        {
            var count = albums.Content?.TopAlbums?.Count ?? 0;
            var reply =
                $"User hasn't listened to enough albums ({count} of required {chartSettings.ImagesNeeded}) for a chart this size. \n" +
                $"Please try a smaller chart or a bigger time period ({Constants.CompactTimePeriodList}).";

            if (chartSettings.SkipWithoutImage)
            {
                reply += "\n\n" +
                         $"Note that {extraAlbums} extra albums are required because you are skipping albums without an image.";
            }

            response.Text = reply;
            response.ResponseType = ResponseType.Text;
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
                response.Text =
                    $"Sorry, you haven't listened to enough albums released in {chartSettings.ReleaseYearFilter} ({albums.Content.TopAlbums.Count} of required {chartSettings.ImagesNeeded}) to generate a chart.\n" +
                    $"Please try a smaller chart, a different year or a bigger time period ({Constants.CompactTimePeriodList})";
                response.ResponseType = ResponseType.Text;
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
                response.Text =
                    $"Sorry, you haven't listened to enough albums released in the {chartSettings.ReleaseDecadeFilter}s ({albums.Content.TopAlbums.Count} of required {chartSettings.ImagesNeeded}) to generate a chart.\n" +
                    $"Please try a smaller chart, a different year or a bigger time period ({Constants.CompactTimePeriodList})";
                response.ResponseType = ResponseType.Text;
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

        var embedAuthorDescription = "";
        if (!userSettings.DifferentUser)
        {
            embedAuthorDescription =
                $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Chart for " +
                await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            embedAuthorDescription =
                $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Chart for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName(embedAuthorDescription);
        response.EmbedAuthor.WithUrl(
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/albums?{chartSettings.TimespanUrlString}");

        var embedDescription = "";

        response.Embed.WithAuthor(response.EmbedAuthor);

        if (!userSettings.DifferentUser)
        {
            response.EmbedFooter.Text =
                $"{userSettings.UserNameLastFm} has {context.ContextUser.TotalPlaycount.Format(context.NumberFormat)} scrobbles";
            response.Embed.WithFooter(response.EmbedFooter);
        }

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        embedDescription +=
            ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix);
        if (supporter != null)
        {
            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
                style: ButtonStyle.Secondary, customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault);
        }

        var nsfwAllowed = context.DiscordGuild == null || ((SocketTextChannel)context.DiscordChannel).IsNsfw;
        var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription +=
                $"{chartSettings.CensoredItems.Value} {StringExtensions.GetAlbumsString(chartSettings.CensoredItems.Value)} filtered due to images that are not allowed to be posted on Discord.\n";
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            embedDescription +=
                $"⚠️ Contains NSFW covers - Click to reveal\n";
        }

        response.Embed.WithDescription(embedDescription);

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();

        response.FileName =
            $"album-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";
        response.Spoiler = chartSettings.ContainsNsfw;

        if (context.SelectMenu != null)
        {
            response.Components = new ComponentBuilder().WithSelectMenu(context.SelectMenu);
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
            response.Text = $"You can't create a chart with more than 100 images (10x10).\n" +
                            $"Please try a smaller size.";
            response.ResponseType = ResponseType.Text;
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

            response.Text = reply;
            response.ResponseType = ResponseType.Text;
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

        var embedAuthorDescription = "";
        if (!userSettings.DifferentUser)
        {
            embedAuthorDescription =
                $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Artist Chart for " +
                await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            embedAuthorDescription =
                $"{chartSettings.Width}x{chartSettings.Height} {chartSettings.TimespanString} Artist Chart for {userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName(embedAuthorDescription);
        response.EmbedAuthor.WithUrl(
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{chartSettings.TimespanUrlString}");

        var embedDescription = "";

        response.Embed.WithAuthor(response.EmbedAuthor);

        var footer = new StringBuilder();

        footer.AppendLine("Image source: Spotify | Use 'skip' to skip artists without images");

        if (!userSettings.DifferentUser)
        {
            footer.AppendLine($"{userSettings.UserNameLastFm} has {context.ContextUser.TotalPlaycount.Format(context.NumberFormat)} scrobbles");
        }

        response.Embed.WithFooter(footer.ToString());

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        embedDescription +=
            ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix);
        if (supporter != null)
        {
            response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton,
                style: ButtonStyle.Secondary, customId: InteractionConstants.SupporterLinks.GetPurchaseButtonsDefault);
        }

        var nsfwAllowed = context.DiscordGuild == null || ((SocketTextChannel)context.DiscordChannel).IsNsfw;
        var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription +=
                $"{chartSettings.CensoredItems.Value} {StringExtensions.GetArtistsString(chartSettings.CensoredItems.Value)} filtered due to images that are not allowed to be posted on Discord.\n";
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            embedDescription +=
                $"⚠️ Contains NSFW covers - Click to reveal\n";
        }

        response.Embed.WithDescription(embedDescription);

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();

        response.FileName =
            $"artist-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";
        response.Spoiler = chartSettings.ContainsNsfw;

        if (context.SelectMenu != null)
        {
            response.Components = new ComponentBuilder().WithSelectMenu(context.SelectMenu);
        }

        return response;
    }
}
