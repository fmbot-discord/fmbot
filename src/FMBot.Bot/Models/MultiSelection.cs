using System.Collections.Generic;
using System.Linq;

using Fergun.Interactive.Selection;
using NetCord.Rest;

namespace FMBot.Bot.Models;

public class MultiSelection<T> : BaseSelection<MultiSelectionOption<T>>
{
    public MultiSelection(MultiSelectionBuilder<T> builder)
        : base(builder)
    {
    }

    public override List<IMessageComponentProperties> GetOrAddComponents(bool disableAll, List<IMessageComponentProperties> builder = null)
    {
        builder ??= new List<IMessageComponentProperties>();
        var selectMenus = new Dictionary<int, StringMenuProperties>();

        foreach (var option in Options)
        {
            if (!selectMenus.ContainsKey(option.Row))
            {
                selectMenus[option.Row] = new StringMenuProperties($"selectmenu{option.Row}")
                {
                    Disabled = disableAll
                };
            }

            var optionBuilder = new StringMenuSelectOptionProperties(option.Option, option.Value)
            {
                Description = option.Description,
                Default = option.IsDefault
            };

            selectMenus[option.Row].Add(optionBuilder);
        }

        foreach (var selectMenu in selectMenus.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value))
        {
            builder.Add(selectMenu);
        }

        return builder;
    }
}
