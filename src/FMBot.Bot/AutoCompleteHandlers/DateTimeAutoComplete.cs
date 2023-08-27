using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;

namespace FMBot.Bot.AutoCompleteHandlers;

public class DateTimeAutoComplete : AutocompleteHandler
{
    private readonly List<string> _allPossibleCombinations;
    public DateTimeAutoComplete()
    {
        this._allPossibleCombinations = new List<string>();

        this._allPossibleCombinations.AddRange(Enum.GetNames(typeof(TimePeriod)).ToList());

        this._allPossibleCombinations.AddRange(Enumerable.Range(1, 12).Select(i => new { I = i, M = DateTimeFormatInfo.CurrentInfo.GetMonthName(i) }).Select(s => s.M));

        this._allPossibleCombinations.AddRange(new List<string>
        {
            "1-day",
            "2-day",
            "3-day",
            "4-day",
            "5-day",
            "6-day",
        });

        foreach (var year in Enumerable.Range(2005, DateTime.UtcNow.Year - 2004).ToList().OrderByDescending(o => o).Select(s => s.ToString()))
        {
            this._allPossibleCombinations.Add(year);
            this._allPossibleCombinations.AddRange(Enumerable.Range(1, 12).Select(i => new { I = i, M = DateTimeFormatInfo.CurrentInfo.GetMonthName(i) }).Select(s => s.M + " " + year));
        }
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var results = new List<string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            results
                .ReplaceOrAddToList(new List<string>
                {
                    "Weekly",
                    "Monthly",
                    "Yearly",
                    DateTime.UtcNow.AddMonths(-1).ToString("MMM", CultureInfo.InvariantCulture),
                    "Alltime",
                    DateTime.UtcNow.ToString("yyyy", CultureInfo.InvariantCulture),
                    "Quarterly",
                    "Half-yearly",
                    DateTime.UtcNow.ToString("MMM", CultureInfo.InvariantCulture) + " " + DateTime.UtcNow.AddYears(-1).ToString("yyyy", CultureInfo.InvariantCulture)
                });
        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6));

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(4));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
