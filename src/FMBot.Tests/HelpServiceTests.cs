using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord.Services.Commands;
using NUnit.Framework;

namespace FMBot.Tests;

public class HelpServiceTests
{
    private HelpService _helpService = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var commandService = new CommandService<CommandContext>();
        commandService.AddModules(typeof(FMBot.Bot.Program).Assembly);
        this._helpService = new HelpService(commandService);
    }

    [Test]
    public void EverySlashCommandIsInTheCatalog()
    {
        Assert.That(this._helpService.UnmappedSlashCommands, Is.Empty,
            "Slash commands without a text equivalent need an entry in HelpService.SlashOnlyCategories, " +
            "or a matching text command alias, so they show up in help");
    }

    [Test]
    public void CatalogContainsCoreCommands()
    {
        foreach (var name in new[] { "fm", "whoknows", "chart", "topartists", "login", "help", "import manage" })
        {
            Assert.That(this._helpService.FindCommand(name), Is.Not.Null, $"{name} missing from help catalog");
        }

        Assert.That(this._helpService.FindCommand("wk")?.Name, Is.EqualTo("whoknows"));
        Assert.That(this._helpService.FindCommand("top artists")?.Name, Is.EqualTo("topartists"));
        Assert.That(this._helpService.FindCommand("/fm")?.Name, Is.EqualTo("fm"));
        Assert.That(this._helpService.FindCommand("doesnotexist"), Is.Null);
    }

    [Test]
    public void FeaturesResolveToCatalogEntries()
    {
        var slashNames = this._helpService.Commands
            .SelectMany(c => c.SlashCommands.Select(s => s.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in HelpService.Features)
        {
            foreach (var textCommand in feature.TextCommands)
            {
                Assert.That(this._helpService.FindCommand(textCommand), Is.Not.Null,
                    $"Feature {feature.LocalizationKey} references unknown text command {textCommand}");
            }

            foreach (var slashCommand in feature.SlashCommands)
            {
                Assert.That(slashNames, Does.Contain(slashCommand),
                    $"Feature {feature.LocalizationKey} references unknown slash command /{slashCommand}");
            }
        }
    }

    [Test]
    public void EveryCategoryFitsInTwoSelectMenus()
    {
        foreach (var info in HelpService.Categories)
        {
            foreach (var mode in new[] { HelpMode.Slash, HelpMode.Text })
            {
                var commands = this._helpService.GetCategoryCommands(info.Category, mode);
                Assert.That(commands, Is.Not.Empty, $"Category {info.Category} has no commands in {mode} mode");
                Assert.That(commands.Count, Is.LessThanOrEqualTo(50), $"Category {info.Category} has too many commands for two select menus");
            }
        }
    }

    [Test]
    public void MergedSlashCommandsReplaceTextCommandsInSlashMode()
    {
        foreach (var category in new[] { CommandCategory.Artists, CommandCategory.Albums, CommandCategory.Tracks })
        {
            var slashNames = this._helpService.GetCategoryCommands(category, HelpMode.Slash).Select(c => c.Name).ToList();
            Assert.That(slashNames, Does.Contain("gaps"), $"/gaps missing from {category} in slash mode");
            Assert.That(slashNames, Does.Not.Contain("artistgaps").And.Not.Contain("albumgaps").And.Not.Contain("trackgaps"));

            var textNames = this._helpService.GetCategoryCommands(category, HelpMode.Text).Select(c => c.Name).ToList();
            Assert.That(textNames, Does.Not.Contain("gaps"), $"/gaps should be hidden from {category} in text mode");
        }

        Assert.That(this._helpService.GetCategoryCommands(CommandCategory.WhoKnows, HelpMode.Slash).Select(c => c.Name),
            Does.Not.Contain("playleaderboard"));
        Assert.That(this._helpService.Commands.Select(c => c.Name), Does.Contain("source"));
        Assert.That(this._helpService.GetCategoryCommands(CommandCategory.Other, HelpMode.Text).Select(c => c.Name),
            Does.Not.Contain("source"));
    }

    [Test]
    public void EveryCatalogEntryHasAListedCategory()
    {
        var listed = HelpService.Categories.Select(c => c.Category).ToHashSet();

        foreach (var entry in this._helpService.Commands)
        {
            Assert.That(entry.Categories.Any(listed.Contains), Is.True,
                $"{entry.Name} only has categories that are not shown in help: {string.Join(", ", entry.Categories)}");
        }

        Assert.That(listed, Does.Not.Contain(CommandCategory.General));
    }
}
