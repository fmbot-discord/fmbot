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
        this._countries = JsonSerializer.Deserialize<List<CountryInfo>>(countryJson);
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

        var searchQuery = countryValues.ToLower().Replace(" ", "").Replace("-", "");

        var foundCountry = this._countries
            .FirstOrDefault(f => f.Name.Replace(" ", "").Replace("-", "").ToLower() == searchQuery ||
                                 f.Code.ToLower() == searchQuery);

        return foundCountry;
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
            .OrderByDescending(o => o.Artists.Count)
            .ToList();
    }

    public async Task<List<TopArtist>> GetTopArtistsForCountry(string country, IEnumerable<TopArtist> topArtists)
    {
        await CacheAllArtistCountries();

        var countryArtists = (List<string>)this._cache.Get(CacheKeyForCountryArtists(country.ToLower()));

        return topArtists.Where(w => countryArtists.Contains(w.ArtistName.ToLower())).ToList();
    }

    private List<CountryWithPlaycount> GetCountryWithPlaycountsForArtist(List<CountryWithPlaycount> countries, string artistName, long? artistPlaycount)
    {
        var foundCountry = (string)this._cache.Get(CacheKeyForArtistCountry(artistName.ToLower()));

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
