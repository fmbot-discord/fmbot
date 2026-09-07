using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class ChartSizeAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private const int MaxChoices = 25;

    private static readonly (int Width, int Height)[] DefaultSizes =
    [
        (3, 3), (4, 4), (5, 5), (6, 6), (8, 8), (10, 10), (15, 15),
        (4, 3), (5, 3), (8, 5), (10, 6), (4, 8), (15, 6)
    ];

    private static readonly int[] CommonHeights = [3, 4, 5, 6, 8, 10, 2, 7, 9, 12, 15];

    private static readonly Regex SizeInput = new(
        @"^\s*(?<width>[0-9]{1,3})(?<separator>\s*(?:[x×*,/]|by)\s*|\s+)?(?<height>[0-9]{0,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var suggestions = GetSuggestions(option.Value, ChartService.MaxImages);

        var localizer = Localizer.ForGuild(context.Interaction.GuildId, discordLocale: context.Interaction.GuildLocale);
        var countKey = IsArtistChart(context) ? "shared.artists" : "shared.albums";

        var choices = suggestions.Select(s =>
        {
            var value = $"{s.Width}x{s.Height}";
            var label = $"{value} · {localizer.TranslateCount(countKey, s.Width * s.Height)}";
            return new ApplicationCommandOptionChoiceProperties(label, value);
        });

        return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>>(choices.ToList());
    }

    public static List<(int Width, int Height)> GetSuggestions(string input, int maxImages)
    {
        var results = new List<(int Width, int Height)>();
        var seen = new HashSet<(int, int)>();

        void Add(int width, int height)
        {
            if (results.Count >= MaxChoices || width < 1 || height < 1 || width * height > maxImages)
            {
                return;
            }

            if (seen.Add((width, height)))
            {
                results.Add((width, height));
            }
        }

        var match = string.IsNullOrWhiteSpace(input) ? null : SizeInput.Match(input);
        if (match == null || !match.Success)
        {
            foreach (var size in DefaultSizes)
            {
                Add(size.Width, size.Height);
            }

            return results;
        }

        var width = int.Parse(match.Groups["width"].Value);
        var hasSeparator = match.Groups["separator"].Success;
        var heightText = match.Groups["height"].Value;

        if (width < 1 || width > maxImages)
        {
            foreach (var size in DefaultSizes)
            {
                Add(size.Width, size.Height);
            }

            return results;
        }

        var maxHeight = maxImages / width;

        if (heightText.Length > 0)
        {
            var height = int.Parse(heightText);

            if (height <= maxHeight)
            {
                Add(width, height);

                for (var i = 0; i <= 9; i++)
                {
                    Add(width, height * 10 + i);
                }
            }
            else
            {
                var square = (int)Math.Sqrt(maxImages);
                Add(width, maxHeight);
                Add(maxImages / height, height);
                Add(square, square);
            }

            return results;
        }

        if (hasSeparator)
        {
            Add(width, Math.Min(width, maxHeight));

            for (var height = 1; height <= maxHeight; height++)
            {
                Add(width, height);
            }

            return results;
        }

        Add(width, Math.Min(width, maxHeight));

        foreach (var size in DefaultSizes.Where(w => w.Width != width &&
                                                     w.Width.ToString().StartsWith(width.ToString())))
        {
            Add(size.Width, size.Height);
        }

        foreach (var height in CommonHeights)
        {
            Add(width, height);
        }

        for (var height = 1; height <= maxHeight; height++)
        {
            Add(width, height);
        }

        return results;
    }

    private static bool IsArtistChart(AutocompleteInteractionContext context)
    {
        return context.Interaction.Data.Options.Any(o =>
            o.Type == ApplicationCommandOptionType.SubCommand &&
            o.Name.Equals("artists", StringComparison.OrdinalIgnoreCase));
    }
}
