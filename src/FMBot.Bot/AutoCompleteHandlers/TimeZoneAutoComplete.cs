using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class TimeZoneAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
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

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var results = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            results.ReplaceOrAddToDictionary(new Dictionary<string, string>()
            {
                { "null", "Start typing to search for more timezones" }
            });

            if (context?.User?.Locale != null && context.Guild?.PreferredLocale != null)
            {
                var userLocale = context.User.Locale[Math.Max(0, context.User.Locale.Length - 2)..];
                var guildLocale = context.Guild.PreferredLocale[Math.Max(0, context.Guild.PreferredLocale.Length - 2)..];

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
            var searchValue = option.Value;

            results.ReplaceOrAddToDictionary(this._allTimeZones
                .Where(w => w.Value.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .ToDictionary(d => d.Key, d => d.Value));

            results.ReplaceOrAddToDictionary(this._allTimeZones
                .Where(w => w.Value.Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                            w.Key.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToDictionary(d => d.Key, d => d.Value));
        }

        return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
            new ApplicationCommandOptionChoiceProperties(s.Value, s.Key)));
    }
}
