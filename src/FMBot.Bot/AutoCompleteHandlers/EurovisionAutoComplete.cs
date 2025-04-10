using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;

namespace FMBot.Bot.AutoCompleteHandlers;

public class EurovisionAutoComplete : AutocompleteHandler
{
    private readonly List<string> _allPossibleCombinations;
    public EurovisionAutoComplete()
    {
        this._allPossibleCombinations = new List<string>();

        var countries = new[]
        {
            "Albania", "Andorra", "Armenia", "Australia", "Austria", "Azerbaijan",
            "Belarus", "Belgium", "Bosnia and Herzegovina", "Bulgaria", "Croatia",
            "Cyprus", "Czech Republic", "Denmark", "Estonia", "Finland", "France",
            "Georgia", "Germany", "Greece", "Hungary", "Iceland", "Ireland", "Israel",
            "Italy", "Latvia", "Lithuania", "Luxembourg", "Malta", "Moldova",
            "Monaco", "Montenegro", "Morocco", "Netherlands", "North Macedonia",
            "Norway", "Poland", "Portugal", "Romania", "Russia", "San Marino",
            "Serbia", "Serbia and Montenegro", "Slovakia", "Slovenia", "Spain",
            "Sweden", "Switzerland", "Turkey", "Ukraine", "United Kingdom",
            "Yugoslavia"
        };

        this._allPossibleCombinations.AddRange(countries);
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var results = new List<string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            results
                .ReplaceOrAddToList(this._allPossibleCombinations.Take(10).ToList());
        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6));

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(5));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
