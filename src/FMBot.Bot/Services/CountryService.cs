using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class CountryService
{
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;
    private readonly List<CountryInfo> _countries;

    public CountryService(IMemoryCache cache, IOptions<BotSettings> botSettings)
    {
        this._cache = cache;
        this._botSettings = botSettings.Value;

        var countryJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "countries.json");
        var countryJson = File.ReadAllBytes(countryJsonPath);
        this._countries = JsonSerializer.Deserialize<List<CountryInfo>>(countryJson, new JsonSerializerOptions
        {
            AllowTrailingCommas = true
        });
    }

    private static string CacheKeyForArtistCountry(string artistName)
    {
        return $"artist-country-{artistName}";
    }
    private static string CacheKeyForCountryArtists(string country)
    {
        return $"country-artists-{country}";
    }

    private async Task CacheAllArtistCountries()
    {
        const string cacheKey = "artist-countries-cached";
        var cacheTime = TimeSpan.FromMinutes(5);

        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        const string sql = "SELECT LOWER(artists.name) AS artist_name, country_code " +
                           "FROM public.artists " +
                           "WHERE country_code IS NOT null;";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var artistCountries = (await connection.QueryAsync<ArtistCountryDto>(sql)).ToList();

        foreach (var artist in artistCountries)
        {
            this._cache.Set(CacheKeyForArtistCountry(artist.ArtistName), artist.CountryCode, cacheTime);
        }
        foreach (var country in artistCountries.GroupBy(g => g.CountryCode))
        {
            var artists = country.Select(s => s.ArtistName).ToList();
            this._cache.Set(CacheKeyForCountryArtists(country.Key.ToLower()), artists, cacheTime);
        }

        this._cache.Set(cacheKey, true, cacheTime);
    }

    public CountryInfo GetValidCountry(string countryValues)
    {
        if (string.IsNullOrWhiteSpace(countryValues))
        {
            return null;
        }

        var searchQuery = TrimCountry(countryValues);

        var foundCountry = this._countries
            .FirstOrDefault(f => TrimCountry(f.Name) == searchQuery ||
                                 f.Emoji == searchQuery ||
                                 f.Code.ToLower() == searchQuery ||
                                 f.Aliases != null && f.Aliases.Any(a => TrimCountry(a) == searchQuery));

        return foundCountry;
    }

    public IEnumerable<CountryInfo> SearchThroughCountries(string countryValues)
    {
        if (string.IsNullOrWhiteSpace(countryValues))
        {
            return null;
        }

        var searchQuery = TrimCountry(countryValues);

        var foundCountries = this._countries
            .Where(f => TrimCountry(f.Name) == searchQuery ||
                        f.Code.ToLower() == searchQuery ||
                        TrimCountry(f.Name).StartsWith(searchQuery) ||
                        TrimCountry(f.Name).Contains(searchQuery) ||
                        f.Aliases != null && f.Aliases.Any(a => TrimCountry(a) == searchQuery));

        return foundCountries;
    }

    public static string TrimCountry(string country)
    {
        return country.ToLower().Replace(" ", "").Replace("-", "");
    }

    public async Task<List<TopCountry>> GetTopCountriesForTopArtists(IEnumerable<TopArtist> topArtists, bool addArtists = false)
    {
        if (topArtists == null)
        {
            return new List<TopCountry>();
        }

        await CacheAllArtistCountries();

        var allCountries = new List<CountryWithPlaycount>();
        foreach (var artist in topArtists)
        {
            allCountries = GetCountryWithPlaycountsForArtist(allCountries, artist.ArtistName, artist.UserPlaycount);
        }

        var countries = allCountries
            .GroupBy(g => g.CountryCode)
            .OrderByDescending(o => o.Sum(s => s.Playcount))
            .Where(w => w.Key != null)
            .Select(s => new TopCountry
            {
                UserPlaycount = s.Sum(se => se.Playcount),
                CountryName = this._countries.FirstOrDefault(f => f.Code.ToLower() == s.Key.ToLower())?.Name,
                CountryCode = s.Key,
            }).ToList();

        if (addArtists)
        {
            foreach (var country in countries)
            {
                var countryArtists = (List<string>)this._cache.Get(CacheKeyForCountryArtists(country.CountryCode.ToLower()));
                country.Artists = topArtists.Where(w => countryArtists.Contains(w.ArtistName.ToLower())).ToList();
            }
        }

        return countries
            .OrderByDescending(o => addArtists ? o.Artists.Count : o.UserPlaycount)
            .ToList();
    }

    public async Task<List<AffinityItemDto>> GetTopCountriesForTopArtists(IEnumerable<AffinityItemDto> topArtists)
    {
        if (topArtists == null)
        {
            return new List<AffinityItemDto>();
        }

        await CacheAllArtistCountries();

        var allCountries = new List<CountryWithPlaycount>();
        foreach (var artist in topArtists)
        {
            allCountries = GetCountryWithPlaycountsForArtist(allCountries, artist.Name, artist.Playcount);
        }

        return allCountries
            .GroupBy(g => g.CountryCode)
            .OrderByDescending(o => o.Sum(s => s.Playcount))
            .Where(w => w.Key != null)
            .Select((s, i) => new AffinityItemDto
            {
                Playcount = s.Sum(se => se.Playcount),
                Name = s.Key,
                Position = i
            }).ToList();
    }

    public string CountryCodeToCountryName(string code)
    {
        return this._countries.FirstOrDefault(f => f.Code == code)?.Name;
    }

    public async Task<List<string>> GetTopCountriesForTopArtistsString(IEnumerable<string> topArtists)
    {
        var topCountries = new List<string>();
        if (topArtists == null)
        {
            return topCountries;
        }

        await CacheAllArtistCountries();

        foreach (var topArtist in topArtists)
        {
            var country = GetCountry(topArtist);
            if (country != null)
            {
                topCountries.Add(country);
            }
        }

        return topCountries
            .GroupBy(g => g)
            .OrderByDescending(o => o.Count())
            .Where(w => w.Key != null)
            .Select(s => s.Key)
            .ToList();
    }

    public async Task<List<TopArtist>> GetTopArtistsForCountry(string country, IEnumerable<TopArtist> topArtists)
    {
        await CacheAllArtistCountries();

        var countryArtists = (List<string>)this._cache.Get(CacheKeyForCountryArtists(country.ToLower()));

        if (countryArtists == null || !countryArtists.Any())
        {
            return new List<TopArtist>();
        }

        return topArtists.Where(w => countryArtists.Contains(w.ArtistName.ToLower())).ToList();
    }

    private string GetCountry(string artist)
    {
        return (string)this._cache.Get(CacheKeyForArtistCountry(artist.ToLower()));
    }

    private List<CountryWithPlaycount> GetCountryWithPlaycountsForArtist(List<CountryWithPlaycount> countries, string artistName, long? artistPlaycount)
    {
        var foundCountry = GetCountry(artistName);

        if (foundCountry != null)
        {
            var playcount = artistPlaycount.GetValueOrDefault();

            if (playcount > 0)
            {
                countries.Add(new CountryWithPlaycount(foundCountry, playcount));
            }
        }

        return countries;
    }

    private record CountryWithPlaycount(string CountryCode, long Playcount);
}
