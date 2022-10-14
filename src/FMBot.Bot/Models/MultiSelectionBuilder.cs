using System.Linq;
using Fergun.Interactive;
using Fergun.Interactive.Selection;

namespace FMBot.Bot.Models;

public class MultiSelectionBuilder<T> : BaseSelectionBuilder<MultiSelection<T>, MultiSelectionOption<T>, MultiSelectionBuilder<T>>
{
    public override InputType InputType => InputType.SelectMenus;

    public override MultiSelection<T> Build() => new(this);
}
