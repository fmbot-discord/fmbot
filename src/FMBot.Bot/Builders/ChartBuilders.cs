using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using NetCord;
using NetCord.Rest;
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
    private readonly GenreService _genreService;

    public ChartBuilders(ChartService chartService,
        IDataSourceFactory dataSourceFactory,
        AlbumService albumService,
        SupporterService supporterService,
        ArtistsService artistService,
        MusicDataFactory musicDataFactory,
        GenreService genreService)
    {
        this._chartService = chartService;
        this._dataSourceFactory = dataSourceFactory;
        this._albumService = albumService;
        this._supporterService = supporterService;
        this._artistService = artistService;
        this._musicDataFactory = musicDataFactory;
        this._genreService = genreService;
    }

    private static ResponseModel BuildChartValidationError(
        ContextModel context,
        string message,
        string chartType,
        ChartSettings chartSettings,
        string userNameLastFm)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
            CommandResponse = CommandResponse.WrongInput
        };

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                context.DiscordUser.Id, chartType, chartSettings, userNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(message)]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(message));
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);

        return response;
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
            return BuildChartValidationError(context,
                context.Localize("chart.tooManyImages"),
                InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
        }

        var extraAlbums = 0;
        if (chartSettings.SkipWithoutImage)
        {
            extraAlbums = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
        }

        if (chartSettings.SkipNsfw)
        {
            extraAlbums += chartSettings.Height;
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
                              chartSettings.FilteredArtist != null ||
                              chartSettings.FilterSingles ||
                              chartSettings.HasGenreFilter
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

        if (chartSettings.HasGenreFilter && albums?.Content?.TopAlbums != null)
        {
            var artistsInGenres = await this._genreService.GetArtistsInGenres(
                albums.Content.TopAlbums.Select(f => f.ArtistName), chartSettings.FilteredGenres);

            albums.Content.TopAlbums = albums.Content.TopAlbums
                .Where(f => artistsInGenres.Contains(f.ArtistName))
                .ToList();
        }

        if (albums?.Content?.TopAlbums == null || albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
        {
            var count = albums?.Content?.TopAlbums?.Count ?? 0;
            var reply = new StringBuilder();
            if (chartSettings.FilteredArtist != null)
            {
                reply.AppendLine(context.Localize("chart.notEnoughAlbumsArtist",
                    ("amount", count.ToString()),
                    ("required", chartSettings.ImagesNeeded.ToString()),
                    ("artist", chartSettings.FilteredArtist.Name),
                    ("url", LastfmUrlExtensions.GetArtistUrl(chartSettings.FilteredArtist.Name)),
                    ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                reply.AppendLine();
                reply.AppendLine(context.Localize("chart.tryDifferentArtistFilter",
                    ("periods", Constants.CompactTimePeriodList)));
            }
            else if (chartSettings.HasGenreFilter)
            {
                reply.AppendLine(context.LocalizeCount("chart.notEnoughAlbumsGenre",
                    chartSettings.FilteredGenres.Count,
                    ("amount", count.ToString()),
                    ("required", chartSettings.ImagesNeeded.ToString()),
                    ("genres", string.Join("**, **", chartSettings.FilteredGenres.Select(StringExtensions.Sanitize))),
                    ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                reply.AppendLine();
                reply.AppendLine(context.Localize("chart.tryDifferentGenres",
                    ("periods", Constants.CompactTimePeriodList)));
            }
            else
            {
                reply.AppendLine(context.Localize("chart.notEnoughAlbums",
                    ("amount", count.ToString()),
                    ("required", chartSettings.ImagesNeeded.ToString()),
                    ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                reply.AppendLine();
                reply.AppendLine(context.Localize("chart.tryDifferent",
                    ("periods", Constants.CompactTimePeriodList)));
            }

            if (chartSettings.SkipWithoutImage && chartSettings.FilteredArtist == null && !chartSettings.HasGenreFilter)
            {
                reply.AppendLine();
                reply.AppendLine(context.Localize("chart.extraAlbumsRequired",
                    ("amount", extraAlbums.ToString())));
            }

            return BuildChartValidationError(context, reply.ToString(),
                InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
        }

        if ((chartSettings.ReleaseYearFilter.HasValue || chartSettings.ReleaseDecadeFilter.HasValue) &&
            chartSettings.TimeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var topAllTimeDb = chartSettings.ReleaseYearFilter.HasValue
                ? await this._albumService.GetUserAllTimeTopAlbumsByReleaseYear(userSettings.UserId,
                    chartSettings.ReleaseYearFilter.Value)
                : await this._albumService.GetUserAllTimeTopAlbumsByReleaseDecade(userSettings.UserId,
                    chartSettings.ReleaseDecadeFilter.Value);

            if (chartSettings.HasGenreFilter)
            {
                var artistsInGenres = await this._genreService.GetArtistsInGenres(
                    topAllTimeDb.Select(f => f.ArtistName), chartSettings.FilteredGenres);

                topAllTimeDb = topAllTimeDb.Where(f => artistsInGenres.Contains(f.ArtistName)).ToList();
            }

            albums.Content.TopAlbums = topAllTimeDb;
            albums.Content.TotalAmount = topAllTimeDb.Count;
        }

        albums.Content.TopAlbums = await this._albumService.FillMissingAlbumCovers(albums.Content.TopAlbums);

        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseYear(albums, chartSettings.ReleaseYearFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughReleaseYear",
                        ("year", chartSettings.ReleaseYearFilter.Value.ToString()),
                        ("amount", albums.Content.TopAlbums.Count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            }
        }
        else if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            albums = await this._albumService.FilterAlbumToReleaseDecade(albums,
                chartSettings.ReleaseDecadeFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughReleaseDecade",
                        ("decade", chartSettings.ReleaseDecadeFilter.Value.ToString()),
                        ("amount", albums.Content.TopAlbums.Count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            }
        }

        if (chartSettings.FilterSingles)
        {
            albums = await this._albumService.FilterAlbumsThatAreSingles(albums);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughNonSingles",
                        ("amount", albums.Content.TopAlbums.Count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
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
        embedTitle.Append(context.Localize("chart.albumChartTitle",
            ("size", $"{chartSettings.Width}x{chartSettings.Height}"),
            ("timespan", context.Localizer.PeriodLabel(chartSettings.TimeSettings)),
            ("url", url),
            ("user", userSettings.DisplayName)));

        var embedDescription = new StringBuilder();

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix,
            context.Localizer);

        var nsfwAllowed = context.DiscordGuild == null || ((TextGuildChannel)context.DiscordChannel).Nsfw;
        using var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                context.LocalizeCount("chart.albumsFiltered", chartSettings.CensoredItems.Value));
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayProperties(context.Localize("chart.containsNsfwCovers")));
        }

        response.FileDescription = StringExtensions.TruncateLongString(chartSettings.FileDescription.ToString(), 1024);
        response.FileName =
            $"album-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            {
                Description = StringExtensions.TruncateLongString(response.FileDescription, 256),
                Spoiler = chartSettings.ContainsNsfw
            };

        response.ComponentsContainer.AddComponent(new MediaGalleryProperties
        {
            mediaGallery
        });

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(embedDescription.ToString()));
        }

        var footerText = !userSettings.DifferentUser
            ? context.LocalizeCount("chart.userScrobbles", context.ContextUser.TotalPlaycount.GetValueOrDefault(),
                ("user", userSettings.UserNameLastFm))
            : context.Localize("chart.requestedBy",
                ("user", await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)));

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                               context.DiscordUser.Id, InteractionConstants.Chart.AlbumType,
                               chartSettings, userSettings.UserNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(footerText)]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(footerText));
        }

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.StringMenus.Add(context.SelectMenu);
        }

        if (supporter != null)
        {
            var actionRow = new ActionRowProperties();
            actionRow.WithButton(context.Localize("buttons.getFmbotSupporter"),
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
            return BuildChartValidationError(context,
                context.Localize("chart.tooManyImages"),
                InteractionConstants.Chart.ArtistType, chartSettings, userSettings.UserNameLastFm);
        }

        var extraArtists = 0;
        if (chartSettings.SkipWithoutImage)
        {
            extraArtists = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
        }

        var imagesToRequest = chartSettings.HasGenreFilter
            ? 1000
            : chartSettings.ImagesNeeded + extraArtists;

        var artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
            chartSettings.TimeSettings, imagesToRequest, useCache: true);

        var topArtists = artists?.Content?.TopArtists ?? [];

        if (chartSettings.HasGenreFilter && topArtists.Count != 0)
        {
            var genreArtists =
                await this._genreService.GetArtistsForGenres(chartSettings.FilteredGenres, topArtists);
            var artistsInGenres = new HashSet<string>(
                genreArtists.SelectMany(g => g.Artists.Select(a => a.ArtistName)),
                StringComparer.OrdinalIgnoreCase);

            topArtists = topArtists.Where(w => artistsInGenres.Contains(w.ArtistName)).ToList();
        }

        if (topArtists.Count < chartSettings.ImagesNeeded)
        {
            var count = topArtists.Count;

            string reply;
            if (chartSettings.HasGenreFilter)
            {
                reply = context.LocalizeCount("chart.notEnoughArtistsGenre",
                    chartSettings.FilteredGenres.Count,
                    ("amount", count.ToString()),
                    ("required", chartSettings.ImagesNeeded.ToString()),
                    ("genres", string.Join("**, **", chartSettings.FilteredGenres.Select(g => StringExtensions.Sanitize(g)))),
                    ("periods", Constants.CompactTimePeriodList));
            }
            else
            {
                reply = context.Localize("chart.notEnoughArtists",
                    ("amount", count.ToString()),
                    ("required", chartSettings.ImagesNeeded.ToString()),
                    ("periods", Constants.CompactTimePeriodList));

                if (chartSettings.SkipWithoutImage)
                {
                    reply += "\n\n" + context.Localize("chart.extraArtistsRequired",
                        ("amount", extraArtists.ToString()));
                }
            }

            return BuildChartValidationError(context, reply,
                InteractionConstants.Chart.ArtistType, chartSettings, userSettings.UserNameLastFm);
        }

        topArtists = topArtists.Take(chartSettings.ImagesNeeded + extraArtists).ToList();

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

        embedTitle.Append(context.Localize("chart.artistChartTitle",
            ("size", $"{chartSettings.Width}x{chartSettings.Height}"),
            ("timespan", context.Localizer.PeriodLabel(chartSettings.TimeSettings)),
            ("url", url),
            ("user", userSettings.DisplayName)));

        var embedDescription = new StringBuilder();

        var footer = new StringBuilder();
        if (!userSettings.DifferentUser)
        {
            footer.AppendLine(context.LocalizeCount("chart.userScrobbles",
                context.ContextUser.TotalPlaycount.GetValueOrDefault(),
                ("user", userSettings.UserNameLastFm)));
        }
        else
        {
            footer.AppendLine(context.Localize("chart.requestedBy",
                ("user", await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser))));
        }

        footer.AppendLine(context.Localize("chart.imageSource"));

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix,
            context.Localizer);

        var nsfwAllowed = context.DiscordGuild == null || ((TextGuildChannel)context.DiscordChannel).Nsfw;
        using var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                context.LocalizeCount("chart.artistsFiltered", chartSettings.CensoredItems.Value));
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayProperties(context.Localize("chart.containsNsfwImages")));
        }

        response.FileDescription = StringExtensions.TruncateLongString(chartSettings.FileDescription.ToString(), 1024);
        response.FileName =
            $"artist-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}.png";

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            {
                Description = StringExtensions.TruncateLongString(response.FileDescription, 256),
                Spoiler = chartSettings.ContainsNsfw
            };

        response.ComponentsContainer.AddComponent(new MediaGalleryProperties
        {
            mediaGallery
        });

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(embedDescription.ToString()));
        }

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                               context.DiscordUser.Id, InteractionConstants.Chart.ArtistType,
                               chartSettings, userSettings.UserNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(footer.ToString())]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(footer.ToString()));
        }

        var encoded = chart.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.StringMenus.Add(context.SelectMenu);
        }

        if (supporter != null)
        {
            var actionRow = new ActionRowProperties();
            actionRow.WithButton(context.Localize("buttons.getFmbotSupporter"),
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "chart-broughtby"),
                style: ButtonStyle.Secondary);
            response.ComponentsV2.AddComponent(actionRow);
        }

        return response;
    }
}
