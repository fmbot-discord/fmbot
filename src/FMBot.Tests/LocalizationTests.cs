using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Tests;

public class LocalizationTests
{
    private static readonly string[] CountSuffixes = ["_zero", "_one", "_two", "_few", "_many", "_other"];

    private static readonly string[] RuntimeKeys =
    [
        "shared.timeAgo.seconds_one", "shared.timeAgo.seconds_other", "shared.timeAgo.minute",
        "shared.timeAgo.minutes_one", "shared.timeAgo.minutes_other", "shared.timeAgo.hour",
        "shared.timeAgo.hours_one", "shared.timeAgo.hours_other", "shared.timeAgo.yesterday",
        "shared.timeAgo.days_one", "shared.timeAgo.days_other", "shared.timeAgo.months_one",
        "shared.timeAgo.months_other", "shared.timeAgo.moreThanMonth",
        "shared.days_one", "shared.days_other", "shared.hours_one", "shared.hours_other",
        "shared.minutes_one", "shared.minutes_other",
        "shared.ordinalOne", "shared.ordinalTwo", "shared.ordinalFew", "shared.ordinalOther"
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "FMBot.Discord.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Could not locate repo root from test base directory");
        return directory!.FullName;
    }

    private static Dictionary<string, string> LoadLocaleFile(string localeCode)
    {
        var path = Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Resources", "Locales", $"{localeCode}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(path))!;
    }

    [OneTimeSetUp]
    public void LoadTranslations()
    {
        new LocalizationService(null!).LoadTranslations();
    }

    [Test]
    [TestCase(Language.English, 0, "_other")]
    [TestCase(Language.English, 1, "_one")]
    [TestCase(Language.English, 2, "_other")]
    [TestCase(Language.German, 1, "_one")]
    [TestCase(Language.German, 14, "_other")]
    [TestCase(Language.Hindi, 0, "_one")]
    [TestCase(Language.Hindi, 1, "_one")]
    [TestCase(Language.Hindi, 2, "_other")]
    [TestCase(Language.French, 0, "_one")]
    [TestCase(Language.French, 1, "_one")]
    [TestCase(Language.French, 2, "_other")]
    [TestCase(Language.French, 1000000, "_many")]
    [TestCase(Language.Portuguese, 0, "_one")]
    [TestCase(Language.Portuguese, 1, "_one")]
    [TestCase(Language.Portuguese, 17, "_other")]
    [TestCase(Language.Spanish, 0, "_other")]
    [TestCase(Language.Spanish, 1, "_one")]
    [TestCase(Language.Spanish, 2000000, "_many")]
    [TestCase(Language.Polish, 1, "_one")]
    [TestCase(Language.Polish, 2, "_few")]
    [TestCase(Language.Polish, 4, "_few")]
    [TestCase(Language.Polish, 5, "_many")]
    [TestCase(Language.Polish, 12, "_many")]
    [TestCase(Language.Polish, 13, "_many")]
    [TestCase(Language.Polish, 14, "_many")]
    [TestCase(Language.Polish, 22, "_few")]
    [TestCase(Language.Polish, 25, "_many")]
    [TestCase(Language.Polish, 112, "_many")]
    [TestCase(Language.Polish, 122, "_few")]
    public void PluralSuffix(Language language, long count, string expected)
    {
        Assert.That(Localizer.GetPluralSuffix(language, count), Is.EqualTo(expected));
    }

    [Test]
    public void Interpolation()
    {
        var localizer = new Localizer(Language.English, NumberFormat.CommaSeparator);
        Assert.That(localizer.Translate("shared.requestedBy", ("user", "frikandel")), Is.EqualTo("Requested by frikandel"));
        Assert.That(localizer.Translate("shared.pageCounter", ("page", "2"), ("pages", "14")), Is.EqualTo("Page 2/14"));
    }

    [Test]
    public void CountFormatting()
    {
        var localizer = new Localizer(Language.English, NumberFormat.CommaSeparator);
        Assert.That(localizer.TranslateCount("shared.plays", 1), Is.EqualTo("1 play"));
        Assert.That(localizer.TranslateCount("shared.plays", 5000), Is.EqualTo("5,000 plays"));
    }

    [Test]
    public void FallbackChain()
    {
        var english = new Localizer(Language.English, NumberFormat.NoSeparator);
        Assert.That(english.Translate("nonexistent.key"), Is.EqualTo("nonexistent.key"));

        var german = new Localizer(Language.German, NumberFormat.NoSeparator);
        Assert.That(german.Translate("nonexistent.key"), Is.EqualTo("nonexistent.key"));

        var germanEntries = LoadLocaleFile("de");
        Assert.That(german.Translate("errors.userBlocked"), Is.EqualTo(germanEntries["errors.userBlocked"]));
        Assert.That(german.TranslateCount("shared.plays", 3),
            Is.EqualTo(germanEntries["shared.plays_other"].Replace("{{count}}", "3")));

        var frenchEntries = LoadLocaleFile("fr");
        if (!frenchEntries.ContainsKey("shared.plays_many"))
        {
            var french = new Localizer(Language.French, NumberFormat.NoSeparator);
            Assert.That(french.TranslateCount("shared.plays", 1000000),
                Is.EqualTo(frenchEntries["shared.plays_other"].Replace("{{count}}", "1000000")));
        }
    }

    [Test]
    public void Ordinals()
    {
        var localizer = new Localizer(Language.English, NumberFormat.NoSeparator);
        Assert.That(localizer.Ordinal(1), Is.EqualTo("1st"));
        Assert.That(localizer.Ordinal(2), Is.EqualTo("2nd"));
        Assert.That(localizer.Ordinal(3), Is.EqualTo("3rd"));
        Assert.That(localizer.Ordinal(4), Is.EqualTo("4th"));
        Assert.That(localizer.Ordinal(11), Is.EqualTo("11th"));
        Assert.That(localizer.Ordinal(12), Is.EqualTo("12th"));
        Assert.That(localizer.Ordinal(13), Is.EqualTo("13th"));
        Assert.That(localizer.Ordinal(21), Is.EqualTo("21st"));
        Assert.That(localizer.Ordinal(122), Is.EqualTo("122nd"));
    }

    [Test]
    public void TimeAgoUsesTranslations()
    {
        var localizer = new Localizer(Language.English, NumberFormat.NoSeparator);
        Assert.That(localizer.TimeAgo(DateTime.UtcNow.AddDays(-1.5)), Is.EqualTo("yesterday"));
        Assert.That(localizer.TimeAgo(DateTime.UtcNow.AddDays(-5)), Is.EqualTo("5 days ago"));
        Assert.That(localizer.LongListeningTime(new TimeSpan(3, 5, 12, 0)), Is.EqualTo("3 days, 5 hours"));
        Assert.That(localizer.LongListeningTime(new TimeSpan(0, 1, 30, 0)), Is.EqualTo("1 hour, 30 minutes"));
        Assert.That(localizer.LongListeningTime(new TimeSpan(0, 0, 45, 0)), Is.EqualTo("45 minutes"));
    }

    [Test]
    public void EveryKeyReferencedInSourceExistsInEnglish()
    {
        var english = LoadLocaleFile("en");
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "FMBot.Bot"), "*.cs", SearchOption.AllDirectories);

        var plainKeyRegex = new Regex(@"\.(?:Localize|Translate)\(\s*""(?<key>[a-zA-Z][a-zA-Z0-9.]+)""", RegexOptions.Compiled);
        var countKeyRegex = new Regex(@"\.(?:LocalizeCount|TranslateCount)\(\s*""(?<key>[a-zA-Z][a-zA-Z0-9.]+)""", RegexOptions.Compiled);

        var missing = new List<string>();
        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in plainKeyRegex.Matches(content))
            {
                var key = match.Groups["key"].Value;
                if (!english.ContainsKey(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }

            foreach (Match match in countKeyRegex.Matches(content))
            {
                var key = match.Groups["key"].Value;
                if (!english.ContainsKey($"{key}_one") || !english.ContainsKey($"{key}_other"))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key} (needs _one and _other)");
                }
            }
        }

        foreach (var runtimeKey in RuntimeKeys)
        {
            if (!english.ContainsKey(runtimeKey))
            {
                missing.Add($"runtime: {runtimeKey}");
            }
        }

        Assert.That(missing, Is.Empty, $"Keys referenced in code but missing from en.json:\n{string.Join("\n", missing)}");
    }

    [Test]
    public void CountPlaceholderIsReservedForPluralKeys()
    {
        var english = LoadLocaleFile("en");

        var violations = new List<string>();
        foreach (var entry in english)
        {
            if (!entry.Value.Contains("{{count}}") ||
                entry.Key.StartsWith("shared.ordinal") ||
                CountSuffixes.Any(entry.Key.EndsWith))
            {
                continue;
            }

            violations.Add($"{entry.Key}: {entry.Value}");
        }

        Assert.That(violations, Is.Empty,
            "{{count}} is reserved for the plural driver, so these keys can never pluralize. " +
            $"Give them _one and _other variants and call them with LocalizeCount:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void LocalePlaceholdersMatchEnglishSource()
    {
        var english = LoadLocaleFile("en");
        var placeholderRegex = new Regex(@"\{\{([a-zA-Z]+)\}\}", RegexOptions.Compiled);
        var localesDir = Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Resources", "Locales");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(localesDir, "*.json"))
        {
            var localeCode = Path.GetFileNameWithoutExtension(file);
            if (localeCode == "en")
            {
                continue;
            }

            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(file))!;
            foreach (var entry in entries)
            {
                var reference = GetEnglishReference(entry.Key, english);
                if (reference == null)
                {
                    violations.Add($"{localeCode}: {entry.Key} has no English source key");
                    continue;
                }

                var translationPlaceholders = placeholderRegex.Matches(entry.Value).Select(s => s.Groups[1].Value).ToHashSet();
                var referencePlaceholders = placeholderRegex.Matches(reference).Select(s => s.Groups[1].Value).ToHashSet();
                if (!translationPlaceholders.SetEquals(referencePlaceholders))
                {
                    violations.Add($"{localeCode}: {entry.Key} placeholders don't match the English source");
                }
            }
        }

        Assert.That(violations, Is.Empty, string.Join("\n", violations));
    }

    private static string? GetEnglishReference(string key, Dictionary<string, string> english)
    {
        if (english.TryGetValue(key, out var direct))
        {
            return direct;
        }

        var suffix = CountSuffixes.FirstOrDefault(key.EndsWith);
        if (suffix == null)
        {
            return null;
        }

        var baseKey = key[..^suffix.Length];
        if (english.TryGetValue($"{baseKey}_other", out var other))
        {
            return other;
        }

        return english.GetValueOrDefault($"{baseKey}_one");
    }
}

