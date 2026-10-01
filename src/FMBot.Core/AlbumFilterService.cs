using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core;

public class AlbumFilterService
{
    private readonly BotSettings _botSettings;

    public AlbumFilterService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<Response<TopAlbumList>> FilterAlbumToReleaseYear(Response<TopAlbumList> albums, int year)
    {
        await EnrichTopAlbums(albums.Content.TopAlbums);

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = yearStart.AddYears(1).AddSeconds(-1);
        albums.Content = albums.Content with
        {
            TopAlbums = albums.Content.TopAlbums
                .Where(w => w.ReleaseDate.HasValue &&
                            w.ReleaseDate.Value >= yearStart &&
                            w.ReleaseDate.Value <= yearEnd)
                .ToList()
        };

        DataSourceFactory.AddAlbumTopList(albums, null);

        return albums;
    }

    public async Task<Response<TopAlbumList>> FilterAlbumToReleaseDecade(Response<TopAlbumList> albums, int decade)
    {
        await EnrichTopAlbums(albums.Content.TopAlbums);

        var decadeStart = new DateTime(decade, 1, 1);
        var decadeEnd = decadeStart.AddYears(10).AddSeconds(-1);
        albums.Content = albums.Content with
        {
            TopAlbums = albums.Content.TopAlbums
                .Where(w => w.ReleaseDate.HasValue &&
                            w.ReleaseDate.Value >= decadeStart &&
                            w.ReleaseDate.Value <= decadeEnd)
                .ToList()
        };

        DataSourceFactory.AddAlbumTopList(albums, null);

        return albums;
    }

    public async Task<Response<TopAlbumList>> FilterAlbumsThatAreSingles(Response<TopAlbumList> albums)
    {
        await EnrichTopAlbums(albums.Content.TopAlbums);

        albums.Content = albums.Content with
        {
            TopAlbums = albums.Content.TopAlbums
                .Where(w => !string.Equals(w.AlbumType, "single", StringComparison.OrdinalIgnoreCase))
                .ToList()
        };

        DataSourceFactory.AddAlbumTopList(albums, null);

        return albums;
    }

    public async Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleaseYear(int userId, int year)
    {
        return await GetUserAllTimeTopAlbumsByReleasePrefix(userId, year.ToString(), prefixLength: 4);
    }

    public async Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleaseDecade(int userId, int decade)
    {
        return await GetUserAllTimeTopAlbumsByReleasePrefix(userId, (decade / 10).ToString(), prefixLength: 3);
    }

    private async Task<List<TopAlbum>> GetUserAllTimeTopAlbumsByReleasePrefix(int userId, string prefix, int prefixLength)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await AlbumRepository.GetUserAllTimeTopAlbumsByReleasePrefix(userId, prefix, prefixLength, connection);
    }

    private async Task EnrichTopAlbums(IReadOnlyCollection<TopAlbum> list)
    {
        var albumsToEnrich = list.Where(w => w.ReleaseDate == null || w.AlbumType == null).ToList();
        if (albumsToEnrich.Count == 0)
        {
            return;
        }

        var lookup = await GetAlbumEnrichmentLookup(
            albumsToEnrich.Select(s => s.ArtistName).ToArray(),
            albumsToEnrich.Select(s => s.AlbumName).ToArray());

        foreach (var topAlbum in albumsToEnrich)
        {
            var key = (topAlbum.AlbumName.ToLower(), topAlbum.ArtistName.ToLower());

            if (lookup.TryGetValue(key, out var row))
            {
                topAlbum.ReleaseDate ??= ParseReleaseDate(row.ReleaseDate, row.ReleaseDatePrecision);
                topAlbum.ReleaseDatePrecision ??= row.ReleaseDatePrecision;
                topAlbum.AlbumType ??= !string.IsNullOrEmpty(row.AlbumType) ? row.AlbumType : null;
            }
        }
    }

    public async Task<Dictionary<(string AlbumName, string ArtistName), AlbumEnrichmentRow>> GetAlbumEnrichmentLookup(
        string[] artistNames, string[] albumNames)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var rows = await AlbumRepository.GetAlbumEnrichmentRows(artistNames, albumNames, connection);

        return rows
            .GroupBy(g => (g.AlbumName.ToLower(), g.ArtistName.ToLower()))
            .ToDictionary(g => g.Key, g => g.First());
    }

    public static DateTime? ParseReleaseDate(string releaseDate, string precision)
    {
        if (string.IsNullOrEmpty(releaseDate) || releaseDate == "0000")
        {
            return null;
        }

        try
        {
            var parsed = precision switch
            {
                "year" => DateTime.Parse($"{releaseDate}-1-1", CultureInfo.InvariantCulture),
                "month" => DateTime.Parse($"{releaseDate}-1", CultureInfo.InvariantCulture),
                "day" => DateTime.Parse(releaseDate, CultureInfo.InvariantCulture),
                _ => (DateTime?)null
            };

            return parsed.HasValue ? DateTime.SpecifyKind(parsed.Value, DateTimeKind.Utc) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
