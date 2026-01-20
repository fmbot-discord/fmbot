using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class ChartSizeAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
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

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
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
