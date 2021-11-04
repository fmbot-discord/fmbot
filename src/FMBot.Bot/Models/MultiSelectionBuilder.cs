using System.Linq;
using Fergun.Interactive;
using Fergun.Interactive.Selection;

namespace FMBot.Bot.Models
{
    public class MultiSelectionBuilder<T> : BaseSelectionBuilder<MultiSelection<T>, MultiSelectionOption<T>, MultiSelectionBuilder<T>>
    {
        public override InputType InputType => InputType.SelectMenus;

        public override MultiSelection<T> Build() => new(EmoteConverter, StringConverter,
            EqualityComparer, AllowCancel, SelectionPage?.Build(), Users?.ToArray(), Options?.ToArray(),
            CanceledPage?.Build(), TimeoutPage?.Build(), SuccessPage?.Build(), Deletion, InputType,
            ActionOnCancellation, ActionOnTimeout, ActionOnSuccess);
    }
}
