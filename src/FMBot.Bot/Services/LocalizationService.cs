using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace FMBot.Bot.Services;

public class LocalizationService
{
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _translations = new();
    private const string FallbackLocale = "en";

    public LocalizationService()
    {
        LoadAllLocales();
    }

    private void LoadAllLocales()
    {
        var localesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Locales");

        if (!Directory.Exists(localesPath))
        {
            Log.Warning("Locales directory not found at {Path}", localesPath);
            return;
        }

        foreach (var file in Directory.GetFiles(localesPath, "*.json"))
        {
            var locale = Path.GetFileNameWithoutExtension(file);

            try
            {
                var json = File.ReadAllText(file);
                var doc = JsonDocument.Parse(json);
                var flat = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                FlattenJson(doc.RootElement, "", flat);
                this._translations[locale] = flat;
                Log.Information("Loaded {Count} translation keys for locale {Locale}", flat.Count, locale);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load locale file {File}", file);
            }
        }
    }

    private static void FlattenJson(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJson(property.Value, key, result);
                }
                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    result[prefix] = value;
                }
                break;
        }
    }

    public LocaleAccessor For(string locale)
    {
        return new LocaleAccessor(this, locale);
    }

    public string Get(string locale, string key, params (string name, string value)[] replacements)
    {
        var translated = Resolve(locale, key);

        foreach (var (name, value) in replacements)
        {
            translated = translated.Replace($"{{{name}}}", value, StringComparison.Ordinal);
        }

        return translated;
    }

    private string Resolve(string locale, string key)
    {
        if (!string.IsNullOrEmpty(locale) &&
            locale != FallbackLocale &&
            this._translations.TryGetValue(locale, out var localeDict) &&
            localeDict.TryGetValue(key, out var localized))
        {
            return localized;
        }

        if (this._translations.TryGetValue(FallbackLocale, out var fallbackDict) &&
            fallbackDict.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return key;
    }
}
