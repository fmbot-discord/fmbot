using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class ArtistAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly ArtistsService _artistsService;

    public ArtistAutoComplete(ArtistsService artistsService)
    {
        this._artistsService = artistsService;
    }

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var recentlyPlayedArtists = await this._artistsService.GetLatestArtists(context.User.Id);
        var recentTopArtists = (await this._artistsService.GetRecentTopArtists(context.User.Id))
            .Select(s => s.ArtistName).ToList();

        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            if (recentlyPlayedArtists == null || !recentlyPlayedArtists.Any() ||
                recentTopArtists == null || !recentTopArtists.Any())
            {
                results.Add("Start typing to search through artists...");

                return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
                    new ApplicationCommandOptionChoiceProperties(s, s)));
            }

            results
                .ReplaceOrAddToList(recentlyPlayedArtists.Take(4));

            results
                .ReplaceOrAddToList(recentTopArtists.Take(4));
        }
        else
        {
            var searchValue = option.Value;
            results = [searchValue];

            var artistResults =
                await this._artistsService.SearchThroughArtists(searchValue);

            results.ReplaceOrAddToList(recentlyPlayedArtists
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(4));

            results.ReplaceOrAddToList(recentTopArtists
                .Where(w => w.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(4));

            results.ReplaceOrAddToList(recentlyPlayedArtists
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(2));

            results.ReplaceOrAddToList(recentTopArtists
                .Where(w => w.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(3));

            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Popularity > 60 &&
                            w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .Select(s => s.Name));

            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Name.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .Select(s => s.Name));


            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .Select(s => s.Name));
        }

        return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
            new ApplicationCommandOptionChoiceProperties(s, s)));
    }
}
