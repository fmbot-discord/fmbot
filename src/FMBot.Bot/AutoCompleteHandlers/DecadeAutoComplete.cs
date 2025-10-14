using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class DecadeAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
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

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            results
                .ReplaceOrAddToList(this._allPossibleCombinations.Take(12).ToList());
        }
        else
        {
            var searchValue = option.Value;

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6));

            results.ReplaceOrAddToList(this._allPossibleCombinations
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(5));
        }

        return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
            new ApplicationCommandOptionChoiceProperties(s, s)));
    }
}
