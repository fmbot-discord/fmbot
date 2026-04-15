using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services;

public class CountryService
{
    private readonly BotSettings _botSettings;
    public readonly List<CountryInfo> Countries;

    public CountryService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;

        var countryJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "countries.json");
        var countryJson = File.ReadAllBytes(countryJsonPath);
        this.Countries = JsonSerializer.Deserialize<List<CountryInfo>>(countryJson, new JsonSerializerOptions
        {
            AllowTrailingCommas = true
        });
    }

    public CountryInfo GetValidCountry(string countryValues)
    {
        if (string.IsNullOrWhiteSpace(countryValues))
        {
            return null;
        }

        var searchQuery = TrimCountry(countryValues);

        var foundCountry = this.Countries
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

        var foundCountries = this.Countries
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

    public async Task<List<TopArtist>> GetUserArtistsForCountry(int userId, string countryCode)
    {
        const string sql = "SELECT ua.name AS ArtistName, ua.playcount AS UserPlaycount " +
                           "FROM user_artists ua " +
                           "INNER JOIN artists a ON a.id = ua.artist_id " +
                           "WHERE ua.user_id = @userId AND ua.artist_id IS NOT NULL " +
                           "AND LOWER(a.country_code) = LOWER(@countryCode) " +
                           "ORDER BY ua.playcount DESC";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return (await connection.QueryAsync<TopArtist>(sql, new { userId, countryCode })).ToList();
    }

    public async Task<List<TopCountry>> GetTopCountriesForTopArtists(IEnumerable<TopArtist> topArtists, bool addArtists = false)
    {
        if (topArtists == null)
        {
            return [];
        }

        var artistList = topArtists.ToList();
        if (artistList.Count == 0)
        {
            return [];
        }

        var artistNames = artistList.Select(a => a.ArtistName).Distinct().ToArray();

        const string sql = "SELECT a.name AS ArtistName, a.country_code AS CountryCode " +
                           "FROM artists a " +
                           "WHERE a.name = ANY(@artistNames::citext[]) " +
                           "AND a.country_code IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var countryMappings = (await connection.QueryAsync<(string ArtistName, string CountryCode)>(sql,
            new { artistNames })).ToList();

        var artistCountryMap = countryMappings
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CountryCode, StringComparer.OrdinalIgnoreCase);

        var countriesWithArtists = new List<(string CountryCode, long Playcount, TopArtist Artist)>();
        foreach (var artist in artistList)
        {
            if (artistCountryMap.TryGetValue(artist.ArtistName, out var countryCode) && artist.UserPlaycount > 0)
            {
                countriesWithArtists.Add((countryCode, artist.UserPlaycount, artist));
            }
        }

        var countries = countriesWithArtists
            .GroupBy(g => g.CountryCode, StringComparer.OrdinalIgnoreCase)
            .Select(s => new TopCountry
            {
                UserPlaycount = s.Sum(se => se.Playcount),
                CountryName = this.Countries.FirstOrDefault(f => f.Code.Equals(s.Key, StringComparison.OrdinalIgnoreCase))?.Name,
                CountryCode = s.Key,
                Artists = addArtists ? s.Select(a => a.Artist).OrderByDescending(a => a.UserPlaycount).ToList() : null
            }).ToList();

        return countries
            .OrderByDescending(o => addArtists ? o.Artists?.Count ?? 0 : o.UserPlaycount)
            .ToList();
    }

    public List<TopListObject> GetTopListForTopCountries(List<TopCountry> topCountries)
    {
        return topCountries.Select(s => new TopListObject
        {
            Name = s.CountryName,
            Playcount = s.UserPlaycount.GetValueOrDefault()
        }).ToList();
    }

    public async Task<List<AffinityItemDto>> GetTopCountriesForTopArtists(IEnumerable<AffinityItemDto> topArtists)
    {
        if (topArtists == null)
        {
            return [];
        }

        var artistList = topArtists.ToList();
        if (artistList.Count == 0)
        {
            return [];
        }

        var artistNames = artistList.Select(a => a.Name).Distinct().ToArray();

        const string sql = "SELECT a.name AS ArtistName, a.country_code AS CountryCode " +
                           "FROM artists a " +
                           "WHERE a.name = ANY(@artistNames::citext[]) " +
                           "AND a.country_code IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var countryMappings = (await connection.QueryAsync<(string ArtistName, string CountryCode)>(sql,
            new { artistNames })).ToList();

        var artistCountryMap = countryMappings
            .GroupBy(g => g.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CountryCode, StringComparer.OrdinalIgnoreCase);

        var allCountries = new List<(string CountryCode, long Playcount)>();
        foreach (var artist in artistList)
        {
            if (artistCountryMap.TryGetValue(artist.Name, out var countryCode) && artist.Playcount > 0)
            {
                allCountries.Add((countryCode, artist.Playcount));
            }
        }

        return allCountries
            .GroupBy(g => g.CountryCode, StringComparer.OrdinalIgnoreCase)
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
        return this.Countries.FirstOrDefault(f => f.Code == code)?.Name;
    }

    public async Task<List<string>> GetTopCountriesForTopArtistsString(IEnumerable<string> topArtists)
    {
        var topCountries = new List<string>();
        if (topArtists == null)
        {
            return topCountries;
        }

        var artistNames = topArtists.Distinct().ToArray();
        if (artistNames.Length == 0)
        {
            return topCountries;
        }

        const string sql = "SELECT a.name AS ArtistName, a.country_code AS CountryCode " +
                           "FROM artists a " +
                           "WHERE a.name = ANY(@artistNames::citext[]) " +
                           "AND a.country_code IS NOT NULL";

        DefaultTypeMap.MatchNamesWithUnderscores = true;
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var countryMappings = (await connection.QueryAsync<(string ArtistName, string CountryCode)>(sql,
            new { artistNames })).ToList();

        return countryMappings
            .GroupBy(g => g.CountryCode, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(o => o.Count())
            .Where(w => w.Key != null)
            .Select(s => s.Key)
            .ToList();
    }
}
