using System.Collections.Generic;
using System.Linq;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord.Services.Commands;

namespace FMBot.Bot.Models;

public enum HelpMode
{
    Text,
    Slash
}

public enum HelpView
{
    Overview,
    Category,
    Command,
    All
}

public class HelpSlashParameter
{
    public string Name { get; init; }
    public string Description { get; init; }
}

public class HelpSlashCommand
{
    public string Name { get; init; }
    public string TopLevelName { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<HelpSlashParameter> Parameters { get; init; } = [];

    public string Mention()
    {
        return PublicProperties.SlashCommands.TryGetValue(this.TopLevelName, out var id)
            ? $"</{this.Name}:{id}>"
            : $"`/{this.Name}`";
    }
}

public class HelpCommandEntry
{
    public string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string Summary { get; init; }
    public IReadOnlyList<CommandCategory> Categories { get; init; } = [];
    public IReadOnlyList<string> Options { get; init; } = [];
    public IReadOnlyList<string> Examples { get; init; } = [];
    public string SupporterEnhanced { get; init; }
    public string SupporterExclusive { get; init; }
    public bool ServerStaffOnly { get; init; }
    public ICommandInfo<CommandContext> TextCommand { get; init; }
    public List<HelpSlashCommand> SlashCommands { get; } = [];

    public bool HasText => this.TextCommand != null;
    public bool HasSlash => this.SlashCommands.Count > 0;

    public CommandCategory PrimaryCategory => this.Categories.FirstOrDefault(CommandCategory.Other);

    public string ShortSummary
    {
        get
        {
            var summary = this.HasText ? this.Summary : this.SlashCommands.FirstOrDefault()?.Description;
            if (string.IsNullOrWhiteSpace(summary))
            {
                return "";
            }

            var firstLine = summary.Split('\n')[0].Trim();
            return firstLine.Length > 90 ? firstLine[..87].TrimEnd() + "…" : firstLine;
        }
    }

    public bool MatchesSearch(string search)
    {
        if (this.Name.Equals(search, System.StringComparison.OrdinalIgnoreCase) ||
            this.Aliases.Any(a => a.Equals(search, System.StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var collapsed = search.Replace(" ", "");
        return this.SlashCommands.Any(s =>
            s.Name.Equals(search, System.StringComparison.OrdinalIgnoreCase) ||
            s.Name.Replace(" ", "").Equals(collapsed, System.StringComparison.OrdinalIgnoreCase));
    }
}

public class HelpCategoryInfo
{
    public CommandCategory Category { get; init; }
    public string Emoji { get; init; }
    public string DocsUrl { get; init; }
}

public class HelpFeature
{
    public string LocalizationKey { get; init; }
    public IReadOnlyList<string> TextCommands { get; init; } = [];
    public IReadOnlyList<string> SlashCommands { get; init; } = [];
}
