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

    public (string full, string oneline) GetEurovisionDescription(EurovisionEntry entry)
    {
        var full = new StringBuilder();
        var oneLine = new StringBuilder();

        var country = this.CountryService.GetValidCountry(entry.EntryCode);

        full.Append(
            $"- **{entry.Year}** entry for **{country.Name}** {country.Emoji}");
        full.AppendLine();

        oneLine.Append(
            $"Eurovision {entry.Year} for {country.Name} {country.Emoji}");

        if (!entry.HasScore && !entry.ReachedFinals && entry.HasSemiFinalNr)
        {
            full.Append(
                $"- Playing in the **{entry.SemiFinalNr}{StringExtensions.GetAmountEnd(entry.SemiFinalNr)} semi-final**");
            if (entry.HasSemiFinalDraw)
            {
                full.Append(
                    $" - **{entry.HasSemiFinalDraw}{StringExtensions.GetAmountEnd(entry.SemiFinalDraw)} running position**");
            }

            oneLine.Append(
                $" - {entry.SemiFinalNr}{StringExtensions.GetAmountEnd(entry.SemiFinalNr)} semi-final");
        }
        else if (!entry.HasScore && entry.ReachedFinals)
        {
            full.Append(
                $"- Playing in the Grand final");
            if (entry.HasDraw)
            {
                full.Append(
                    $" - **{entry.Draw}{StringExtensions.GetAmountEnd(entry.Draw)} running position**");
            }

            oneLine.Append($" - Grand finals");
        }
        else if (entry.HasPosition)
        {
            full.Append(
                $"- Got **{entry.Position}{StringExtensions.GetAmountEnd(entry.Position)} place**");
            if (entry.HasScore)
            {
                full.Append($" with **{entry.Score} points**");
            }
            if (entry.Position == 1)
            {
                full.Append($" 👑");
            }

            oneLine.Append($" - #{entry.Position}");
        }
        else if (entry.HasSemiFinalPosition)
        {
            full.Append(
                $"- Got **{entry.SemiFinalPosition}{StringExtensions.GetAmountEnd(entry.SemiFinalPosition)} place in semi-finals**");
            if (entry.HasSemiFinalScore)
            {
                full.Append($" with **{entry.HasSemiFinalScore} points**");
            }
        }

        return (full.ToString(), oneLine.ToString());
    }

    public async Task<EurovisionContest> GetEntries(int year)
    {
        var entries = await this._eurovisionEnrichment.GetContestByYearAsync(new YearRequest
        {
            Year = year
        });

        return entries?.Contest;
    }

    public async Task<EurovisionEntry> GetEntry(int year, string countryCode)
    {
        try
        {
            var entry = await this._eurovisionEnrichment.GetCountryEntryByYearAsync(new CountryYearRequest
            {
                Year = year,
                CountryCode = countryCode
            });

            return entry?.Entry;
        }
        catch (Exception e)
        {
            return null;
        }
    }

    public async Task<List<EurovisionVote>> GetVotesForEntry(int year, string countryCode)
    {
        try
        {
            var votes = await this._eurovisionEnrichment.GetVotesForCountryByYearAsync(new VotesRequest
            {
                Year = year,
                CountryCode = countryCode
            });

            return votes?.Votes?.ToList() ?? [];
        }
        catch (Exception e)
        {
            return [];
        }
    }
}
