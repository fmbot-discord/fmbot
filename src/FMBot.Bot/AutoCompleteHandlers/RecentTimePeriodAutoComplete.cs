using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class RecentTimePeriodAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private static List<string> GetOptions()
    {
        var options = new List<string>
        {
            "Weekly",
            "Monthly",
            "Alltime"
        };

        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var previousMonth = currentMonth.AddMonths(-1);

        options.Add(currentMonth.ToString("MMMM", CultureInfo.InvariantCulture));
        options.Add(previousMonth.Year != now.Year
            ? $"{previousMonth.ToString("MMMM", CultureInfo.InvariantCulture)} {previousMonth.Year}"
            : previousMonth.ToString("MMMM", CultureInfo.InvariantCulture));

        return options;
    }

    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var allOptions = GetOptions();
        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            results.ReplaceOrAddToList(allOptions);
        }
        else
        {
            var searchValue = option.Value;

            results.ReplaceOrAddToList(allOptions
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(6));

            results.ReplaceOrAddToList(allOptions
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(4));
        }

        return ValueTask.FromResult(
            results.Select(s => new ApplicationCommandOptionChoiceProperties(s, s)));
    }
}
