using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core;

public class AffinityService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly GenreService _genreService;
    private readonly CountryService _countryService;

    public AffinityService(IMemoryCache cache, IOptions<BotSettings> botSettings, GenreService genreService,
        CountryService countryService)
    {
        this._cache = cache;
        this._botSettings = botSettings.Value;
        this._genreService = genreService;
        this._countryService = countryService;
    }

    public async Task<ICollection<AffinityItemDto>> GetAllTimeTopArtistForGuild(int guildId, bool largeGuild, bool bypassCache = false)
    {
        var cacheKey = $"guild-affinity-top-artist-alltime-{guildId}";

        var cachedArtistsAvailable = this._cache.TryGetValue(cacheKey, out ICollection<AffinityItemDto> guildArtists);
        if (cachedArtistsAvailable && !bypassCache)
        {
            return guildArtists;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        guildArtists = await WhoKnowsRepository.GetAllTimeTopArtistForGuild(guildId, largeGuild, connection);

        this._cache.Set(cacheKey, guildArtists, TimeSpan.FromMinutes(10));

        return guildArtists;
    }

    public async Task<ICollection<AffinityItemDto>> GetQuarterlyTopArtistForGuild(int guildId, bool largeGuild, bool bypassCache = false)
    {
        var cacheKey = $"guild-affinity-top-artist-quarterly-{guildId}";

        var cachedArtistsAvailable = this._cache.TryGetValue(cacheKey, out ICollection<AffinityItemDto> guildArtists);
        if (cachedArtistsAvailable && !bypassCache)
        {
            return guildArtists;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        guildArtists = await WhoKnowsRepository.GetQuarterlyTopArtistForGuild(guildId, largeGuild, connection);

        this._cache.Set(cacheKey, guildArtists, TimeSpan.FromMinutes(10));

        return guildArtists;
    }


    public async Task<ConcurrentDictionary<int, AffinityUser>> GetAffinity(
        IEnumerable<AffinityItemDto> guildAllTimeArtists,
        List<AffinityItemDto> ownAllTime,
        IEnumerable<AffinityItemDto> guildQuarterlyArtists,
        List<AffinityItemDto> ownQuarterly)
    {
        var ownAllTimeArtists = ownAllTime.GroupBy(g => g.Name)
            .ToDictionary(d => d.First().Name, d => d.First().Position);
        var ownAllTimeArtistsConcurrent = new ConcurrentDictionary<string, int>(ownAllTimeArtists);

        var ownAllTimeGenres = (await this._genreService.GetTopGenresWithPositionForTopArtists(ownAllTime))
            .ToDictionary(d => d.Name, d => d.Position);
        var ownAllTimeGenresConcurrent = new ConcurrentDictionary<string, int>(ownAllTimeGenres);

        var ownAllTimeCountries = (await this._countryService.GetTopCountriesForTopArtists(ownAllTime))
            .ToDictionary(d => d.Name, d => d.Position);
        var ownAllTimeCountriesConcurrent = new ConcurrentDictionary<string, int>(ownAllTimeCountries);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = 6
        };

        var results = new ConcurrentDictionary<int, AffinityUser>();
        await Parallel.ForEachAsync(guildAllTimeArtists.GroupBy(g => g.UserId), parallelOptions, async (guildUserTopArtists, _) =>
        {
            var result = await GetAffinityUser(guildUserTopArtists.Key, ownAllTimeArtistsConcurrent, ownAllTimeGenresConcurrent, ownAllTimeCountriesConcurrent, guildUserTopArtists.ToList());
            results.TryAdd(result.UserId, result);
        });

        var ownQuarterlyArtists = ownQuarterly
            .GroupBy(g => g.Name)
            .ToDictionary(d => d.First().Name, d => d.First().Position);
        var ownQuarterArtistsConcurrent = new ConcurrentDictionary<string, int>(ownQuarterlyArtists);

        var ownQuarterlyGenres = (await this._genreService.GetTopGenresWithPositionForTopArtists(ownQuarterly))
            .ToDictionary(d => d.Name, d => d.Position);
        var ownQuarterlyGenresConcurrent = new ConcurrentDictionary<string, int>(ownQuarterlyGenres);

        var ownQuarterlyCountries = (await this._countryService.GetTopCountriesForTopArtists(ownQuarterly))
            .ToDictionary(d => d.Name, d => d.Position);
        var ownQuarterlyCountriesConcurrent = new ConcurrentDictionary<string, int>(ownQuarterlyCountries);

        await Parallel.ForEachAsync(guildQuarterlyArtists.GroupBy(g => g.UserId), parallelOptions, async (guildUserTopArtists, _) =>
        {
            var result = await GetAffinityUser(guildUserTopArtists.Key, ownQuarterArtistsConcurrent,
                ownQuarterlyGenresConcurrent, ownQuarterlyCountriesConcurrent, guildUserTopArtists.ToList());

            if (results.TryGetValue(result.UserId, out var value))
            {
                value.ArtistPoints += result.ArtistPoints * 2;
                value.GenrePoints += result.GenrePoints * 2;
                value.CountryPoints += result.CountryPoints * 2;
                value.TotalPoints += result.TotalPoints * 2;
            }
            else
            {
                results.TryAdd(result.UserId, result);
            }
        });

        return results;
    }

    public async Task<AffinityUser> GetAffinityUser(int userId,
        IReadOnlyDictionary<string, int> artistDictionary,
        IReadOnlyDictionary<string, int> genreDictionary,
        IReadOnlyDictionary<string, int> countryDictionary,
        ICollection<AffinityItemDto> otherTopArtists)
    {
        var artistPoints = 0;
        var genrePoints = 0;
        var countryPoints = 0;

        foreach (var otherArtist in otherTopArtists)
        {
            if (artistDictionary.TryGetValue(otherArtist.Name, out var value))
            {
                artistPoints += AddPoints(value, otherArtist.Position);
            }
        }

        var otherTopGenres = await this._genreService.GetTopGenresWithPositionForTopArtists(otherTopArtists);

        foreach (var otherTopGenre in otherTopGenres)
        {
            if (genreDictionary.TryGetValue(otherTopGenre.Name, out var value))
            {
                genrePoints += AddPoints(value, otherTopGenre.Position);
            }
        }

        var otherTopCountries = await this._countryService.GetTopCountriesForTopArtists(otherTopArtists);

        foreach (var otherTopCountry in otherTopCountries)
        {
            if (countryDictionary.TryGetValue(otherTopCountry.Name, out var value))
            {
                countryPoints += AddPoints(value, otherTopCountry.Position);
            }
        }

        return new AffinityUser
        {
            ArtistPoints = artistPoints,
            GenrePoints = genrePoints,
            CountryPoints = countryPoints,
            TotalPoints = artistPoints * 0.42 + genrePoints * 0.42 + countryPoints * 0.16,
            UserId = userId
        };
    }

    public static int AddPoints(int ownPosition, int otherPosition)
    {
        return otherPosition switch
        {
            <= 5 => ownPosition switch
            {
                <= 5 => 32,
                <= 10 => 18,
                <= 25 => 12,
                <= 40 => 6,
                <= 60 => 3,
                <= 120 => 2,
                _ => 1
            },
            <= 10 => ownPosition switch
            {
                <= 10 => 18,
                <= 25 => 12,
                <= 40 => 6,
                <= 60 => 4,
                <= 120 => 2,
                _ => 1
            },
            <= 25 => ownPosition switch
            {
                <= 25 => 12,
                <= 40 => 6,
                <= 60 => 4,
                <= 120 => 2,
                _ => 1
            },
            <= 40 => ownPosition switch
            {
                <= 40 => 6,
                <= 60 => 4,
                <= 120 => 2,
                _ => 1
            },
            <= 60 => ownPosition switch
            {
                <= 60 => 4,
                <= 120 => 2,
                _ => 1
            },
            <= 120 => ownPosition switch
            {
                <= 120 => 2,
                _ => 1
            },
            _ => 1
        };
    }
}
