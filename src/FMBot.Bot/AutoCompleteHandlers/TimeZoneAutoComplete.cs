using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;

namespace FMBot.Bot.AutoCompleteHandlers;

public class TimeZoneAutoComplete : AutocompleteHandler
{
    private readonly Dictionary<string, string> _allTimeZones;
    private readonly List<CountryTimezoneInfo> _storedTimeZones;
    public TimeZoneAutoComplete()
    {
        this._allTimeZones = TimeZoneInfo
            .GetSystemTimeZones()
            .ToDictionary(d => d.Id, d => d.DisplayName);

        var timeZoneJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "timezones.json");
        var timeZoneJson = File.ReadAllBytes(timeZoneJsonPath);
        this._storedTimeZones = JsonSerializer.Deserialize<List<CountryTimezoneInfo>>(timeZoneJson, new JsonSerializerOptions
        {
            AllowTrailingCommas = true
        });
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var results = new Dictionary<string, string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            results.ReplaceOrAddToDictionary(new Dictionary<string, string>()
            {
                { "null", "Start typing to search for more timezones" }
            });

            if (autocompleteInteraction?.UserLocale != null)
            {
                var userLocale = autocompleteInteraction.UserLocale[Math.Max(0, autocompleteInteraction.UserLocale.Length - 2)..];
                var guildLocale = autocompleteInteraction.GuildLocale[Math.Max(0, autocompleteInteraction.UserLocale.Length - 2)..];

                var foundCountries =
                    this._storedTimeZones
                        .Where(w => w.IsoAlpha2.Equals(userLocale, StringComparison.OrdinalIgnoreCase) ||
                                    w.IsoAlpha2.Equals(guildLocale, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                if (foundCountries.Any())
                {
                    foreach (var foundCountry in foundCountries)
                    {
                        results.ReplaceOrAddToDictionary(foundCountry.WindowsTimeZones.ToDictionary(d => d.Id, d => d.Name));
                    }
                }
            }
        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();

            results.ReplaceOrAddToDictionary(this._allTimeZones
                .Where(w => w.Value.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .ToDictionary(d => d.Key, d => d.Value));

            results.ReplaceOrAddToDictionary(this._allTimeZones
                .Where(w => w.Value.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToDictionary(d => d.Key, d => d.Value));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s.Value, s.Key))));
    }
}
