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

public class TrackAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly TrackService _trackService;

    public TrackAutoComplete(TrackService trackService)
    {
        this._trackService = trackService;
    }

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var recentlyPlayedTracks = await this._trackService.GetLatestTracks(context.User.Id);
        var recentTopTracks = await this._trackService.GetRecentTopTracksAutoComplete(context.User.Id);

        var results = new List<string>();

        if (string.IsNullOrWhiteSpace(option.Value))
        {
            if (recentlyPlayedTracks == null || !recentlyPlayedTracks.Any() ||
                recentTopTracks == null || !recentTopTracks.Any())
            {
                results.Add("Start typing to search through tracks...");

                return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
                    new ApplicationCommandOptionChoiceProperties(s, s)));
            }

            results
                .ReplaceOrAddToList(recentlyPlayedTracks.Select(s => s.Name).Take(5));

            results
                .ReplaceOrAddToList(recentTopTracks.Select(s => s.Name).Take(5));
        }
        else
        {
            try
            {
                var searchValue = option.Value;
                results = [searchValue];

                var trackResults =
                    await this._trackService.SearchThroughTracks(searchValue);

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentTopTracks
                    .Where(w => w.Track.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(2));

                results.ReplaceOrAddToList(recentTopTracks
                    .Where(w => w.Track.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(3));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Artist.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Popularity != null && w.Popularity > 60 &&
                                w.Name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(s => s.Name));

                results.ReplaceOrAddToList(trackResults
                    .Where(w => w.Name.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .Select(s => s.Name));


                results.ReplaceOrAddToList(trackResults
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

        return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
            new ApplicationCommandOptionChoiceProperties(s, s)));
    }
}
