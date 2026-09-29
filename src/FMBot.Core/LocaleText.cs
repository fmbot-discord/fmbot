using System.Text;

namespace FMBot.Core;

public static class LocaleText
{
    public static string Interpolate(string translation, (string Name, string Value)[] args)
    {
        if (args == null || args.Length == 0)
        {
            return translation;
        }

        var result = new StringBuilder(translation);
        foreach (var arg in args)
        {
            result.Replace($"{{{{{arg.Name}}}}}", arg.Value ?? string.Empty);
        }

        return result.ToString();
    }
}
