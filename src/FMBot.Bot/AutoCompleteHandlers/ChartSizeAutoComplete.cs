using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;

namespace FMBot.Bot.AutoCompleteHandlers;

public class ChartSizeAutoComplete : AutocompleteHandler
{
    private readonly List<string> _allPossibleCombinations;
    public ChartSizeAutoComplete()
    {
        this._allPossibleCombinations = new List<string>();

        for (var i = 1; i <= 50; i++)
        {
            for (var j = 1; j <= 50 && i * j <= 100; j++)
            {
                this._allPossibleCombinations.Add($"{i}x{j}");
            }

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
                    "3x3",
                    "4x4",
                    "5x5",
                    "8x5",
                    "10x10",
                    "4x8",
                    "15x6",
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
                .Take(5));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
