using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services;

public class LocalizationService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    private static readonly ConcurrentDictionary<ulong, Language> GuildLanguages = new();

    private static FrozenDictionary<Language, FrozenDictionary<string, string>> Translations =
        FrozenDictionary<Language, FrozenDictionary<string, string>>.Empty;

    private static readonly Regex PlaceholderRegex = new(@"\{\{([a-zA-Z]+)\}\}", RegexOptions.Compiled);

    private static readonly string[] PluralSuffixes = ["_zero", "_one", "_two", "_few", "_many", "_other"];

    public LocalizationService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public void LoadTranslations()
    {
        var loaded = new Dictionary<Language, Dictionary<string, string>>();
        foreach (var language in Enum.GetValues<Language>())
        {
            var localePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Locales",
                $"{language.GetLocaleCode()}.json");

            if (!File.Exists(localePath))
            {
                Log.Warning("Localization: No locale file found for {language} at {localePath}", language, localePath);
                continue;
            }

            try
            {
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(localePath));
                if (entries == null)
                {
                    Log.Error("Localization: Locale file for {language} deserialized to null", language);
                    continue;
                }

                loaded.Add(language, entries);
            }
            catch (Exception e)
            {
                Log.Error(e, "Localization: Failed to load locale file for {language}", language);
            }
        }

        var english = loaded.TryGetValue(Language.English, out var englishEntries)
            ? englishEntries.ToFrozenDictionary()
            : FrozenDictionary<string, string>.Empty;

        var translations = new Dictionary<Language, FrozenDictionary<string, string>>
        {
            { Language.English, english }
        };

        foreach (var language in loaded.Keys.Where(w => w != Language.English))
        {
            translations.Add(language, ValidatePlaceholders(loaded[language], english, language));
        }

        Translations = translations.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, string> ValidatePlaceholders(Dictionary<string, string> entries,
        FrozenDictionary<string, string> english, Language language)
    {
        var validated = new Dictionary<string, string>();
        foreach (var entry in entries)
        {
            var reference = GetEnglishReference(entry.Key, english);
            if (reference != null && !GetPlaceholders(entry.Value).SetEquals(GetPlaceholders(reference)))
            {
                Log.Warning("Localization: Dropped {translationKey} for {language} - placeholders don't match the English source",
                    entry.Key, language);
                continue;
            }

            validated.Add(entry.Key, entry.Value);
        }

        return validated.ToFrozenDictionary();
    }

    private static string GetEnglishReference(string key, FrozenDictionary<string, string> english)
    {
        if (english.TryGetValue(key, out var direct))
        {
            return direct;
        }

        var suffix = PluralSuffixes.FirstOrDefault(key.EndsWith);
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

    private static HashSet<string> GetPlaceholders(string translation)
    {
        return PlaceholderRegex.Matches(translation).Select(s => s.Groups[1].Value).ToHashSet();
    }

    public async Task LoadAllGuildLanguages()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var servers = await db.Guilds.Where(w => w.Language != null).ToListAsync();
        foreach (var server in servers)
        {
            StoreGuildLanguage(server.DiscordGuildId, server.Language.Value);
        }
    }

    public static void StoreGuildLanguage(ulong discordGuildId, Language language)
    {
        GuildLanguages[discordGuildId] = language;
    }

    public static void RemoveGuildLanguage(ulong discordGuildId)
    {
        GuildLanguages.TryRemove(discordGuildId, out _);
    }

    public static Language GetLanguage(ulong? discordGuildId, string discordLocale = null)
    {
        if (!discordGuildId.HasValue)
        {
            return Language.English;
        }

        if (GuildLanguages.TryGetValue(discordGuildId.Value, out var storedLanguage))
        {
            return storedLanguage;
        }

        if (ConfigData.Data.Bot.UseDiscordGuildLocale == true && discordLocale != null)
        {
            return LanguageExtensions.FromDiscordLocale(discordLocale) ?? Language.English;
        }

        return Language.English;
    }

    internal static string GetTranslation(Language language, string key)
    {
        if (Translations.TryGetValue(language, out var translations) &&
            translations.TryGetValue(key, out var translation))
        {
            return translation;
        }

        if (language != Language.English &&
            Translations.TryGetValue(Language.English, out var english) &&
            english.TryGetValue(key, out var englishTranslation))
        {
            return englishTranslation;
        }

        Log.Warning("Localization: Missing translation key {translationKey} for {language}", key, language);
        return key;
    }

    internal static string GetPluralTranslation(Language language, string key, string pluralSuffix)
    {
        if (Translations.TryGetValue(language, out var translations))
        {
            if (translations.TryGetValue($"{key}{pluralSuffix}", out var translation))
            {
                return translation;
            }
            if (translations.TryGetValue($"{key}_other", out var otherTranslation))
            {
                return otherTranslation;
            }
        }

        if (language != Language.English &&
            Translations.TryGetValue(Language.English, out var english))
        {
            if (english.TryGetValue($"{key}{pluralSuffix}", out var englishTranslation))
            {
                return englishTranslation;
            }
            if (english.TryGetValue($"{key}_other", out var englishOther))
            {
                return englishOther;
            }
        }

        Log.Warning("Localization: Missing plural translation key {translationKey} for {language}", key, language);
        return key;
    }
}
