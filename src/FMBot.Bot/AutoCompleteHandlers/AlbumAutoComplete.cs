using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.AutoCompleteHandlers;

public class AlbumAutoComplete : AutocompleteHandler
{
    private readonly AlbumService _albumService;

    public AlbumAutoComplete(AlbumService albumService)
    {
        this._albumService = albumService;
    }

    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var recentlyPlayedAlbums = await this._albumService.GetLatestAlbums(context.User.Id);
        var recentTopAlbums = await this._albumService.GetRecentTopAlbums(context.User.Id);

        var results = new List<string>();

        if (autocompleteInteraction?.Data?.Current?.Value == null ||
            string.IsNullOrWhiteSpace(autocompleteInteraction?.Data?.Current?.Value.ToString()))
        {
            if (recentlyPlayedAlbums == null || !recentlyPlayedAlbums.Any() ||
                recentTopAlbums == null || !recentTopAlbums.Any())
            {
                results.Add("Start typing to search through albums...");

                return await Task.FromResult(
                    AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
            }

            results
                .ReplaceOrAddToList(recentlyPlayedAlbums.Select(s => s.Name).Take(5));

            results
                .ReplaceOrAddToList(recentTopAlbums.Select(s => s.Name).Take(5));
        }
        else
        {
            try
            {
                var searchValue = autocompleteInteraction.Data.Current.Value.ToString();
                results = new List<string>
                {
                    searchValue
                };

                var albumResults =
                    await this._albumService.SearchThroughAlbums(searchValue);

                results.ReplaceOrAddToList(recentlyPlayedAlbums
                    .Where(w => w.Album.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Album.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentlyPlayedAlbums
                    .Where(w => w.Album.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(2));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Album.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(3));

                results.ReplaceOrAddToList(albumResults
                    .Where(w => w.Artist.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(albumResults
                    .Where(w => w.Popularity != null && w.Popularity > 60 &&
                                w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(albumResults
                    .Where(w => w.Name.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .Select(s => s.Name));


                results.ReplaceOrAddToList(albumResults
                    .Where(w => w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => s.Name));

            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        return await Task.FromResult(
            AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
    }
}
