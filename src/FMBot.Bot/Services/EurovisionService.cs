using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services;

public class EurovisionService
{
    private readonly HttpClient _httpClient;

    public EurovisionService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    private class DotToNullIntConverter : CsvHelper.TypeConversion.ITypeConverter
    {
        public object ConvertFromString(string text, IReaderRow row, MemberMapData memberMapData)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Contains('.'))
            {
                return null;
            }

            if (int.TryParse(text, out var intValue))
            {
                return intValue;
            }

            throw new Exception($"Cannot convert {text} to int.");
        }

        public string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
        {
            return value?.ToString() ?? string.Empty;
        }
    }

    public async Task UpdateEurovisionData()
    {
        var allContestants = new List<EurovisionContestantModel>();

        allContestants.AddRange(await EurovisionCsvToModel("1956-2022"));
        allContestants.AddRange(await EurovisionCsvToModel("2023-2024"));

        foreach (var contestant in allContestants.OrderBy(o => o.Performer))
        {
            var key = GetEurovisionContestantEntryKey(contestant.Performer, contestant.Song);
            PublicProperties.EurovisionContestants.TryRemove(key, out _);
            PublicProperties.EurovisionContestants.TryAdd(key, contestant);
        }
        foreach (var year in allContestants.GroupBy(g => g.Year))
        {
            var countries = year.GroupBy(g => g.ToCountryId);
            var contestants = countries.Select(s => s.OrderByDescending(o => o.RunningSf.HasValue).First());

            PublicProperties.EurovisionYears.TryRemove(year.Key, out _);
            PublicProperties.EurovisionYears.TryAdd(year.Key, contestants.ToList());
        }
    }

    public static EurovisionContestantModel GetEurovisionEntry(string artistName, string trackName)
    {
        var key = GetEurovisionContestantEntryKey(artistName, trackName);
        return PublicProperties.EurovisionContestants.GetValueOrDefault(key);
    }

    public static (string full, string oneline) GetEurovisionDescription(EurovisionContestantModel eurovisionEntry)
    {
        var full = new StringBuilder();
        var oneLine = new StringBuilder();
        
        full.Append(
            $"- **{eurovisionEntry.Year}** entry for **{eurovisionEntry.ToCountry}** :flag_{eurovisionEntry.ToCountryId}:");
        full.AppendLine();

        oneLine.Append($"Eurovision {eurovisionEntry.Year} for {eurovisionEntry.ToCountry} {IsoCountryCodeToFlagEmoji(eurovisionEntry.ToCountryId)}");
        
        if (eurovisionEntry.SfNum.HasValue && !eurovisionEntry.PointsFinal.HasValue)
        {
            full.Append(
                $"- Playing in the **{eurovisionEntry.SfNum}{StringExtensions.GetAmountEnd((int)eurovisionEntry.SfNum.Value)} semi-final** - **{eurovisionEntry.RunningSf}{StringExtensions.GetAmountEnd((int)eurovisionEntry.RunningSf.GetValueOrDefault())} running position**");
            oneLine.Append($" - {eurovisionEntry.SfNum}{StringExtensions.GetAmountEnd((int)eurovisionEntry.SfNum.Value)} semi-final");
        }
        else if (!eurovisionEntry.PointsFinal.HasValue && eurovisionEntry.RunningFinal.HasValue)
        {
            full.Append($"- Playing in the finals - **{eurovisionEntry.RunningFinal}{StringExtensions.GetAmountEnd((int)eurovisionEntry.RunningFinal.GetValueOrDefault())} running position**");
            oneLine.Append($" - Finals");
        }
        else if (eurovisionEntry.PlaceContest.HasValue)
        {
            full.Append(
                $"- Got **{eurovisionEntry.PlaceContest}{StringExtensions.GetAmountEnd(eurovisionEntry.PlaceContest.GetValueOrDefault())} place**");
            if (eurovisionEntry.PointsFinal.HasValue)
            {
                full.Append($" with **{eurovisionEntry.PointsFinal} points**");
            }
            oneLine.Append($" - #{eurovisionEntry.PlaceContest}");
        }
        
        return (full.ToString(), oneLine.ToString());
    }

    public static string IsoCountryCodeToFlagEmoji(string country)
    {
        return string.Concat(country.ToUpper().Select(x => char.ConvertFromUtf32(x + 0x1F1A5)));
    }

    private static string GetEurovisionContestantEntryKey(string artistName, string trackName)
    {
        // todo: no hardcoded redirects pls
        if (artistName.Contains("joost", StringComparison.OrdinalIgnoreCase))
        {
            artistName = "joost";
        }
        return $"{artistName.ToLower()}--{trackName.ToLower()}";
    }


    public async Task<List<EurovisionContestantModel>> EurovisionCsvToModel(string years)
    {
        await using var stream = await this._httpClient.GetStreamAsync($"https://fmbot.xyz/bot-assets/eurovision/contestants-{years}.csv");

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
        };

        using var innerCsvStreamReader = new StreamReader(stream);

        var csv = new CsvReader(innerCsvStreamReader, csvConfig);
        csv.Context.TypeConverterCache.AddConverter<int?>(new DotToNullIntConverter());

        var records = csv.GetRecords<EurovisionContestantModel>();

        return records.ToList();
    }
}
