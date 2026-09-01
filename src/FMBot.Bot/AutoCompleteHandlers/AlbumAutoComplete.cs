using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Serilog;

namespace FMBot.Bot.AutoCompleteHandlers;

public class AlbumAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly AlbumService _albumService;

    public AlbumAutoComplete(AlbumService albumService)
    {
        this._albumService = albumService;
    }

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var recentlyPlayedAlbumsTask = this._albumService.GetLatestAlbums(context.User.Id).ObserveFaults();
        var recentTopAlbumsTask = this._albumService.GetRecentTopAlbums(context.User.Id).ObserveFaults();
        var albumSearchTask = !string.IsNullOrWhiteSpace(option.Value)
            ? this._albumService.SearchThroughAlbums(option.Value).ObserveFaults()
            : null;

        var recentlyPlayedAlbums = await recentlyPlayedAlbumsTask;
        var recentTopAlbums = await recentTopAlbumsTask;

        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            if (recentlyPlayedAlbums == null || !recentlyPlayedAlbums.Any() ||
                recentTopAlbums == null || !recentTopAlbums.Any())
            {
                results.Add("Start typing to search through albums...");

                return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
                    new ApplicationCommandOptionChoiceProperties(s, s)));
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
                var searchValue = option.Value;
                results = [searchValue];

                var albumResults = await albumSearchTask;

                results.ReplaceOrAddToList(recentlyPlayedAlbums
                    .Where(w => w.Album != null && w.Album.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Album != null && w.Album.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentlyPlayedAlbums
                    .Where(w => w.Album != null && w.Album.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(2));

                results.ReplaceOrAddToList(recentTopAlbums
                    .Where(w => w.Album != null && w.Album.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(3));

                results.ReplaceOrAddToList(albumResults.Select(s => s.Name));
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in album autocomplete for search value {SearchValue}", option.Value);
                throw;
            }
        }

        return new List<ApplicationCommandOptionChoiceProperties>(results
            .Where(s => !string.IsNullOrEmpty(s))
            .Take(25)
            .Select(s =>
            {
                var choice = StringExtensions.TruncateLongString(s, 100);
                return new ApplicationCommandOptionChoiceProperties(choice, choice);
            }));
    }
}
