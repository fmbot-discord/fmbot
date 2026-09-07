using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class HelpBuilders(HelpService helpService)
{
    private const int MenuOptionLimit = 25;
    private const string SupportServerUrl = "https://discord.gg/fmbot";

    public ResponseModel Resolve(ContextModel context, string search, HelpMode mode)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Overview(context, mode);
        }

        var trimmed = search.Trim();
        if (!string.IsNullOrEmpty(context.Prefix) && trimmed.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[context.Prefix.Length..].Trim();
        }

        var entry = helpService.FindCommand(trimmed);
        if (entry != null)
        {
            return Command(context, entry, mode);
        }

        if (HelpService.TryFindCategory(trimmed, out var category))
        {
            return Category(context, category, mode);
        }

        if (trimmed.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return AllCommands(context, mode);
        }

        return Overview(context, mode, Truncate(trimmed, 40));
    }

    public ResponseModel Overview(ContextModel context, HelpMode mode, string notFoundSearch = null)
    {
        var response = NewResponse();
        var container = response.ComponentsContainer;

        if (notFoundSearch != null)
        {
            var example = mode == HelpMode.Slash
                ? HelpService.SlashMention("help")
                : $"`{context.Prefix}help whoknows`";
            container.WithTextDisplay("-# ⚠️ " + context.Localize("help.commandNotFound",
                ("command", notFoundSearch.Replace("`", "")), ("example", example)));
            container.WithSeparator();
        }

        var loginCommand = mode == HelpMode.Slash ? HelpService.SlashMention("login") : $"`{context.Prefix}login`";
        var accountLine = context.ContextUser == null
            ? context.Localize("help.getStarted", ("command", loginCommand))
            : context.Localize("help.loggedInAs",
                ("username", context.ContextUser.UserNameLastFM),
                ("url", LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)),
                ("command", loginCommand));

        container.WithTextDisplay($"## {context.Localize("help.overviewTitle")}\n{context.Localize("help.overviewIntro")}\n\n{accountLine}");
        container.WithSeparator();

        var features = new StringBuilder();
        features.AppendLine(context.Localize("help.popularCommands"));
        foreach (var feature in HelpService.Features)
        {
            var commands = mode == HelpMode.Slash && feature.SlashCommands.Count > 0
                ? string.Join(" ", feature.SlashCommands.Select(HelpService.SlashMention))
                : string.Join(" ", feature.TextCommands.Select(c => $"`{context.Prefix}{c}`"));

            features.AppendLine($"{commands} — {FeatureDescription(context, feature.LocalizationKey)}");
        }

        container.WithTextDisplay(features.ToString().TrimEnd());

        container.WithSeparator();
        AddUserAppNotice(container, context);
        container.WithTextDisplay(ModeHint(context, mode));

        AddNavigation(container, context, mode, HelpView.Overview, null, null);

        return response;
    }

    public ResponseModel Category(ContextModel context, CommandCategory category, HelpMode mode)
    {
        var response = NewResponse();
        var container = response.ComponentsContainer;
        var info = HelpService.GetCategoryInfo(category);

        var header = new StringBuilder();
        header.AppendLine($"## {info.Emoji} {CategoryName(context, category)}");
        header.AppendLine(CategoryDescription(context, category));
        if (category == CommandCategory.ServerSettings)
        {
            header.AppendLine(context.Localize("help.serverStaffOnly"));
        }

        var commands = helpService.GetCategoryCommands(category, mode);
        var regular = commands.Where(c => !c.Categories.Contains(CommandCategory.ServerSettings) || category == CommandCategory.ServerSettings).ToList();
        var settings = commands.Except(regular).ToList();

        if (category == CommandCategory.ServerSettings && context.DiscordGuild != null)
        {
            container.AddComponents(new ComponentSectionProperties(ServerSettingsButton(context))
            {
                Components = [new TextDisplayProperties(header.ToString().TrimEnd())]
            });
        }
        else
        {
            container.WithTextDisplay(header.ToString().TrimEnd());
        }

        container.WithSeparator();
        container.WithTextDisplay(CommandList(context, regular, mode));

        if (settings.Count > 0)
        {
            container.WithSeparator();

            var settingsHeader = $"**{CategoryName(context, CommandCategory.ServerSettings)}**\n{context.Localize("help.serverStaffOnly")}";
            if (context.DiscordGuild != null)
            {
                container.AddComponents(new ComponentSectionProperties(ServerSettingsButton(context))
                {
                    Components = [new TextDisplayProperties(settingsHeader)]
                });
                container.WithTextDisplay(CommandList(context, settings, mode));
            }
            else
            {
                container.WithTextDisplay($"{settingsHeader}\n{CommandList(context, settings, mode)}");
            }
        }

        container.WithSeparator();
        AddUserAppNotice(container, context);
        container.WithTextDisplay(ModeHint(context, mode));

        AddNavigation(container, context, mode, HelpView.Category, category, null);

        return response;
    }

    public ResponseModel Command(ContextModel context, HelpCommandEntry entry, HelpMode mode)
    {
        var response = NewResponse();
        var container = response.ComponentsContainer;
        response.CommandResponse = CommandResponse.Help;

        var showSlash = mode == HelpMode.Slash && entry.HasSlash;
        var showText = entry.HasText && !showSlash;

        var body = new StringBuilder();
        body.AppendLine(showSlash
            ? $"## /{entry.SlashCommands[0].Name}"
            : $"## {context.Prefix}{entry.Name}");

        if (showSlash)
        {
            body.AppendLine(string.Join(" ", entry.SlashCommands.Select(s => s.Mention())));
        }

        var summary = entry.HasText ? entry.Summary : entry.SlashCommands.FirstOrDefault()?.Description;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            body.AppendLine();
            body.AppendLine(summary.Replace("{{prfx}}", context.Prefix));
        }

        container.WithTextDisplay(body.ToString().TrimEnd());

        var details = new StringBuilder();

        if (showSlash)
        {
            var parameters = entry.SlashCommands
                .SelectMany(s => s.Parameters)
                .Where(p => !string.IsNullOrWhiteSpace(p.Description))
                .DistinctBy(p => p.Name)
                .GroupBy(p => p.Description)
                .ToList();
            if (parameters.Count > 0)
            {
                details.AppendLine($"**{context.Localize("help.options")}**");
                foreach (var group in parameters)
                {
                    var names = group.Select(p => p.Name).ToList();
                    var label = names.Count > 2 ? $"{names[0]} … {names[^1]}" : string.Join(", ", names);
                    details.AppendLine($"- **{label}** — {group.Key}");
                }
            }
        }
        else if (entry.Options.Count > 0)
        {
            details.AppendLine($"**{context.Localize("help.options")}**");
            foreach (var option in entry.Options)
            {
                details.AppendLine($"- {option}");
            }
        }

        if (showText && entry.Examples.Count > 0)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine($"**{context.Localize("help.examples")}**");
            foreach (var example in entry.Examples)
            {
                details.AppendLine($"`{context.Prefix}{example}`");
            }
        }

        if (showText && entry.Aliases.Count > 0)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine($"**{context.Localize("help.aliases")}**");
            details.AppendLine(AliasList(context, entry.Aliases));
        }

        if (showText && entry.HasSlash)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine($"**{context.Localize("help.slashCommand")}**");
            details.AppendLine(string.Join(" ", entry.SlashCommands.Select(s => s.Mention())));
        }

        if (showSlash && entry.HasText)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine($"**{context.Localize("help.textCommand")}**");
            details.AppendLine(AliasList(context, new[] { entry.Name }.Concat(entry.Aliases).ToList()));
        }

        if (mode == HelpMode.Slash && !entry.HasSlash)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine(context.Localize("help.textOnly"));
        }

        if (entry.ServerStaffOnly)
        {
            if (details.Length > 0)
            {
                details.AppendLine();
            }

            details.AppendLine(context.Localize("help.serverStaffOnly"));
        }

        if (details.Length > 0)
        {
            container.WithSeparator();
            container.WithTextDisplay(details.ToString().TrimEnd());
        }

        var showPurchaseButton = false;
        if (entry.SupporterEnhanced != null || entry.SupporterExclusive != null)
        {
            var supporter = new StringBuilder();
            if (entry.SupporterEnhanced != null)
            {
                supporter.AppendLine($"**{context.Localize("help.supporterEnhanced")}**");
                supporter.AppendLine(entry.SupporterEnhanced);
            }

            if (entry.SupporterExclusive != null)
            {
                if (supporter.Length > 0)
                {
                    supporter.AppendLine();
                }

                supporter.AppendLine($"**{context.Localize("help.supporterExclusive")}**");
                supporter.AppendLine(entry.SupporterExclusive);
            }

            container.WithSeparator();
            container.WithTextDisplay(supporter.ToString().TrimEnd());

            showPurchaseButton = !IsSupporter(context);
        }

        container.WithSeparator();
        AddUserAppNotice(container, context);
        AddNavigation(container, context, mode, HelpView.Command, entry.PrimaryCategory, entry,
            showPurchaseButton ? entry.Name : null);

        return response;
    }

    public ResponseModel AllCommands(ContextModel context, HelpMode mode, int page = 1)
    {
        const int pageCharacterBudget = 3300;

        var response = NewResponse();
        var container = response.ComponentsContainer;

        var blocks = new List<string>();
        foreach (var info in HelpService.Categories)
        {
            var commands = helpService.GetCategoryCommands(info.Category, mode)
                .Select(c => CommandName(context, c, mode))
                .ToList();

            if (commands.Count == 0)
            {
                continue;
            }

            blocks.Add($"**{info.Emoji} {CategoryName(context, info.Category)}**\n{string.Join(", ", commands)}");
        }

        var pages = new List<List<string>> { new() };
        foreach (var block in blocks)
        {
            var current = pages[^1];
            if (current.Count > 0 && current.Sum(b => b.Length + 2) + block.Length > pageCharacterBudget)
            {
                current = [];
                pages.Add(current);
            }

            current.Add(block);
        }

        page = Math.Clamp(page, 1, pages.Count);

        var text = new StringBuilder();
        text.AppendLine($"## {context.Localize("help.allCommands")}");
        foreach (var block in pages[page - 1])
        {
            text.AppendLine();
            text.AppendLine(block);
        }

        container.WithTextDisplay(text.ToString().TrimEnd());
        container.WithSeparator();
        AddUserAppNotice(container, context);
        container.WithTextDisplay(ModeHint(context, mode));

        AddNavigation(container, context, mode, HelpView.All, null, null, paging: (page, pages.Count));

        return response;
    }

    private static ButtonProperties ServerSettingsButton(ContextModel context)
    {
        return new ButtonProperties(InteractionConstants.Settings.ServerNew,
            context.Localize("help.openServerSettings"), ButtonStyle.Secondary)
        {
            Emoji = EmojiProperties.Standard("🛠️")
        };
    }

    private static ResponseModel NewResponse()
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };
        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        return response;
    }

    private static void AddUserAppNotice(ComponentContainerProperties container, ContextModel context)
    {
        if (!context.UserApp)
        {
            return;
        }

        container.WithTextDisplay(context.Localize("help.userAppNotice", ("url", Constants.InviteLink)));
        container.WithSeparator();
    }

    private static bool IsSupporter(ContextModel context)
    {
        return context.ContextUser != null && SupporterService.IsSupporter(context.ContextUser.UserType);
    }

    private static string CommandName(ContextModel context, HelpCommandEntry entry, HelpMode mode)
    {
        if (mode == HelpMode.Slash && entry.HasSlash || !entry.HasText)
        {
            return string.Join(" ", entry.SlashCommands.Select(s => s.Mention()));
        }

        return $"`{context.Prefix}{entry.Name}`";
    }

    private static string CommandList(ContextModel context, IEnumerable<HelpCommandEntry> entries, HelpMode mode)
    {
        var list = new StringBuilder();
        foreach (var entry in entries)
        {
            var name = CommandName(context, entry, mode);

            var summary = mode == HelpMode.Slash && entry.HasSlash
                ? entry.SlashCommands[0].Description
                : entry.ShortSummary.Replace("{{prfx}}", context.Prefix);

            list.AppendLine(string.IsNullOrWhiteSpace(summary) ? name : $"{name} — {summary}");
        }

        return list.ToString().TrimEnd();
    }

    private static string ModeHint(ContextModel context, HelpMode mode)
    {
        return mode == HelpMode.Slash
            ? context.Localize("help.slashHint")
            : context.Localize("help.commandHelpHint", ("example", $"{context.Prefix}chart help"));
    }

    private static string ModeString(HelpMode mode)
    {
        return mode == HelpMode.Slash ? "slash" : "text";
    }

    public static HelpMode ParseMode(string mode)
    {
        return mode != null && mode.Equals("slash", StringComparison.OrdinalIgnoreCase) ? HelpMode.Slash : HelpMode.Text;
    }

    private static string NavigateId(HelpMode mode, HelpView view, string argument)
    {
        return $"{InteractionConstants.Help.Navigate}:{ModeString(mode)}:{view.ToString().ToLower()}:{argument ?? "-"}";
    }

    private void AddNavigation(ComponentContainerProperties container, ContextModel context, HelpMode mode,
        HelpView view, CommandCategory? category, HelpCommandEntry command, string purchaseSource = null,
        (int Page, int Pages)? paging = null)
    {
        var categoryMenu = new StringMenuProperties($"{InteractionConstants.Help.CategoryMenu}:{ModeString(mode)}")
            .WithPlaceholder(context.Localize("help.browseCategories"));

        categoryMenu.AddOption(new StringMenuSelectOptionProperties(context.Localize("help.overview"), "overview")
        {
            Description = context.Localize("help.overviewDescription"),
            Emoji = EmojiProperties.Standard("🏠"),
            Default = view == HelpView.Overview
        });

        foreach (var info in HelpService.Categories)
        {
            categoryMenu.AddOption(new StringMenuSelectOptionProperties(CategoryName(context, info.Category), info.Category.ToString())
            {
                Description = CategoryDescription(context, info.Category),
                Emoji = EmojiProperties.Standard(info.Emoji),
                Default = view != HelpView.All && category == info.Category
            });
        }

        categoryMenu.AddOption(new StringMenuSelectOptionProperties(context.Localize("help.allCommands"), "all")
        {
            Description = context.Localize("help.allCommandsDescription"),
            Emoji = EmojiProperties.Standard("📋"),
            Default = view == HelpView.All
        });

        container.AddComponents(categoryMenu);

        if (category.HasValue)
        {
            var commands = helpService.GetCategoryCommands(category.Value, mode);
            var pages = (int)Math.Ceiling(commands.Count / (double)MenuOptionLimit);

            for (var page = 0; page < pages; page++)
            {
                var commandMenu = new StringMenuProperties(
                        $"{InteractionConstants.Help.CommandMenu}:{ModeString(mode)}:{category.Value}")
                    .WithPlaceholder(pages > 1
                        ? context.Localize("help.selectCommandPage", ("page", (page + 1).ToString()), ("pages", pages.ToString()))
                        : context.Localize("help.selectCommand"));

                foreach (var entry in commands.Skip(page * MenuOptionLimit).Take(MenuOptionLimit))
                {
                    var label = mode == HelpMode.Slash && entry.HasSlash
                        ? $"/{entry.SlashCommands[0].Name}"
                        : entry.HasText
                            ? $"{context.Prefix}{entry.Name}"
                            : $"/{entry.SlashCommands[0].Name}";

                    var description = mode == HelpMode.Slash && entry.HasSlash
                        ? entry.SlashCommands[0].Description
                        : entry.ShortSummary.Replace("{{prfx}}", context.Prefix);

                    commandMenu.AddOption(new StringMenuSelectOptionProperties(label, entry.Name)
                    {
                        Description = string.IsNullOrWhiteSpace(description) ? null : Truncate(description, 100),
                        Default = command != null && command.Name == entry.Name
                    });
                }

                container.AddComponents(commandMenu);
            }
        }

        var argument = view switch
        {
            HelpView.Category => category?.ToString(),
            HelpView.Command => command?.Name,
            HelpView.All => paging?.Page.ToString(),
            _ => null
        };

        var row = new ActionRowProperties();
        row.AddComponents(new ButtonProperties(NavigateId(HelpMode.Slash, view, argument),
            context.Localize("help.slashCommands"),
            mode == HelpMode.Slash ? ButtonStyle.Primary : ButtonStyle.Secondary));
        row.AddComponents(new ButtonProperties(NavigateId(HelpMode.Text, view, argument),
            context.Localize("help.textCommands"),
            mode == HelpMode.Text ? ButtonStyle.Primary : ButtonStyle.Secondary));
        row.AddComponents(new ButtonProperties(InteractionConstants.Faq.OverviewNew, context.Localize("help.faq"), ButtonStyle.Secondary));

        var docsUrl = category.HasValue ? HelpService.GetCategoryInfo(category.Value).DocsUrl : Constants.DocsUrl;
        row.AddComponents(new LinkButtonProperties(docsUrl, context.Localize("help.docs")));
        row.AddComponents(new LinkButtonProperties(SupportServerUrl, context.Localize("help.supportServer")));

        container.WithActionRow(row);

        if (paging is { Pages: > 1 })
        {
            var pageRow = new ActionRowProperties();
            pageRow.AddComponents(new ButtonProperties(NavigateId(mode, HelpView.All, (paging.Value.Page - 1).ToString()),
                EmojiProperties.Custom(DiscordConstants.PagesPrevious), ButtonStyle.Secondary)
            {
                Disabled = paging.Value.Page <= 1
            });
            pageRow.AddComponents(new ButtonProperties(NavigateId(mode, HelpView.All, "current"),
                context.Localize("shared.pageCounter", ("page", paging.Value.Page.ToString()), ("pages", paging.Value.Pages.ToString())),
                ButtonStyle.Secondary)
            {
                Disabled = true
            });
            pageRow.AddComponents(new ButtonProperties(NavigateId(mode, HelpView.All, (paging.Value.Page + 1).ToString()),
                EmojiProperties.Custom(DiscordConstants.PagesNext), ButtonStyle.Secondary)
            {
                Disabled = paging.Value.Page >= paging.Value.Pages
            });
            container.WithActionRow(pageRow);
        }

        if (purchaseSource != null)
        {
            container.WithActionRow(new ActionRowProperties().AddComponents(new ButtonProperties(
                InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: $"help-{purchaseSource}"),
                Constants.GetSupporterButton, ButtonStyle.Primary)
            {
                Emoji = EmojiProperties.Standard("⭐")
            }));
        }
    }

    private static string AliasList(ContextModel context, IReadOnlyList<string> aliases)
    {
        const int maxAliases = 12;
        var shown = aliases.Take(maxAliases).Select(a => $"`{context.Prefix}{a}`");
        var list = string.Join(", ", shown);
        return aliases.Count > maxAliases ? $"{list}, …" : list;
    }

    private static string Truncate(string value, int length)
    {
        return value.Length <= length ? value : value[..(length - 1)].TrimEnd() + "…";
    }

    private static string FeatureDescription(ContextModel context, string key)
    {
        return key switch
        {
            "help.feature.nowPlaying" => context.Localize("help.feature.nowPlaying"),
            "help.feature.topLists" => context.Localize("help.feature.topLists"),
            "help.feature.whoKnows" => context.Localize("help.feature.whoKnows"),
            "help.feature.charts" => context.Localize("help.feature.charts"),
            "help.feature.overview" => context.Localize("help.feature.overview"),
            "help.feature.social" => context.Localize("help.feature.social"),
            "help.feature.games" => context.Localize("help.feature.games"),
            "help.feature.importing" => context.Localize("help.feature.importing"),
            _ => key
        };
    }

    public static string CategoryName(ContextModel context, CommandCategory category)
    {
        return category switch
        {
            CommandCategory.WhoKnows => context.Localize("help.category.whoKnows"),
            CommandCategory.Artists => context.Localize("help.category.artists"),
            CommandCategory.Albums => context.Localize("help.category.albums"),
            CommandCategory.Tracks => context.Localize("help.category.tracks"),
            CommandCategory.Charts => context.Localize("help.category.charts"),
            CommandCategory.Genres => context.Localize("help.category.genres"),
            CommandCategory.Crowns => context.Localize("help.category.crowns"),
            CommandCategory.Friends => context.Localize("help.category.friends"),
            CommandCategory.Games => context.Localize("help.category.games"),
            CommandCategory.Importing => context.Localize("help.category.importing"),
            CommandCategory.ThirdParty => context.Localize("help.category.thirdParty"),
            CommandCategory.UserSettings => context.Localize("help.category.userSettings"),
            CommandCategory.ServerSettings => context.Localize("help.category.serverSettings"),
            _ => context.Localize("help.category.other")
        };
    }

    private static string CategoryDescription(ContextModel context, CommandCategory category)
    {
        return category switch
        {
            CommandCategory.WhoKnows => context.Localize("help.categoryDescription.whoKnows"),
            CommandCategory.Artists => context.Localize("help.categoryDescription.artists"),
            CommandCategory.Albums => context.Localize("help.categoryDescription.albums"),
            CommandCategory.Tracks => context.Localize("help.categoryDescription.tracks"),
            CommandCategory.Charts => context.Localize("help.categoryDescription.charts"),
            CommandCategory.Genres => context.Localize("help.categoryDescription.genres"),
            CommandCategory.Crowns => context.Localize("help.categoryDescription.crowns"),
            CommandCategory.Friends => context.Localize("help.categoryDescription.friends"),
            CommandCategory.Games => context.Localize("help.categoryDescription.games"),
            CommandCategory.Importing => context.Localize("help.categoryDescription.importing"),
            CommandCategory.ThirdParty => context.Localize("help.categoryDescription.thirdParty"),
            CommandCategory.UserSettings => context.Localize("help.categoryDescription.userSettings"),
            CommandCategory.ServerSettings => context.Localize("help.categoryDescription.serverSettings"),
            _ => context.Localize("help.categoryDescription.other")
        };
    }
}
