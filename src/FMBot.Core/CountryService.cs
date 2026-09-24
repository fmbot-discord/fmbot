using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core;

public class CountryService
{
    private readonly BotSettings _botSettings;
    public readonly List<CountryInfo> Countries;

    private async Task<NpgsqlConnection> OpenConnection()
    {
        var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

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
        await using var connection = await OpenConnection();
        return await CountryRepository.GetUserArtistsForCountry(userId, countryCode, connection);
    }

    public async Task<ICollection<WhoKnowsObjectWithUser>> GetGuildUsersForCountry(
        int guildId,
        string countryCode,
        IDictionary<int, FullGuildUser> guildUsers)
    {
        await using var connection = await OpenConnection();
        return await CountryRepository.GetGuildUsersForCountry(guildId, countryCode, guildUsers, connection);
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

        await using var connection = await OpenConnection();
        var countryMappings = await CountryRepository.GetCountryMappingsForArtists(artistNames, connection);

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

        await using var connection = await OpenConnection();
        var countryMappings = await CountryRepository.GetCountryMappingsForArtists(artistNames, connection);

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

        await using var connection = await OpenConnection();
        var countryMappings = await CountryRepository.GetCountryMappingsForArtists(artistNames, connection);

        return countryMappings
            .GroupBy(g => g.CountryCode, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(o => o.Count())
            .Where(w => w.Key != null)
            .Select(s => s.Key)
            .ToList();
    }
}
