using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;

namespace FMBot.Bot.AutoCompleteHandlers;

public class TimeZoneAutoComplete : AutocompleteHandler
{
    private readonly Dictionary<string, string> _allTimeZones;
    public TimeZoneAutoComplete()
    {
        this._allTimeZones = TimeZoneInfo
            .GetSystemTimeZones()
            .ToDictionary(d => d.Id, d => d.DisplayName);
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
                    "asdasdas"
                });
        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();

            //results.ReplaceOrAddToList(this._allTimeZones
            //    .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
            //    .Take(6));

            //results.ReplaceOrAddToList(this._allTimeZones
            //    .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
            //    .Take(5));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
