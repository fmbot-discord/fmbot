using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.AutoCompleteHandlers;

public class ArtistAutoComplete : AutocompleteHandler
{
    private readonly ArtistsService _artistsService;

    public ArtistAutoComplete(ArtistsService artistsService)
    {
        this._artistsService = artistsService;
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var recentlyPlayedArtists = await this._artistsService.GetLatestArtists(context.User.Id);
        var recentTopArtists = await this._artistsService.GetRecentTopArtists(context.User.Id);

        var results = new List<string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            if (recentlyPlayedArtists == null || !recentlyPlayedArtists.Any() ||
                recentTopArtists == null || !recentTopArtists.Any())
            {
                results.Add("Start typing to search through artists...");

                return await Task.FromResult(
                    AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
            }

            results
                .ReplaceOrAddToList(recentlyPlayedArtists.Take(4));

            results
                .ReplaceOrAddToList(recentTopArtists.Take(4));
        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();
            results = new List<string>
            {
                searchValue
            };

            var artistResults =
                await this._artistsService.SearchThroughArtists(searchValue);

            results.ReplaceOrAddToList(recentlyPlayedArtists
                .Where(w => w.ToLower().StartsWith(searchValue.ToLower()))
                .Take(4));

            results.ReplaceOrAddToList(recentTopArtists
                .Where(w => w.ToLower().StartsWith(searchValue.ToLower()))
                .Take(4));

            results.ReplaceOrAddToList(recentlyPlayedArtists
                .Where(w => w.ToLower().Contains(searchValue.ToLower()))
                .Take(2));

            results.ReplaceOrAddToList(recentTopArtists
                .Where(w => w.ToLower().Contains(searchValue.ToLower()))
                .Take(3));

            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Popularity > 60 &&
                            w.Name.ToLower().Contains(searchValue.ToLower()))
                .Take(2)
                .Select(s => s.Name));

            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Name.ToLower().StartsWith(searchValue.ToLower()))
                .Take(4)
                .Select(s => s.Name));


            results.ReplaceOrAddToList(artistResults
                .Where(w => w.Name.ToLower().Contains(searchValue.ToLower()))
                .Take(2)
                .Select(s => s.Name));
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
