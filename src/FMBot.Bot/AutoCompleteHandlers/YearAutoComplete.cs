using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class YearAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly List<string> _allPossibleCombinations;
    public YearAutoComplete()
    {
        this._allPossibleCombinations = new List<string>();

        for (var i = DateTime.UtcNow.Year; i >= 1900; i--)
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
                .ReplaceOrAddToList(this._allPossibleCombinations.Take(10).ToList());
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
