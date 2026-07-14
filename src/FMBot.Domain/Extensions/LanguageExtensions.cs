using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Extensions;

public static class LanguageExtensions
{
    private static readonly ConcurrentDictionary<Language, CultureInfo> Cultures = new();

    extension(Language language)
    {
        public CultureInfo GetCultureInfo()
        {
            return Cultures.GetOrAdd(language, l => new CultureInfo(l.GetLocaleCode()));
        }

        public string GetLocaleCode()
        {
            return language switch
            {
                Language.Portuguese => "pt-BR",
                Language.Spanish => "es-ES",
                Language.Hindi => "hi",
                Language.German => "de",
                Language.Polish => "pl",
                Language.Dutch => "nl",
                Language.French => "fr",
                Language.Italian => "it",
                Language.Turkish => "tr",
                Language.Swedish => "sv-SE",
                _ => "en"
            };
        }

        public string GetEnglishName()
        {
            return language switch
            {
                Language.Portuguese => "Brazilian Portuguese",
                Language.Spanish => "Spanish",
                Language.Hindi => "Hindi",
                Language.German => "German",
                Language.Polish => "Polish",
                Language.Dutch => "Dutch",
                Language.French => "French",
                Language.Italian => "Italian",
                Language.Turkish => "Turkish",
                Language.Swedish => "Swedish",
                _ => "English"
            };
        }
    }

    public static Language? FromDiscordLocale(string discordLocale)
    {
        return discordLocale switch
        {
            "en-US" or "en-GB" => Language.English,
            "pt-BR" => Language.Portuguese,
            "es-ES" or "es-419" => Language.Spanish,
            "hi" => Language.Hindi,
            "de" => Language.German,
            "pl" => Language.Polish,
            "nl" => Language.Dutch,
            "fr" => Language.French,
            "it" => Language.Italian,
            "tr" => Language.Turkish,
            "sv-SE" => Language.Swedish,
            _ => null
        };
    }

    public static Language? FromLocaleCode(string localeCode)
    {
        foreach (var language in Enum.GetValues<Language>())
        {
            if (string.Equals(language.GetLocaleCode(), localeCode, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return null;
    }

    public static Language? FromUserInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim().ToLowerInvariant();
        if (trimmed is "हिन्दी" or "हिंदी")
        {
            return Language.Hindi;
        }

        return RemoveDiacritics(trimmed) switch
        {
            "english" or "en" => Language.English,
            "portuguese" or "portugues" or "pt" or "pt-br" or "ptbr" => Language.Portuguese,
            "spanish" or "espanol" or "es" or "es-es" => Language.Spanish,
            "hindi" or "hi" => Language.Hindi,
            "german" or "deutsch" or "de" => Language.German,
            "polish" or "polski" or "pl" => Language.Polish,
            "dutch" or "nederlands" or "nl" => Language.Dutch,
            "french" or "francais" or "fr" => Language.French,
            "italian" or "italiano" or "it" => Language.Italian,
            "turkish" or "turkce" or "tr" => Language.Turkish,
            "swedish" or "svenska" or "sv" or "sv-se" => Language.Swedish,
            _ => null
        };
    }

    private static string RemoveDiacritics(string text)
    {
        var formD = text.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
