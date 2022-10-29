namespace FMBot.Bot.Models;

public class MultiSelectionOption<T>
{
    public MultiSelectionOption(string option, string value, int row, string description)
    {
        Option = option;
        Value = value;
        Row = row;
        IsDefault = false;
        Description = description;
    }

    public MultiSelectionOption(string option, string value, int row, bool isDefault, string description)
    {
        Option = option;
        Value = value;
        Row = row;
        IsDefault = isDefault;
        Description = description;
    }

    public string Option { get; }
    public string Value { get; }

    public string Description { get; }

    public int Row { get; }

    public bool IsDefault { get; set; }

    public override string ToString() => Option.ToString();

    public override int GetHashCode() => Option.GetHashCode();

    public override bool Equals(object obj) => Equals(Option, obj);
}
