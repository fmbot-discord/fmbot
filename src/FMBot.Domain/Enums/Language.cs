using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum Language
{
    [Option("English", "English")]
    English = 0,
    [Option("Português do Brasil", "Portuguese, Brazilian")]
    Portuguese = 1,
    [Option("Español", "Spanish")]
    Spanish = 2,
    [Option("हिन्दी", "Hindi")]
    Hindi = 3,
    [Option("Deutsch", "German")]
    German = 4,
    [Option("Polski", "Polish")]
    Polish = 5,
    [Option("Nederlands", "Dutch")]
    Dutch = 6,
    [Option("Français", "French")]
    French = 7,
    [Option("Italiano", "Italian")]
    Italian = 8,
    [Option("Türkçe", "Turkish")]
    Turkish = 9,
    [Option("Svenska", "Swedish")]
    Swedish = 10
}
