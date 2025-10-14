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

public class CountryArtistAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private readonly ArtistsService _artistsService;
    private readonly CountryService _countryService;

    public CountryArtistAutoComplete(ArtistsService artistsService, CountryService countryService)
    {
        this._artistsService = artistsService;
        this._countryService = countryService;
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
                results.Add("Start typing to search through countries or artists...");

                return new List<ApplicationCommandOptionChoiceProperties>(results.Select(s =>
                    new ApplicationCommandOptionChoiceProperties(s, s)));
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
            var searchValue = option.Value;
            results = [searchValue];

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
