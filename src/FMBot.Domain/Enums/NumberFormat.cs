using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum NumberFormat
{
    [Option("No separator")]
    NoSeparator = 0, // 4294967295,1 or 5000
    [Option("Decimal separator")]
    DecimalSeparator = 1, // 4.294.967.295,1 or 5.000
    [Option("Comma separator")]
    CommaSeparator = 2, //  4,294,967,295.1 or 5,000
    [Option("Space separator")]
    SpaceSeparator = 3 // 	4 294 967 295,1 or 5 000
}
