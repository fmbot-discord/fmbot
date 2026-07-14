using System.Collections.Concurrent;
using System.Globalization;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Extensions;

public static class LanguageExtensions
{
    private static readonly ConcurrentDictionary<Language, CultureInfo> Cultures = new();

    public static CultureInfo GetCultureInfo(this Language language)
    {
        return Cultures.GetOrAdd(language, l => new CultureInfo(l.GetLocaleCode()));
    }

    public static string GetLocaleCode(this Language language)
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
}
