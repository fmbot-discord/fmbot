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
        var recentlyPlayedTracksTask = this._trackService.GetLatestTracks(context.User.Id).ObserveFaults();
        var recentTopTracksTask = this._trackService.GetRecentTopTracksAutoComplete(context.User.Id).ObserveFaults();
        var trackSearchTask = !string.IsNullOrWhiteSpace(option.Value)
            ? this._trackService.SearchThroughTracks(option.Value).ObserveFaults()
            : null;

        var recentlyPlayedTracks = await recentlyPlayedTracksTask;
        var recentTopTracks = await recentTopTracksTask;

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

                var trackResults = await trackSearchTask;

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track != null && w.Track.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentTopTracks
                    .Where(w => w.Track != null && w.Track.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(4));

                results.ReplaceOrAddToList(recentlyPlayedTracks
                    .Where(w => w.Track != null && w.Track.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(2));

                results.ReplaceOrAddToList(recentTopTracks
                    .Where(w => w.Track != null && w.Track.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Name)
                    .Take(3));

                results.ReplaceOrAddToList(trackResults.Select(s => s.Name));
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in track autocomplete for search value {SearchValue}", option.Value);
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
