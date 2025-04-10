using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using Web.InternalApi;

namespace FMBot.Bot.Services;

public class EurovisionService
{
    private readonly EurovisionEnrichment.EurovisionEnrichmentClient _eurovisionEnrichment;
    private CountryService CountryService { get; set; }


    public EurovisionService(EurovisionEnrichment.EurovisionEnrichmentClient eurovisionEnrichment,
        CountryService countryService)
    {
        this._eurovisionEnrichment = eurovisionEnrichment;
        this.CountryService = countryService;
    }

    public async Task<EurovisionEntry> GetEurovisionEntryForSpotifyId(string spotifyId)
    {
        try
        {
            var entry = await this._eurovisionEnrichment.GetEntryBySpotifyIdAsync(new SpotifyIdRequest
            {
                SpotifyId = spotifyId
            });

            return entry?.Entry;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public (string full, string oneline) GetEurovisionDescription(EurovisionEntry eurovisionEntry)
    {
        var full = new StringBuilder();
        var oneLine = new StringBuilder();

        var country = this.CountryService.GetValidCountry(eurovisionEntry.EntryCode);

        full.Append(
            $"- **{eurovisionEntry.Year}** entry for **{country.Name}** {country.Emoji}");
        full.AppendLine();

        oneLine.Append(
            $"Eurovision {eurovisionEntry.Year} for {country.Name} {country.Emoji}");

        if (!eurovisionEntry.HasScore && !eurovisionEntry.ReachedFinals && eurovisionEntry.HasSemiFinalNr)
        {
            full.Append(
                $"- Playing in the **{eurovisionEntry.SemiFinalNr}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalNr)} semi-final**");
            if (eurovisionEntry.HasDraw)
            {
                full.Append(
                    $" - **{eurovisionEntry.Draw}{StringExtensions.GetAmountEnd(eurovisionEntry.Draw)} running position**");
            }

            oneLine.Append(
                $" - {eurovisionEntry.SemiFinalNr}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalNr)} semi-final");
        }
        else if (!eurovisionEntry.HasScore && eurovisionEntry.ReachedFinals)
        {
            full.Append(
                $"- Playing in the finals");
            if (eurovisionEntry.HasDraw)
            {
                full.Append(
                    $" - **{eurovisionEntry.Draw}{StringExtensions.GetAmountEnd(eurovisionEntry.Draw)} running position**");
            }

            oneLine.Append($" - Finals");
        }
        else if (eurovisionEntry.HasPosition)
        {
            full.Append(
                $"- Got **{eurovisionEntry.Position}{StringExtensions.GetAmountEnd(eurovisionEntry.Position)} place**");
            if (eurovisionEntry.HasScore)
            {
                full.Append($" with **{eurovisionEntry.Score} points**");
            }

            oneLine.Append($" - #{eurovisionEntry.Position}");
        }

        return (full.ToString(), oneLine.ToString());
    }

    public async Task<List<EurovisionEntry>> GetEntries(int year)
    {
        var entries = await this._eurovisionEnrichment.GetContestByYearAsync(new YearRequest
        {
            Year = year
        });

        return entries?.Contest?.Entries?.ToList();
    }

    public async Task<EurovisionEntry> GetEntry(int year, string countryCode)
    {
        var entry = await this._eurovisionEnrichment.GetCountryEntryByYearAsync(new CountryYearRequest
        {
            Year = year,
            CountryCode = countryCode
        });

        return entry?.Entry;
    }

    public async Task<List<EurovisionVote>> GetVotesForEntry(int year, string countryCode)
    {
        var votes = await this._eurovisionEnrichment.GetVotesForCountryByYearAsync(new VotesRequest
        {
            Year = year,
            CountryCode = countryCode
        });

        return votes?.Votes?.ToList();
    }


}
