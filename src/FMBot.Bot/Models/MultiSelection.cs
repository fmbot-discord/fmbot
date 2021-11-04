using System;
using System.Collections.Generic;
using Discord;
using Fergun.Interactive;
using Fergun.Interactive.Selection;

namespace FMBot.Bot.Models
{
    public class MultiSelection<T> : BaseSelection<MultiSelectionOption<T>>
    {
        public MultiSelection(Func<MultiSelectionOption<T>, IEmote> emoteConverter, Func<MultiSelectionOption<T>, string> stringConverter,
            IEqualityComparer<MultiSelectionOption<T>> equalityComparer, bool allowCancel, Page selectionPage, IReadOnlyCollection<IUser> users,
            IReadOnlyCollection<MultiSelectionOption<T>> options, Page canceledPage, Page timeoutPage, Page successPage, DeletionOptions deletion,
            InputType inputType, ActionOnStop actionOnCancellation, ActionOnStop actionOnTimeout, ActionOnStop actionOnSuccess)
            : base(emoteConverter, stringConverter, equalityComparer, allowCancel, selectionPage, users, options, canceledPage,
                  timeoutPage, successPage, deletion, inputType, actionOnCancellation, actionOnTimeout, actionOnSuccess)
        {
        }

        public override MessageComponent BuildComponents(bool disableAll)
        {
            var builder = new ComponentBuilder();
            var selectMenus = new Dictionary<int, SelectMenuBuilder>();

            foreach (var option in Options)
            {
                if (!selectMenus.ContainsKey(option.Row))
                {
                    selectMenus[option.Row] = new SelectMenuBuilder()
                        .WithCustomId($"selectmenu{option.Row}")
                        .WithDisabled(disableAll);
                }

                var optionBuilder = new SelectMenuOptionBuilder()
                    .WithLabel(option.Option)
                    .WithValue(option.Value)
                    .WithDescription(option.Description)
                    .WithDefault(option.IsDefault);

                selectMenus[option.Row].AddOption(optionBuilder);
            }

            foreach ((int row, var selectMenu) in selectMenus)
            {
                builder.WithSelectMenu(selectMenu, row);
            }

            return builder.Build();
        }
    }
}
