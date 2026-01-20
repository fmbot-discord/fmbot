using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class EurovisionAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly List<string> _allPossibleCombinations;
    public EurovisionAutoComplete()
    {
        this._allPossibleCombinations = [];

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
