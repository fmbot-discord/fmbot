using System;
using System.Collections.Generic;

using Fergun.Interactive.Selection;
using NetCord.Rest;

namespace FMBot.Bot.Models;

public class MultiSelection<T> : BaseSelection<MultiSelectionOption<T>>
{
    public MultiSelection(MultiSelectionBuilder<T> builder)
        : base(builder)
    {
    }

    public override ComponentBuilder GetOrAddComponents(bool disableAll, ComponentBuilder builder = null)
    {
        builder ??= new ActionRowProperties();
        var selectMenus = new Dictionary<int, SelectMenuBuilder>();

        foreach (var option in Options)
        {
            if (!selectMenus.ContainsKey(option.Row))
            {
                selectMenus[option.Row] = new StringMenuProperties($"selectmenu{option.Row}")
                    .WithDisabled(disableAll);
            }

            var optionBuilder = new StringMenuSelectOptionProperties(option.Option, option.Value)
                .WithDescription(option.Description)
                .WithDefault(option.IsDefault);

            selectMenus[option.Row].AddOption(optionBuilder);
        }

        foreach ((int row, var selectMenu) in selectMenus)
        {
            builder.WithSelectMenu(selectMenu, row);
        }

        return builder;
    }
}
