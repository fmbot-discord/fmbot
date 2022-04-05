using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Fergun.Interactive;
using Fergun.Interactive.Extensions;
using Fergun.Interactive.Pagination;
using Fergun.Interactive.Selection;

namespace FMBot.Bot.Models;

public class PagedSelectionBuilder<TOption> : BaseSelectionBuilder<PagedSelection<TOption>,
    KeyValuePair<TOption, Paginator>, PagedSelectionBuilder<TOption>>
{
    /// <summary>
    ///     Gets a dictionary of options and their paginators.
    /// </summary>
    public new IDictionary<TOption, Paginator> Options { get; set; } = new Dictionary<TOption, Paginator>();

    public override Func<KeyValuePair<TOption, Paginator>, string> StringConverter { get; set; } =
        option => option.Key?.ToString();

    public PagedSelection<TOption> Build(PageBuilder startPage)
    {
        this.SelectionPage = startPage;
        return Build();
    }

    /// <inheritdoc />
    public override PagedSelection<TOption> Build()
    {
        base.Options = this.Options;
        return new PagedSelection<TOption>(this);
    }

    public PagedSelectionBuilder<TOption> WithOptions<TPaginator>(IDictionary<TOption, TPaginator> options)
        where TPaginator : Paginator
    {
        this.Options = options as IDictionary<TOption, Paginator> ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    public PagedSelectionBuilder<TOption> AddOption(TOption option, Paginator paginator)
    {
        this.Options.Add(option, paginator);
        return this;
    }
}

public class PagedSelection<TOption> : BaseSelection<KeyValuePair<TOption, Paginator>>
{
    /// <inheritdoc />
    public PagedSelection(PagedSelectionBuilder<TOption> builder) : base(builder)
    {
        this.Options = new ReadOnlyDictionary<TOption, Paginator>(builder.Options);
        this.CurrentOption = this.Options.Keys.First();
    }

    /// <summary>
    ///     Gets a dictionary of options and their paginators.
    /// </summary>
    public new IReadOnlyDictionary<TOption, Paginator> Options { get; }

    /// <summary>
    ///     Gets the current option.
    /// </summary>
    public TOption CurrentOption { get; private set; }

    public override ComponentBuilder GetOrAddComponents(bool disableAll, ComponentBuilder builder = null)
    {
        builder ??= new ComponentBuilder();
        var paginator = this.Options[this.CurrentOption];

        // add paginator components to the builder
        paginator.GetOrAddComponents(disableAll, builder);

        // select menu
        var options = new List<SelectMenuOptionBuilder>();

        foreach (var selection in this.Options)
        {
            var emote = this.EmoteConverter?.Invoke(selection);
            var label = this.StringConverter?.Invoke(selection);
            if (emote is null && label is null)
                throw new InvalidOperationException(
                    $"Neither {nameof(this.EmoteConverter)} nor {nameof(this.StringConverter)} returned a valid emote or string.");

            var option = new SelectMenuOptionBuilder()
                .WithLabel(label)
                .WithEmote(emote)
                .WithDefault(Equals(selection.Key, this.CurrentOption))
                .WithValue(emote?.ToString() ?? label);

            options.Add(option);
        }

        var selectMenu = new SelectMenuBuilder()
            .WithCustomId("foobar")
            .WithOptions(options)
            .WithDisabled(disableAll);

        builder.WithSelectMenu(selectMenu);

        return builder;
    }

    public override async Task<InteractiveInputResult<KeyValuePair<TOption, Paginator>>> HandleInteractionAsync(
        SocketMessageComponent input, IUserMessage message)
    {
        if (input.Message.Id != message.Id || !this.CanInteract(input.User)) return InteractiveInputStatus.Ignored;

        var option = input.Data.Values?.FirstOrDefault();

        if (input.Data.Type == ComponentType.SelectMenu && option is not null)
        {
            KeyValuePair<TOption, Paginator> selected = default;
            string selectedString = null;

            foreach (var value in this.Options)
            {
                var stringValue = this.EmoteConverter?.Invoke(value)?.ToString() ?? this.StringConverter?.Invoke(value);
                if (option != stringValue) continue;
                selected = value;
                selectedString = stringValue;
                break;
            }

            if (selectedString is null) return InteractiveInputStatus.Ignored;

            this.CurrentOption = selected.Key;

            var isCanceled = this.AllowCancel &&
                             (this.EmoteConverter?.Invoke(this.CancelOption)?.ToString() ??
                              this.StringConverter?.Invoke(this.CancelOption)) == selectedString;

            if (isCanceled)
                return new InteractiveInputResult<KeyValuePair<TOption, Paginator>>(InteractiveInputStatus.Canceled,
                    selected);
        }

        var paginator = this.Options[this.CurrentOption];
        var (emote, action) = paginator.Emotes.FirstOrDefault(x => x.Key.ToString() == input.Data.CustomId);

        if (emote is not null)
        {
            if (action == PaginatorAction.Exit) return InteractiveInputStatus.Canceled;

            await paginator.ApplyActionAsync(action).ConfigureAwait(false);
        }

        var currentPage = await paginator.GetOrLoadCurrentPageAsync().ConfigureAwait(false);

        await input.UpdateAsync(x =>
        {
            x.Content = currentPage.Text ?? "";
            x.Embeds = currentPage.GetEmbedArray();
            x.Components = GetOrAddComponents(false).Build();
        }).ConfigureAwait(false);

        return InteractiveInputStatus.Ignored;
    }
}
