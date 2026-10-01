using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core.Charts;

public enum ChartDataStatus
{
    Ok,
    TooManyImages,
    NotEnough,
    NotEnoughReleaseYear,
    NotEnoughReleaseDecade,
    NotEnoughNonSingles
}

public record ChartDataResult(ChartDataStatus Status, int Amount = 0, int Extra = 0);

public interface IChartImageFallback
{
    Task<string> GetAlbumCoverAsync(TopAlbum album, string lastFmUserName);

    Task<string> GetArtistImageAsync(TopArtist artist, string lastFmUserName);
}

public class ChartDataService
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GenreService _genreService;
    private readonly AlbumFilterService _albumFilterService;
    private readonly BotSettings _botSettings;
    private readonly IChartImageFallback _imageFallback;

    public ChartDataService(IDataSourceFactory dataSourceFactory, GenreService genreService,
        AlbumFilterService albumFilterService, IOptions<BotSettings> botSettings,
        IChartImageFallback imageFallback = null)
    {
        this._dataSourceFactory = dataSourceFactory;
        this._genreService = genreService;
        this._albumFilterService = albumFilterService;
        this._botSettings = botSettings.Value;
        this._imageFallback = imageFallback;
    }

    public async Task<ChartDataResult> GetAlbumsAsync(ChartSettings chartSettings, int userId, string lastFmUserName)
    {
        if (chartSettings.ImagesNeeded > ChartRenderer.MaxImages)
        {
            return new ChartDataResult(ChartDataStatus.TooManyImages);
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
            var artistTopAlbums = await GetTopAlbumsForArtist(userId, chartSettings.FilteredArtist.Name);
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
            albums = await this._dataSourceFactory.GetTopAlbumsAsync(lastFmUserName,
                chartSettings.TimeSettings, imagesToGet, useCache: true);

            if (chartSettings.FilteredArtist != null)
            {
                albums.Content = albums.Content with
                {
                    TopAlbums = albums.Content.TopAlbums
                        .Where(f => f.ArtistName.Equals(chartSettings.FilteredArtist.Name,
                            StringComparison.OrdinalIgnoreCase))
                        .ToList()
                };
            }
        }

        if (chartSettings.HasGenreFilter && albums?.Content?.TopAlbums != null)
        {
            var artistsInGenres = await this._genreService.GetArtistsInGenres(
                albums.Content.TopAlbums.Select(f => f.ArtistName), chartSettings.FilteredGenres);

            albums.Content = albums.Content with
            {
                TopAlbums = albums.Content.TopAlbums
                    .Where(f => artistsInGenres.Contains(f.ArtistName))
                    .ToList()
            };
        }

        if (albums?.Content?.TopAlbums == null || albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
        {
            return new ChartDataResult(ChartDataStatus.NotEnough, albums?.Content?.TopAlbums?.Count ?? 0, extraAlbums);
        }

        if ((chartSettings.ReleaseYearFilter.HasValue || chartSettings.ReleaseDecadeFilter.HasValue) &&
            chartSettings.TimeSettings.TimePeriod == TimePeriod.AllTime)
        {
            var topAllTimeDb = chartSettings.ReleaseYearFilter.HasValue
                ? await this._albumFilterService.GetUserAllTimeTopAlbumsByReleaseYear(userId,
                    chartSettings.ReleaseYearFilter.Value)
                : await this._albumFilterService.GetUserAllTimeTopAlbumsByReleaseDecade(userId,
                    chartSettings.ReleaseDecadeFilter.Value);

            if (chartSettings.HasGenreFilter)
            {
                var artistsInGenres = await this._genreService.GetArtistsInGenres(
                    topAllTimeDb.Select(f => f.ArtistName), chartSettings.FilteredGenres);

                topAllTimeDb = topAllTimeDb.Where(f => artistsInGenres.Contains(f.ArtistName)).ToList();
            }

            albums.Content = albums.Content with { TopAlbums = topAllTimeDb, TotalAmount = topAllTimeDb.Count };
        }

        if (chartSettings.ReleaseYearFilter.HasValue)
        {
            albums = await this._albumFilterService.FilterAlbumToReleaseYear(albums,
                chartSettings.ReleaseYearFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return new ChartDataResult(ChartDataStatus.NotEnoughReleaseYear, albums.Content.TopAlbums.Count,
                    extraAlbums);
            }
        }
        else if (chartSettings.ReleaseDecadeFilter.HasValue)
        {
            albums = await this._albumFilterService.FilterAlbumToReleaseDecade(albums,
                chartSettings.ReleaseDecadeFilter.Value);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return new ChartDataResult(ChartDataStatus.NotEnoughReleaseDecade, albums.Content.TopAlbums.Count,
                    extraAlbums);
            }
        }

        if (chartSettings.FilterSingles)
        {
            albums = await this._albumFilterService.FilterAlbumsThatAreSingles(albums);

            if (albums.Content.TopAlbums.Count < chartSettings.ImagesNeeded)
            {
                return new ChartDataResult(ChartDataStatus.NotEnoughNonSingles, albums.Content.TopAlbums.Count,
                    extraAlbums);
            }
        }

        var imagesToRequest = chartSettings.ImagesNeeded + extraAlbums;
        var topAlbums = albums.Content.TopAlbums.Take(imagesToRequest).ToList();

        if (topAlbums.Count != 0)
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await AlbumRepository.GetAlbumCovers(topAlbums, connection);
        }

        if (this._imageFallback != null)
        {
            var albumsWithoutImage = topAlbums.Where(f => f.AlbumCoverUrl == null).ToList();

            var amountToFetch = albumsWithoutImage.Count > 3 ? 3 : albumsWithoutImage.Count;
            for (var i = 0; i < amountToFetch; i++)
            {
                var albumWithoutImage = albumsWithoutImage[i];
                var coverUrl = await this._imageFallback.GetAlbumCoverAsync(albumWithoutImage, lastFmUserName);
                if (coverUrl != null)
                {
                    var index = topAlbums.FindIndex(f => f.ArtistName == albumWithoutImage.ArtistName &&
                                                         f.AlbumName == albumWithoutImage.AlbumName);
                    topAlbums[index].AlbumCoverUrl = coverUrl;
                }
            }
        }

        chartSettings.Albums = topAlbums;

        return new ChartDataResult(ChartDataStatus.Ok, topAlbums.Count, extraAlbums);
    }

    public async Task<ChartDataResult> GetArtistsAsync(ChartSettings chartSettings, string lastFmUserName)
    {
        if (chartSettings.ImagesNeeded > ChartRenderer.MaxImages)
        {
            return new ChartDataResult(ChartDataStatus.TooManyImages);
        }

        var extraArtists = 0;
        if (chartSettings.SkipWithoutImage)
        {
            extraArtists = chartSettings.Height * 2 + (chartSettings.Height > 5 ? 8 : 2);
        }

        var imagesToRequest = chartSettings.HasGenreFilter
            ? 1000
            : chartSettings.ImagesNeeded + extraArtists;

        var artists = await this._dataSourceFactory.GetTopArtistsAsync(lastFmUserName,
            chartSettings.TimeSettings, imagesToRequest, useCache: true);

        var topArtists = artists?.Content?.TopArtists?.ToList() ?? [];

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
            return new ChartDataResult(ChartDataStatus.NotEnough, topArtists.Count, extraArtists);
        }

        topArtists = topArtists.Take(chartSettings.ImagesNeeded + extraArtists).ToList();

        if (topArtists.Any(w => string.IsNullOrWhiteSpace(w.ArtistImageUrl)))
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await ArtistRepository.FillArtistImages(topArtists, connection);
        }

        if (this._imageFallback != null)
        {
            var artistsWithoutImages = topArtists.Where(w => w.ArtistImageUrl == null).ToList();

            var amountToFetch = artistsWithoutImages.Count > 3 ? 3 : artistsWithoutImages.Count;
            for (var i = 0; i < amountToFetch; i++)
            {
                var artistWithoutImage = artistsWithoutImages[i];
                var imageUrl = await this._imageFallback.GetArtistImageAsync(artistWithoutImage, lastFmUserName);
                if (imageUrl != null)
                {
                    var index = topArtists.FindIndex(f => f.ArtistName == artistWithoutImage.ArtistName);
                    topArtists[index].ArtistImageUrl = imageUrl;
                }
            }
        }

        chartSettings.Artists = topArtists;

        return new ChartDataResult(ChartDataStatus.Ok, topArtists.Count, extraArtists);
    }

    public async Task<List<string>> GetGenreOptions(bool artistChart, string lastFmUserName,
        TimeSettingsModel timeSettings, IReadOnlyCollection<string> selectedGenres)
    {
        List<string> artistNames;
        if (!artistChart)
        {
            var topAlbums = await this._dataSourceFactory.GetTopAlbumsAsync(
                lastFmUserName, timeSettings, 250, useCache: true);
            artistNames = topAlbums?.Content?.TopAlbums?
                .Select(a => a.ArtistName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList() ?? [];
        }
        else
        {
            var topArtists = await this._dataSourceFactory.GetTopArtistsAsync(
                lastFmUserName, timeSettings, 250, useCache: true);
            artistNames = topArtists?.Content?.TopArtists?
                .Select(a => a.ArtistName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList() ?? [];
        }

        var chartGenres = await this._genreService.GetTopGenresForTopArtistsString(artistNames);

        var menuGenres = new List<string>();
        foreach (var genre in selectedGenres)
        {
            if (!menuGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                menuGenres.Add(genre);
            }
        }

        foreach (var genre in chartGenres)
        {
            if (menuGenres.Count >= 25)
            {
                break;
            }

            if (!menuGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
            {
                menuGenres.Add(genre);
            }
        }

        return menuGenres;
    }

    private async Task<TopAlbumList> GetTopAlbumsForArtist(int userId, string artistName)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var userAlbums = await AlbumRepository.GetUserAlbumsForArtist(userId, artistName, connection);
        var topAlbums = userAlbums.Select(s => new TopAlbum
        {
            AlbumName = s.Name,
            ArtistName = s.ArtistName,
            UserPlaycount = s.Playcount
        });

        return new TopAlbumList
        {
            TopAlbums = topAlbums.ToList()
        };
    }
}