public class SlashCommandLocalizationTests
{
    private static string LocalizationsDir()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "FMBot.Discord.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null);
        return Path.Combine(directory!.FullName, "src", "FMBot.Bot", "Resources", "SlashCommandLocalizations");
    }

    [Test]
    public void NoNameKeysInAnyLocalizationFile()
    {
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(LocalizationsDir(), "*.json"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file));
            FindNameKeys(root, Path.GetFileName(file), "$", violations);
        }

        Assert.That(violations, Is.Empty,
            $"Localization files must never contain 'name' keys (command names stay identical in every locale):\n{string.Join("\n", violations)}");
    }

    private static void FindNameKeys(JsonNode? node, string file, string path, List<string> violations)
    {
        if (node is not JsonObject jsonObject)
        {
            return;
        }

        foreach (var property in jsonObject)
        {
            if (property.Key == "name")
            {
                violations.Add($"{file}: {path}.name");
            }

            FindNameKeys(property.Value, file, $"{path}.{property.Key}", violations);
        }
    }

    [Test]
    public void EnglishBaseFileMatchesSlashCommandAttributes()
    {
        var generated = GenerateEnglishBase();
        var path = Path.Combine(LocalizationsDir(), "en.json");

        if (Environment.GetEnvironmentVariable("FMBOT_REGEN_SLASH_LOCALIZATIONS") == "1")
        {
            File.WriteAllText(path, generated);
            Assert.Pass("Regenerated en.json");
        }

        var current = JsonNode.Parse(File.ReadAllText(path))!.ToJsonString();
        var expected = JsonNode.Parse(generated)!.ToJsonString();
        Assert.That(current, Is.EqualTo(expected),
            "SlashCommandLocalizations/en.json is out of sync with the [SlashCommand] attributes. " +
            "Run the test with FMBOT_REGEN_SLASH_LOCALIZATIONS=1 to regenerate it.");
    }

    private static string GenerateEnglishBase()
    {
        var commands = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var assembly = typeof(FMBot.Bot.Startup).Assembly;

        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(ApplicationCommandModule<ApplicationCommandContext>).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            var groupAttribute = type.GetCustomAttribute<SlashCommandAttribute>();
            if (groupAttribute != null)
            {
                var group = new JsonObject
                {
                    ["description"] = groupAttribute.Description
                };

                var subCommands = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var subAttribute = method.GetCustomAttribute<SubSlashCommandAttribute>();
                    if (subAttribute == null)
                    {
                        continue;
                    }

                    subCommands[subAttribute.Name] = BuildCommand(subAttribute.Description, method);
                }

                if (subCommands.Count > 0)
                {
                    var subCommandsObject = new JsonObject();
                    foreach (var subCommand in subCommands)
                    {
                        subCommandsObject[subCommand.Key] = subCommand.Value;
                    }

                    group["subcommands"] = subCommandsObject;
                }

                commands[groupAttribute.Name] = group;
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var commandAttribute = method.GetCustomAttribute<SlashCommandAttribute>();
                if (commandAttribute == null)
                {
                    continue;
                }

                commands[commandAttribute.Name] = BuildCommand(commandAttribute.Description, method);
            }
        }

        var commandsObject = new JsonObject();
        foreach (var command in commands)
        {
            commandsObject[command.Key] = command.Value;
        }

        var root = new JsonObject
        {
            ["commands"] = commandsObject
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static JsonObject BuildCommand(string description, MethodInfo method)
    {
        var command = new JsonObject
        {
            ["description"] = description
        };

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in method.GetParameters())
        {
            var parameterAttribute = parameter.GetCustomAttribute<SlashCommandParameterAttribute>();
            if (parameterAttribute?.Description == null)
            {
                continue;
            }

            parameters[parameterAttribute.Name ?? parameter.Name!] = parameterAttribute.Description;
        }

        if (parameters.Count > 0)
        {
            var parametersObject = new JsonObject();
            foreach (var parameter in parameters)
            {
                parametersObject[parameter.Key] = new JsonObject
                {
                    ["description"] = parameter.Value
                };
            }

            command["parameters"] = parametersObject;
        }

        return command;
    }
}
