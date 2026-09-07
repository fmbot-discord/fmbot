using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FMBot.Bot.Attributes;
using FMBot.Bot.Models;
using FMBot.Bot.SlashCommands;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using Serilog;

namespace FMBot.Bot.Services;

public class HelpService
{
    private readonly CommandService<CommandContext> _commandService;
    private readonly Lazy<IReadOnlyList<HelpCommandEntry>> _commands;

    private static readonly Dictionary<string, string> SlashToTextOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        { "chart albums", "chart" },
        { "chart artists", "artistchart" },
        { "fwkalbum", "friendwhoknowsalbum" },
        { "fwkgenre", "friendwhoknowgenre" },
        { "fwktrack", "friendwhoknowstrack" },
        { "gwkalbum", "globalwhoknowsalbum" },
        { "gwktrack", "globalwhoknowstrack" },
        { "like", "rl" },
        { "mode", "fmmode" }
    };

    private static readonly HashSet<string> HiddenInSlashMode = new(StringComparer.OrdinalIgnoreCase)
    {
        "covermode", "graphmode", "responsemode", "spotifyalbum", "spotifyartist", "playleaderboard", "timeleaderboard",
        "artistgaps", "albumgaps", "trackgaps"
    };

    private static readonly HashSet<string> HiddenInTextMode = new(StringComparer.OrdinalIgnoreCase)
    {
        "gaps"
    };

    private static readonly HashSet<string> HiddenAlways = new(StringComparer.OrdinalIgnoreCase)
    {
        "source"
    };

    private static readonly Dictionary<string, CommandCategory[]> SlashOnlyCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        { "gaps", [CommandCategory.Artists, CommandCategory.Albums, CommandCategory.Tracks] },
        { "giftsupporter", [CommandCategory.Other] },
        { "import manage", [CommandCategory.Importing] },
        { "localization", [CommandCategory.UserSettings] }
    };

    public static readonly IReadOnlyList<HelpCategoryInfo> Categories =
    [
        new() { Category = CommandCategory.WhoKnows, Emoji = "👥", DocsUrl = "https://fm.bot/commands/artists/#whoknows-wk-w" },
        new() { Category = CommandCategory.Artists, Emoji = "🎤", DocsUrl = "https://fm.bot/commands/artists/" },
        new() { Category = CommandCategory.Albums, Emoji = "💿", DocsUrl = "https://fm.bot/commands/albums/" },
        new() { Category = CommandCategory.Tracks, Emoji = "🎵", DocsUrl = "https://fm.bot/commands/tracks/" },
        new() { Category = CommandCategory.Charts, Emoji = "🖼️", DocsUrl = "https://fm.bot/commands/albums/#chart-c" },
        new() { Category = CommandCategory.Genres, Emoji = "🏷️", DocsUrl = "https://fm.bot/commands/genres/" },
        new() { Category = CommandCategory.Crowns, Emoji = "👑", DocsUrl = "https://fm.bot/commands/crowns/" },
        new() { Category = CommandCategory.Friends, Emoji = "🫂", DocsUrl = "https://fm.bot/commands/friends/" },
        new() { Category = CommandCategory.Games, Emoji = "🎮", DocsUrl = "https://fm.bot/commands/games/" },
        new() { Category = CommandCategory.Importing, Emoji = "📥", DocsUrl = "https://fm.bot/importing/" },
        new() { Category = CommandCategory.ThirdParty, Emoji = "🔗", DocsUrl = "https://fm.bot/commands/spotify/" },
        new() { Category = CommandCategory.UserSettings, Emoji = "⚙️", DocsUrl = "https://fm.bot/commands/" },
        new() { Category = CommandCategory.ServerSettings, Emoji = "🛠️", DocsUrl = "https://fm.bot/guildsettings/" },
        new() { Category = CommandCategory.Other, Emoji = "📦", DocsUrl = "https://fm.bot/commands/misc/" }
    ];

    public static readonly IReadOnlyList<HelpFeature> Features =
    [
        new() { LocalizationKey = "help.feature.nowPlaying", TextCommands = ["fm"], SlashCommands = ["fm"] },
        new() { LocalizationKey = "help.feature.topLists", TextCommands = ["topartists", "topalbums", "toptracks"], SlashCommands = ["top artists", "top albums", "top tracks"] },
        new() { LocalizationKey = "help.feature.whoKnows", TextCommands = ["whoknows", "wkalbum", "wktrack"], SlashCommands = ["wk", "wkalbum", "wktrack"] },
        new() { LocalizationKey = "help.feature.charts", TextCommands = ["chart", "artistchart"], SlashCommands = ["chart albums", "chart artists"] },
        new() { LocalizationKey = "help.feature.overview", TextCommands = ["recap", "overview", "profile"], SlashCommands = ["recap", "overview", "profile"] },
        new() { LocalizationKey = "help.feature.social", TextCommands = ["crowns", "friends", "taste"], SlashCommands = ["crowns", "friendsfm", "taste"] },
        new() { LocalizationKey = "help.feature.games", TextCommands = ["jumble", "pixel"], SlashCommands = [] },
        new() { LocalizationKey = "help.feature.importing", TextCommands = ["import"], SlashCommands = ["import spotify", "import applemusic"] }
    ];

    public HelpService(CommandService<CommandContext> commandService)
    {
        this._commandService = commandService;
        this._commands = new Lazy<IReadOnlyList<HelpCommandEntry>>(BuildCatalog);
    }

    public IReadOnlyList<HelpCommandEntry> Commands => this._commands.Value;

    public IReadOnlyList<string> UnmappedSlashCommands
    {
        get
        {
            _ = this._commands.Value;
            return this._unmappedSlashCommands;
        }
    }

    private readonly List<string> _unmappedSlashCommands = [];

    public static HelpCategoryInfo GetCategoryInfo(CommandCategory category)
    {
        return Categories.FirstOrDefault(c => c.Category == category) ?? Categories[^1];
    }

    public static string SlashMention(string slashName)
    {
        var topLevel = slashName.Split(' ')[0];
        return PublicProperties.SlashCommands.TryGetValue(topLevel, out var id)
            ? $"</{slashName}:{id}>"
            : $"`/{slashName}`";
    }

    public HelpCommandEntry FindCommand(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        search = search.Trim().TrimStart('/', '.', '!').Trim();
        if (search.Length == 0)
        {
            return null;
        }

        return this.Commands.FirstOrDefault(c => c.Name.Equals(search, StringComparison.OrdinalIgnoreCase))
               ?? this.Commands.FirstOrDefault(c => c.MatchesSearch(search));
    }

    public static bool TryFindCategory(string search, out CommandCategory category)
    {
        category = CommandCategory.Other;
        if (string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        var collapsed = search.Replace(" ", "").Replace("&", "");
        foreach (var info in Categories)
        {
            if (info.Category.ToString().Equals(collapsed, StringComparison.OrdinalIgnoreCase))
            {
                category = info.Category;
                return true;
            }
        }

        return false;
    }

    public static bool IsVisible(HelpCommandEntry entry, HelpMode mode)
    {
        if (HiddenAlways.Contains(entry.Name))
        {
            return false;
        }

        return mode == HelpMode.Text
            ? !HiddenInTextMode.Contains(entry.Name)
            : !HiddenInSlashMode.Contains(entry.Name);
    }

    public IReadOnlyList<HelpCommandEntry> GetCategoryCommands(CommandCategory category, HelpMode mode)
    {
        var commands = this.Commands.Where(c => c.Categories.Contains(category) && IsVisible(c, mode));

        if (mode == HelpMode.Slash)
        {
            commands = commands.Where(c => c.HasSlash || !c.Categories.Contains(CommandCategory.ServerSettings));
        }

        if (category == CommandCategory.ServerSettings)
        {
            commands = commands.Where(c => !c.Categories.Contains(CommandCategory.Crowns));
        }

        return commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IEnumerable<string> SearchNames(string search, int limit)
    {
        var names = this.Commands
            .SelectMany(c => c.HasText ? new[] { c.Name } : c.SlashCommands.Select(s => s.Name).ToArray())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(search))
        {
            return Features.SelectMany(f => f.TextCommands).Take(limit);
        }

        var aliasMatches = this.Commands
            .Where(c => c.MatchesSearch(search))
            .Select(c => c.HasText ? c.Name : c.SlashCommands[0].Name);

        return aliasMatches
            .Concat(names.Where(n => n.StartsWith(search, StringComparison.OrdinalIgnoreCase)))
            .Concat(names.Where(n => n.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit);
    }

    private IReadOnlyList<HelpCommandEntry> BuildCatalog()
    {
        var entries = new List<HelpCommandEntry>();

        var textCommands = this._commandService.GetCommands()
            .SelectMany(kvp => kvp.Value)
            .Distinct()
            .ToList();

        foreach (var command in textCommands)
        {
            var attributes = command.Attributes.Values.SelectMany(x => x).ToList();
            if (attributes.OfType<ExcludeFromHelp>().Any())
            {
                continue;
            }

            var commandAttribute = attributes.OfType<CommandAttribute>().FirstOrDefault();
            if (commandAttribute == null || commandAttribute.Aliases.Length == 0)
            {
                continue;
            }

            var categories = attributes.OfType<CommandCategoriesAttribute>()
                .SelectMany(c => c.Categories)
                .Distinct()
                .ToList();
            if (categories.Count == 0)
            {
                continue;
            }

            entries.Add(new HelpCommandEntry
            {
                Name = commandAttribute.Aliases[0],
                Aliases = commandAttribute.Aliases.Skip(1).ToList(),
                Summary = attributes.OfType<SummaryAttribute>().FirstOrDefault()?.Summary,
                Categories = categories,
                Options = attributes.OfType<OptionsAttribute>().FirstOrDefault()?.Options ?? [],
                Examples = attributes.OfType<ExamplesAttribute>().FirstOrDefault()?.Examples ?? [],
                SupporterEnhanced = attributes.OfType<SupporterEnhancedAttribute>().FirstOrDefault()?.Explainer,
                SupporterExclusive = attributes.OfType<SupporterExclusiveAttribute>().FirstOrDefault()?.Explainer,
                ServerStaffOnly = attributes.OfType<ServerStaffOnly>().Any(),
                TextCommand = command
            });
        }

        foreach (var slashCommand in ReflectSlashCommands())
        {
            var key = SlashToTextOverrides.TryGetValue(slashCommand.Name, out var mapped)
                ? mapped
                : slashCommand.Name.Replace(" ", "");

            var standalone = SlashOnlyCategories.TryGetValue(slashCommand.Name, out var slashCategories);

            var entry = standalone
                ? null
                : entries.FirstOrDefault(e => e.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                  ?? entries.FirstOrDefault(e => e.Aliases.Any(a => a.Equals(key, StringComparison.OrdinalIgnoreCase)));

            if (entry != null)
            {
                entry.SlashCommands.Add(slashCommand);
                continue;
            }

            if (!standalone)
            {
                Log.Warning("Help catalog: slash command /{SlashCommand} has no text equivalent and no category, skipping", slashCommand.Name);
                this._unmappedSlashCommands.Add(slashCommand.Name);
                continue;
            }

            var slashOnly = new HelpCommandEntry
            {
                Name = slashCommand.Name,
                Summary = slashCommand.Description,
                Categories = slashCategories
            };
            slashOnly.SlashCommands.Add(slashCommand);
            entries.Add(slashOnly);
        }

        Log.Information("Help catalog built with {CommandCount} commands ({SlashCount} with slash equivalents)",
            entries.Count, entries.Count(e => e.HasSlash));

        return entries;
    }

    private static IEnumerable<HelpSlashCommand> ReflectSlashCommands()
    {
        var moduleTypes = typeof(StaticSlashCommands).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, BaseType.IsGenericType: true } &&
                        t.BaseType.GetGenericTypeDefinition() == typeof(ApplicationCommandModule<>));

        foreach (var moduleType in moduleTypes)
        {
            var group = moduleType.GetCustomAttribute<SlashCommandAttribute>();

            foreach (var method in moduleType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                string name;
                string description;

                var sub = method.GetCustomAttribute<SubSlashCommandAttribute>();
                var top = method.GetCustomAttribute<SlashCommandAttribute>();

                if (group != null && sub != null)
                {
                    name = $"{group.Name} {sub.Name}";
                    description = sub.Description;
                }
                else if (top != null)
                {
                    name = top.Name;
                    description = top.Description;
                }
                else
                {
                    continue;
                }

                var parameters = method.GetParameters()
                    .Select(p =>
                    {
                        var attribute = p.GetCustomAttribute<SlashCommandParameterAttribute>();
                        return new HelpSlashParameter
                        {
                            Name = attribute?.Name ?? p.Name,
                            Description = attribute?.Description
                        };
                    })
                    .ToList();

                yield return new HelpSlashCommand
                {
                    Name = name,
                    TopLevelName = name.Split(' ')[0],
                    Description = description,
                    Parameters = parameters
                };
            }
        }
    }
}
