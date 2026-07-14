using System.Collections.Concurrent;
using System.Globalization;
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
}
