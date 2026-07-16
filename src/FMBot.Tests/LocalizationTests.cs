using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
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
        "shared.ordinalOne", "shared.ordinalTwo", "shared.ordinalFew", "shared.ordinalOther",
        "shared.period.oneDay", "shared.period.day", "shared.period.yesterday", "shared.period.twoDay",
        "shared.period.threeDay", "shared.period.fourDay", "shared.period.fiveDay", "shared.period.sixDay",
        "shared.period.weekly", "shared.period.monthly", "shared.period.quarterly", "shared.period.halfYearly",
        "shared.period.yearly", "shared.period.twoYear", "shared.period.overall",
        "whoknows.alsoPlayingOneArtist", "whoknows.alsoPlayingTwoArtists", "whoknows.alsoPlayingThreeArtists",
        "whoknows.alsoPlayingManyArtists_one", "whoknows.alsoPlayingManyArtists_other",
        "whoknows.alsoPlayingOneAlbum", "whoknows.alsoPlayingTwoAlbums", "whoknows.alsoPlayingThreeAlbums",
        "whoknows.alsoPlayingManyAlbums_one", "whoknows.alsoPlayingManyAlbums_other",
        "whoknows.alsoPlayingOneTrack", "whoknows.alsoPlayingTwoTracks", "whoknows.alsoPlayingThreeTracks",
        "whoknows.alsoPlayingManyTracks_one", "whoknows.alsoPlayingManyTracks_other"
    ];

    private static readonly string[] DynamicPlainKeys =
    [
        "album.typeSingleBy", "album.typeCompilationBy", "album.typeAlbumBy",
        "track.headerOnSingle", "track.headerOnCompilation", "track.headerOnAlbum",
        "errors.lastFmNoResult.artist", "errors.lastFmNoResult.album",
        "errors.lastFmNoResult.track", "errors.lastFmNoResult.artistList",
        "jumble.hints.typeArtistPerson", "jumble.hints.typeArtistGroup",
        "jumble.hints.typeArtistOrchestra", "jumble.hints.typeArtistChoir",
        "jumble.hints.typeArtistCharacter",
        "jumble.hints.typeAlbumArtistPerson", "jumble.hints.typeAlbumArtistGroup",
        "jumble.hints.typeAlbumArtistOrchestra", "jumble.hints.typeAlbumArtistChoir",
        "jumble.hints.typeAlbumArtistCharacter"
    ];

    private static readonly string[] DynamicPluralBaseKeys = [];

    private static readonly Dictionary<string, int> KnownDynamicCallSites = new()
    {
        ["AlbumBuilders.cs"] = 1,
        ["TrackBuilders.cs"] = 1,
        ["TemplateBuilders.cs"] = 1
    };

    private static IEnumerable<string> TemplateOptionKeys()
    {
        return TemplateOptions.Options.Select(o => o.DescriptionKey);
    }

    private static readonly long[] PluralSampleCounts =
        Enumerable.Range(0, 301).Select(i => (long)i)
            .Concat([1000L, 1001L, 999999L, 1000000L, 2000000L, 3000001L])
            .ToArray();

    private static HashSet<string> ReachablePluralSuffixes(Language language)
    {
        return PluralSampleCounts.Select(count => Localizer.GetPluralSuffix(language, count)).ToHashSet();
    }

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
    [TestCase(Language.Indonesian, 0, "_other")]
    [TestCase(Language.Indonesian, 1, "_other")]
    [TestCase(Language.Indonesian, 2, "_other")]
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
    public void PeriodAliasesAreSafeInputTokens()
    {
        var englishTokens = new HashSet<string>
        {
            "1-day", "1day", "1d", "24h", "24-h", "24hr", "24-hr", "24hours",
            "today", "day", "daily", "yesterday", "yd",
            "2-day", "2day", "2d", "3-day", "3day", "3d", "4-day", "4day", "4d",
            "5-day", "5day", "5d", "6-day", "6day", "6d",
            "weekly", "week", "w", "7d", "monthly", "month", "m", "1m", "30d",
            "quarterly", "quarter", "q", "3m", "90d",
            "half-yearly", "halfyearly", "half", "h", "6m", "180d",
            "yearly", "year", "y", "12m", "365d", "1y",
            "two-year", "twoyear", "two-yearly", "twoyearly", "2y", "2year", "24m", "730d",
            "overall", "alltime", "all-time", "all", "a", "o", "at"
        };

        var violations = new List<string>();
        var tokenProperties = typeof(PeriodAliases.PeriodTokens).GetProperties()
            .Where(s => s.Name != nameof(PeriodAliases.PeriodTokens.ExcludedMonths));
        foreach (var language in Enum.GetValues<Language>())
        {
            var tokens = PeriodAliases.For(language);
            var seen = new Dictionary<string, string>();
            foreach (var property in tokenProperties)
            {
                foreach (var alias in (string[])property.GetValue(tokens)!)
                {
                    if (alias != alias.ToLowerInvariant())
                    {
                        violations.Add($"{language} {property.Name} '{alias}': must be lowercase");
                    }

                    if (alias.Length < 2)
                    {
                        violations.Add($"{language} {property.Name} '{alias}': too short, collides with search text");
                    }

                    if (englishTokens.Contains(alias))
                    {
                        violations.Add($"{language} {property.Name} '{alias}': shadows an English input token");
                    }

                    if (seen.TryGetValue(alias, out var otherPeriod) && otherPeriod != property.Name)
                    {
                        violations.Add($"{language} '{alias}': ambiguous between {otherPeriod} and {property.Name}");
                    }

                    seen[alias] = property.Name;
                }
            }
        }

        Assert.That(violations, Is.Empty, string.Join("\n", violations));
    }

    [Test]
    public void PeriodLabelsRoundTripAsInput()
    {
        var expectedDescriptions = new Dictionary<string, string>
        {
            ["oneDay"] = "24h",
            ["day"] = "day",
            ["yesterday"] = "yesterday",
            ["twoDay"] = "2-day",
            ["threeDay"] = "3-day",
            ["fourDay"] = "4-day",
            ["fiveDay"] = "5-day",
            ["sixDay"] = "6-day",
            ["weekly"] = "Weekly",
            ["monthly"] = "Monthly",
            ["quarterly"] = "Quarterly",
            ["halfYearly"] = "Half-yearly",
            ["yearly"] = "Yearly",
            ["twoYear"] = "Two-year",
            ["overall"] = "Overall"
        };

        var violations = new List<string>();
        foreach (var language in Enum.GetValues<Language>())
        {
            var translations = LoadLocaleFile(language.GetLocaleCode());
            foreach (var (suffix, expected) in expectedDescriptions)
            {
                if (!translations.TryGetValue($"shared.period.{suffix}", out var label))
                {
                    continue;
                }

                var timeSettings = SettingService.GetTimePeriod(label, language: language);
                if (timeSettings.Description != expected)
                {
                    violations.Add($"{language} '{label}' ({suffix}): parsed as '{timeSettings.Description}', expected '{expected}'");
                }
                else if (!string.IsNullOrWhiteSpace(timeSettings.NewSearchValue))
                {
                    violations.Add($"{language} '{label}' ({suffix}): left '{timeSettings.NewSearchValue}' behind as search text");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "Every shared.period.* label must parse back to the period it describes and leave no search text behind. " +
            $"Add the label to the matching category in PeriodAliases.cs, or reword it:\n{string.Join("\n", violations)}");
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

    [Test]
    public void DynamicallyBuiltKeysExistInEnglish()
    {
        var english = LoadLocaleFile("en");

        var missing = new List<string>();
        foreach (var key in DynamicPlainKeys.Concat(TemplateOptionKeys()))
        {
            if (!english.ContainsKey(key))
            {
                missing.Add(key);
            }
        }

        foreach (var baseKey in DynamicPluralBaseKeys)
        {
            if (!english.ContainsKey($"{baseKey}_one") || !english.ContainsKey($"{baseKey}_other"))
            {
                missing.Add($"{baseKey} (needs _one and _other)");
            }
        }

        Assert.That(missing, Is.Empty,
            "Keys built dynamically in code (switch expressions, TemplateOption.DescriptionKey) are invisible to the " +
            $"literal key scan, so they are registered here and must exist in en.json:\n{string.Join("\n", missing)}");
    }

    [Test]
    public void NoUnregisteredDynamicLocalizeCallSites()
    {
        var dynamicCall = new Regex(@"\.(?:Localize|LocalizeCount)\(\s*(?![""@\s])", RegexOptions.Compiled);
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "FMBot.Bot"), "*.cs", SearchOption.AllDirectories);

        var found = new Dictionary<string, int>();
        foreach (var file in sourceFiles)
        {
            var count = dynamicCall.Matches(File.ReadAllText(file)).Count;
            if (count > 0)
            {
                found[Path.GetFileName(file)] = count;
            }
        }

        var violations = new List<string>();
        foreach (var entry in found)
        {
            if (!KnownDynamicCallSites.TryGetValue(entry.Key, out var expected) || entry.Value != expected)
            {
                violations.Add($"{entry.Key}: {entry.Value} dynamic call site(s), registered: {(KnownDynamicCallSites.TryGetValue(entry.Key, out var e) ? e : 0)}");
            }
        }

        foreach (var registered in KnownDynamicCallSites.Keys.Where(k => !found.ContainsKey(k)))
        {
            violations.Add($"{registered}: registered as dynamic but no dynamic call sites found - update KnownDynamicCallSites");
        }

        Assert.That(violations, Is.Empty,
            "Localize/LocalizeCount calls without a literal key are invisible to the key-existence scan. " +
            "Register the keys such a call can produce in DynamicPlainKeys/DynamicPluralBaseKeys (or TemplateOptionKeys) " +
            $"and update KnownDynamicCallSites:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void LocalizerLiteralKeysExistInEnglish()
    {
        var english = LoadLocaleFile("en");
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Models", "Localizer.cs"));
        var literalRegex = new Regex(@"""(?<key>[a-z][a-zA-Z0-9]*(?:\.[a-zA-Z0-9]+)+)""", RegexOptions.Compiled);

        var missing = new List<string>();
        foreach (Match match in literalRegex.Matches(source))
        {
            var key = match.Groups["key"].Value;
            if (!english.ContainsKey(key) && !english.ContainsKey($"{key}_other"))
            {
                missing.Add(key);
            }
        }

        Assert.That(missing, Is.Empty,
            "Keys referenced inside Localizer.cs are invisible to the call-site regex (no leading dot on bare calls), " +
            $"so every dotted string literal in that file must resolve against en.json:\n{string.Join("\n", missing)}");
    }

    [Test]
    public void LocalesCarryAllReachablePluralForms()
    {
        var english = LoadLocaleFile("en");
        var pluralBases = english.Keys
            .Where(k => k.EndsWith("_other"))
            .Select(k => k[..^"_other".Length])
            .ToList();

        var manyFallsBackToOther = new[] { Language.French, Language.Spanish, Language.Portuguese, Language.Italian };

        var violations = new List<string>();
        foreach (var language in Enum.GetValues<Language>())
        {
            if (language == Language.English)
            {
                continue;
            }

            var entries = LoadLocaleFile(language.GetLocaleCode());
            foreach (var suffix in ReachablePluralSuffixes(language))
            {
                foreach (var baseKey in pluralBases)
                {
                    if (entries.ContainsKey($"{baseKey}{suffix}"))
                    {
                        continue;
                    }

                    if (suffix == "_many" && manyFallsBackToOther.Contains(language) &&
                        entries.ContainsKey($"{baseKey}_other"))
                    {
                        continue;
                    }

                    violations.Add($"{language.GetLocaleCode()}: {baseKey}{suffix} is reachable via GetPluralSuffix but missing");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "A locale must carry every plural form its own GetPluralSuffix rules can request, otherwise counts render " +
            $"with the wrong grammatical form or fall back to English:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void EveryEnglishKeyExistsInEveryLocale()
    {
        var english = LoadLocaleFile("en");

        var violations = new List<string>();
        foreach (var language in Enum.GetValues<Language>())
        {
            if (language == Language.English)
            {
                continue;
            }

            var reachable = ReachablePluralSuffixes(language);
            var entries = LoadLocaleFile(language.GetLocaleCode());
            foreach (var key in english.Keys)
            {
                var suffix = CountSuffixes.FirstOrDefault(key.EndsWith);
                if (suffix != null && !reachable.Contains(suffix))
                {
                    continue;
                }

                if (!entries.ContainsKey(key))
                {
                    violations.Add($"{language.GetLocaleCode()}: {key}");
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "Every en.json key must exist in every locale (except plural forms the language never requests), otherwise " +
            $"embeds render half-translated with only a log line to catch it:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void NoMarkdownDriftBetweenEnglishAndTranslations()
    {
        var english = LoadLocaleFile("en");
        var localesDir = Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Resources", "Locales");
        string[] tokens = ["**", "`", "[", "]", "]("];

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
                    continue;
                }

                foreach (var token in tokens)
                {
                    var referenceCount = CountOccurrences(reference, token);
                    var translationCount = CountOccurrences(entry.Value, token);
                    if (referenceCount != translationCount)
                    {
                        violations.Add($"{localeCode}: {entry.Key} has {translationCount}x '{token}' but English has {referenceCount}x");
                    }
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "Markdown structure must match the English source - broken links and unbalanced bold render literally in " +
            $"Discord while the placeholder check stays green:\n{string.Join("\n", violations)}");
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    [Test]
    public void NoCustomEmoteTagsInLocaleFiles()
    {
        var emoteRegex = new Regex(@"<a?:[a-zA-Z0-9_]+:\d+>", RegexOptions.Compiled);
        var localesDir = Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Resources", "Locales");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(localesDir, "*.json"))
        {
            var localeCode = Path.GetFileNameWithoutExtension(file);
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(file))!;
            foreach (var entry in entries.Where(entry => emoteRegex.IsMatch(entry.Value)))
            {
                violations.Add($"{localeCode}: {entry.Key}");
            }
        }

        Assert.That(violations, Is.Empty,
            "Custom emote tags must never live in locale JSON (they break when the emote is re-uploaded) - " +
            $"concatenate them in C# or pass as an {{{{emote}}}} arg:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void PlaceholdersUseOnlyLetters()
    {
        var anyPlaceholderRegex = new Regex(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled);
        var validNameRegex = new Regex("^[a-zA-Z]+$", RegexOptions.Compiled);
        var localesDir = Path.Combine(RepoRoot(), "src", "FMBot.Bot", "Resources", "Locales");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(localesDir, "*.json"))
        {
            var localeCode = Path.GetFileNameWithoutExtension(file);
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(file))!;
            foreach (var entry in entries)
            {
                foreach (Match match in anyPlaceholderRegex.Matches(entry.Value))
                {
                    if (!validNameRegex.IsMatch(match.Groups[1].Value))
                    {
                        violations.Add($"{localeCode}: {entry.Key} contains {{{{{match.Groups[1].Value}}}}}");
                    }
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "The runtime placeholder validator only recognizes {{likeThis}} ([a-zA-Z]+). A placeholder with digits or " +
            "underscores is invisible to it, so a translation that mangles it ships silently with the value missing. " +
            $"Rename the placeholder to letters only:\n{string.Join("\n", violations)}");
    }

    [Test]
    public void NoOrphanedEnglishKeys()
    {
        var english = LoadLocaleFile("en");
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "FMBot.Bot"), "*.cs", SearchOption.AllDirectories);

        var plainKeyRegex = new Regex(@"\.(?:Localize|Translate)\(\s*""(?<key>[a-zA-Z][a-zA-Z0-9.]+)""", RegexOptions.Compiled);
        var countKeyRegex = new Regex(@"\.(?:LocalizeCount|TranslateCount)\(\s*""(?<key>[a-zA-Z][a-zA-Z0-9.]+)""", RegexOptions.Compiled);

        var plainRefs = new HashSet<string>(RuntimeKeys.Concat(DynamicPlainKeys).Concat(TemplateOptionKeys()));
        var pluralRefs = new HashSet<string>(DynamicPluralBaseKeys);
        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            foreach (Match match in plainKeyRegex.Matches(content))
            {
                plainRefs.Add(match.Groups["key"].Value);
            }

            foreach (Match match in countKeyRegex.Matches(content))
            {
                pluralRefs.Add(match.Groups["key"].Value);
            }
        }

        var orphans = new List<string>();
        foreach (var key in english.Keys)
        {
            if (key.StartsWith("shared."))
            {
                continue;
            }

            if (plainRefs.Contains(key))
            {
                continue;
            }

            var suffix = CountSuffixes.FirstOrDefault(key.EndsWith);
            if (suffix != null && pluralRefs.Contains(key[..^suffix.Length]))
            {
                continue;
            }

            orphans.Add(key);
        }

        Assert.That(orphans, Is.Empty,
            "These en.json keys have no call site anywhere in FMBot.Bot, so they cost translator effort across 11 " +
            "languages for output that never renders. Delete them from en.json and every locale file, or register " +
            $"them in DynamicPlainKeys/DynamicPluralBaseKeys/RuntimeKeys if they are referenced dynamically:\n{string.Join("\n", orphans)}");
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
    public void LocalizationFilesAreLargeEnoughForNetCord()
    {
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(LocalizationsDir(), "*.json"))
        {
            var size = new FileInfo(file).Length;
            if (size <= 3)
            {
                violations.Add($"{Path.GetFileName(file)}: {size} bytes");
            }
        }

        Assert.That(violations, Is.Empty,
            $"NetCord zero-pads SlashCommandLocalizations files of 3 bytes or fewer, which crashes bot startup with a 0x00 JsonException. " +
            $"Keep placeholder files padded (e.g. {{\n  \"commands\": {{}}\n}}):\n{string.Join("\n", violations)}");
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
    public void SlashCommandDescriptionsAreWithinDiscordLimit()
    {
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(LocalizationsDir(), "*.json"))
        {
            var root = JsonNode.Parse(File.ReadAllText(file));
            FindLongDescriptions(root, Path.GetFileName(file), "$", violations);
        }

        Assert.That(violations, Is.Empty,
            $"Slash command and parameter descriptions must be 100 characters or fewer, otherwise Discord rejects the command registration at startup:\n{string.Join("\n", violations)}");
    }

    private static void FindLongDescriptions(JsonNode? node, string file, string path, List<string> violations)
    {
        if (node is not JsonObject jsonObject)
        {
            return;
        }

        foreach (var property in jsonObject)
        {
            if (property.Key == "description" && property.Value is JsonValue value &&
                value.TryGetValue<string>(out var description) && description.Length > 100)
            {
                violations.Add($"{file}: {path}.description is {description.Length} chars: {description}");
            }

            FindLongDescriptions(property.Value, file, $"{path}.{property.Key}", violations);
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
