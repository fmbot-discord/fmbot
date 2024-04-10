using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;

namespace FMBot.Bot.AutoCompleteHandlers;

public class DecadeAutoComplete : AutocompleteHandler
{
    private readonly List<string> _allPossibleCombinations;
    public DecadeAutoComplete()
    {
        this._allPossibleCombinations = new List<string>();

        for (var i = 2020; i >= 1900; i -= 10)
        {
            this._allPossibleCombinations.Add($"{i}");
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
                .ReplaceOrAddToList(this._allPossibleCombinations.Take(12).ToList());
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
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult($"{s}s", s))));
    }
}
