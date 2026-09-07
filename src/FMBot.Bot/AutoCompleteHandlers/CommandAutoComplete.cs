using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class CommandAutoComplete(HelpService helpService) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var results = helpService.SearchNames(option.Value, 25)
            .Select(name => new ApplicationCommandOptionChoiceProperties(name, name))
            .ToList();

        return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>>(results);
    }
}
