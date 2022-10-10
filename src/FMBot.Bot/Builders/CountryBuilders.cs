using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Images.Generators;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class CountryBuilders
{
    private readonly CountryService _countryService;
    private readonly UserService _userService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly ArtistsService _artistsService;
    private readonly PlayService _playService;
    private readonly PuppeteerService _puppeteerService;
    private readonly SpotifyService _spotifyService;

    public CountryBuilders(CountryService countryService, UserService userService, LastFmRepository lastFmRepository,
        ArtistsService artistsService, PlayService playService, PuppeteerService puppeteerService, SpotifyService spotifyService)
    {
        this._countryService = countryService;
        this._userService = userService;
        this._lastFmRepository = lastFmRepository;
        this._artistsService = artistsService;
        this._playService = playService;
        this._puppeteerService = puppeteerService;
        this._spotifyService = spotifyService;
    }

    public async Task<ResponseModel> CountryAsync(
        ContextModel context,
        string countryOptions)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        CountryInfo country = null;
        if (string.IsNullOrWhiteSpace(countryOptions))
        {
            var recentTracks = await this._lastFmRepository.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1, true, context.ContextUser.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                var errorEmbed =
                    GenericEmbedService.RecentScrobbleCallFailedBuilder(recentTracks, context.ContextUser.UserNameLastFM);
                response.Embed = errorEmbed;
                response.CommandResponse = CommandResponse.LastFmError;
                return response;
            }

            var artistName = recentTracks.Content.RecentTracks.First().ArtistName;

            var foundCountry = this._countryService.GetValidCountry(artistName);

            if (foundCountry == null)
            {
                var artistCall = await this._lastFmRepository.GetArtistInfoAsync(artistName, context.ContextUser.UserNameLastFM);
                if (artistCall.Success)
                {
                    var cachedArtist = await this._spotifyService.GetOrStoreArtistAsync(artistCall.Content);

                    if (cachedArtist.CountryCode != null)
                    {
                        var artistCountry = this._countryService.GetValidCountry(cachedArtist.CountryCode);
                        country = artistCountry;
                    }
                }
            }
            else
            {
                country = foundCountry;
            }

            if (country != null)
            {
                var artist = await this._artistsService.GetArtistFromDatabase(artistName);

                if (artist?.CountryCode == null)
                {
                    response.Embed.WithDescription(
                        "Sorry, the country or artist you're searching for do not exist or do not have a country associated with them on MusicBrainz.");

                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                if (artist?.SpotifyImageUrl != null)
                {
                    response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                }

                var description = new StringBuilder();
                foundCountry = this._countryService.GetValidCountry(artist.CountryCode);

                description.AppendLine(
                    $"**{artist.Name}** is from **{foundCountry.Name}** :flag_{foundCountry.Code.ToLower()}:");


                response.Embed.WithDescription(description.ToString());

                response.Embed.WithFooter($"Country source: MusicBrainz\n" +
                                       $"Add a country to this command to see top artists");

                response.ResponseType = ResponseType.Embed;
                return response;
            }
        }
        else
        {
            var foundCountry = this._countryService.GetValidCountry(countryOptions);

            if (foundCountry == null)
            {
                var artist = await this._artistsService.GetArtistFromDatabase(countryOptions);

                if (artist?.CountryCode != null)
                {
                    var description = new StringBuilder();

                    if (artist.SpotifyImageUrl != null)
                    {
                        response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                    }

                    foundCountry = this._countryService.GetValidCountry(artist.CountryCode);

                    description.AppendLine(
                        $"**{artist.Name}** is from **{foundCountry.Name}** :flag_{foundCountry.Code.ToLower()}:");

                    response.Embed.WithDescription(description.ToString());

                    response.Embed.WithFooter($"Country source: MusicBrainz\n" +
                                           $"Add a country to this command to see top artists");

                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                response.Embed.WithDescription(
                    "Sorry, the country or artist you're searching for do not exist or do not have a country associated with them on MusicBrainz.");
                response.CommandResponse = CommandResponse.NotFound;
                response.ResponseType = ResponseType.Embed;
                return response;
            }

            country = foundCountry;
        }

        if (country == null)
        {
            response.Embed.WithDescription(
                "Sorry, we don't have a registered country for the artist you're currently listening to.\n\n" +
                $"Please try again later or manually enter a country (example: `{context.Prefix}country Netherlands`)");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(context.ContextUser.UserId, true);
        if (topArtists.Count < 100)
        {
            response.Embed.WithDescription($"Sorry, you don't have enough top artists yet to use this command (must have at least 100 - you have {topArtists.Count}).\n\n" +
                                        "Please try again later.");
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var countryArtists = await this._countryService.GetTopArtistsForCountry(country.Code, topArtists);

        if (!countryArtists.Any())
        {
            response.Embed.WithDescription("Sorry, we couldn't find any top artists for your selected country.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var pages = new List<PageBuilder>();

        var title = $"Top artists from {country.Name} :flag_{country.Code.ToLower()}: for {userTitle}";

        var genrePages = countryArtists.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var genrePage in genrePages)
        {
            var genrePageString = new StringBuilder();
            foreach (var genreArtist in genrePage)
            {
                genrePageString.AppendLine($"{counter}. **{genreArtist.ArtistName}** ({genreArtist.UserPlaycount} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)})");
                counter++;
            }

            if (country.Code == "UA")
            {
                genrePageString.AppendLine();
                genrePageString.AppendLine("<:ukraine:948301778464694272> [Stand For Ukraine](https://standforukraine.com/)");
            }

            var footer = $"Country source: MusicBrainz\n" +
                         $"Page {pageCounter}/{genrePages.Count} - {countryArtists.Count} total artists - {countryArtists.Sum(s => s.UserPlaycount)} total scrobbles";

            pages.Add(new PageBuilder()
                .WithDescription(genrePageString.ToString())
                .WithTitle(title)
                .WithUrl($"{Constants.LastFMUserUrl}{context.ContextUser.UserNameLastFM}/library/artists?date_preset=ALL")
                .WithFooter(footer));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> GetTopCountries(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        TopListSettings topListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        var pages = new List<PageBuilder>();

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (!userSettings.DifferentUser)
        {
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist countries for {userTitle}");
        response.EmbedAuthor.WithUrl(
            $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "topgenres", context.DiscordUser);
                response.CommandResponse = CommandResponse.LastFmError;
                return response;
            }
        }
        else if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            artists = new Response<TopArtistList>
            {
                Content = new TopArtistList
                {
                    TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                }
            };
        }
        else
        {
            artists = new Response<TopArtistList>
            {
                Content = await this._playService.GetUserTopArtists(userSettings.UserId,
                    timeSettings.PlayDays.GetValueOrDefault())
            };
        }

        if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                $"Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._lastFmRepository
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var countries = await this._countryService.GetTopCountriesForTopArtists(artists.Content.TopArtists, true);
        var previousTopCountries = await this._countryService.GetTopCountriesForTopArtists(previousTopArtists, true);

        var countryPages = countries.ChunkBy(topListSettings.ExtraLarge
            ? Constants.DefaultExtraLargePageSize
            : Constants.DefaultPageSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var countryPage in countryPages)
        {
            var countryPageString = new StringBuilder();
            foreach (var country in countryPage)
            {
                var name =
                    $"**{country.CountryName ?? country.CountryCode}** ({country.UserPlaycount} {StringExtensions.GetPlaysString(country.UserPlaycount)})";

                if (topListSettings.Billboard && previousTopCountries.Any())
                {
                    var previousTopGenre =
                        previousTopCountries.FirstOrDefault(f => f.CountryCode == country.CountryCode);
                    int? previousPosition =
                        previousTopGenre == null ? null : previousTopCountries.IndexOf(previousTopGenre);

                    countryPageString.AppendLine(StringService.GetBillboardLine($"`{country.Artists.Count}` · {name}", counter - 1, previousPosition, false).Text);
                }
                else
                {
                    countryPageString.Append($"`{country.Artists.Count}` · ");
                    countryPageString.AppendLine(name);
                }

                counter++;
            }

            var footer = new StringBuilder();
            footer.AppendLine("Country source: Musicbrainz");
            footer.AppendLine($"Ordered by artists per country");
            footer.AppendLine($"Page {pageCounter}/{countryPages.Count} - {countries.Count} total countries");

            if (topListSettings.Billboard)
            {
                footer.AppendLine(StringService.GetBillBoardSettingString(timeSettings, userSettings.RegisteredLastFm));
            }

            if (rnd == 1 && !topListSettings.Billboard)
            {
                footer.AppendLine("View this list as a billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(countryPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    public async Task<ResponseModel> GetTopCountryChart(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        TopListSettings topListSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (!userSettings.DifferentUser)
        {
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }
        }
        else
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist countries for {userTitle}");
        response.EmbedAuthor.WithUrl(
            $"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");
        response.Embed.WithAuthor(response.EmbedAuthor);

        Response<TopArtistList> artists;

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "topgenres", context.DiscordUser);
                response.CommandResponse = CommandResponse.LastFmError;
                return response;
            }
        }
        else if (timeSettings.TimePeriod == TimePeriod.AllTime)
        {
            artists = new Response<TopArtistList>
            {
                Content = new TopArtistList
                {
                    TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                }
            };
        }
        else
        {
            artists = new Response<TopArtistList>
            {
                Content = await this._playService.GetUserTopArtists(userSettings.UserId,
                    timeSettings.PlayDays.GetValueOrDefault())
            };
        }

        if (artists.Content.TopArtists == null || !artists.Content.TopArtists.Any())
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                $"Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            return response;
        }

        var countries = await this._countryService.GetTopCountriesForTopArtists(artists.Content.TopArtists, true);

        response.Embed.WithFooter($"Country source: Musicbrainz");

        var image = await this._puppeteerService.GetWorldArtistMap(countries);

        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream();
        response.FileName = "artist-map";

        return response;
    }
}
