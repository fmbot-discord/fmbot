using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;

namespace FMBot.Bot.AutoCompleteHandlers;

public class CountryArtistAutoComplete : AutocompleteHandler
{
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;

    public CountryArtistAutoComplete(ArtistsService artistsService, CountryService countryService)
    {
        this._artistsService = artistsService;
        this._countryService = countryService;
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
                results.Add("Start typing to search through countries or artists...");

                return await Task.FromResult(
                    AutocompletionResult.FromSuccess(results.Select(s => new AutocompleteResult(s, s))));
            }

            var recentlyPlayedCountries = (await this._countryService.GetTopCountriesForTopArtistsString(recentlyPlayedArtists)).Select(this._countryService.CountryCodeToCountryName);
            var recentTopCountries = (await this._countryService.GetTopCountriesForTopArtistsString(recentTopArtists)).Select(this._countryService.CountryCodeToCountryName);

            results
                .ReplaceOrAddToList(recentlyPlayedCountries.Take(4));

            results
                .ReplaceOrAddToList(recentlyPlayedArtists.Take(3));

            results
                .ReplaceOrAddToList(recentTopCountries.Take(2));

            results
                .ReplaceOrAddToList(recentTopArtists.Take(2));

        }
        else
        {
            var searchValue = autocompleteInteraction.Data.Current.Value.ToString();
            results = new List<string>
            {
                searchValue
            };

            var countryResults = this._countryService.SearchThroughCountries(searchValue);

            results.ReplaceOrAddToList(countryResults
                .Where(w => CountryService.TrimCountry(w.Code).Equals(CountryService.TrimCountry(searchValue)))
                .Select(s => s.Name)
                .Take(1));

            results.ReplaceOrAddToList(countryResults
                .Where(w => CountryService.TrimCountry(w.Code).StartsWith(CountryService.TrimCountry(searchValue)))
                .Select(s => s.Name)
                .Take(1));

            results.ReplaceOrAddToList(countryResults
                .Where(w => CountryService.TrimCountry(w.Code).StartsWith(CountryService.TrimCountry(searchValue)))
                .Select(s => s.Name)
                .Take(2));

            results.ReplaceOrAddToList(countryResults
                .Where(w => CountryService.TrimCountry(w.Name).StartsWith(CountryService.TrimCountry(searchValue)))
                .Select(s => s.Name)
                .Take(3));

            results.ReplaceOrAddToList(countryResults
                .Where(w => CountryService.TrimCountry(w.Name).Contains(CountryService.TrimCountry(searchValue)))
                .Select(s => s.Name)
                .Take(2));

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
