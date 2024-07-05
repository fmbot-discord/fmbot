using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Images.Generators;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class CountryBuilders
{
    private readonly CountryService _countryService;
    private readonly UserService _userService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly ArtistsService _artistsService;
    private readonly PlayService _playService;
    private readonly PuppeteerService _puppeteerService;
    private readonly MusicDataFactory _musicDataFactory;

    public CountryBuilders(CountryService countryService,
        UserService userService,
        IDataSourceFactory dataSourceFactory,
        ArtistsService artistsService,
        PlayService playService,
        PuppeteerService puppeteerService,
        MusicDataFactory musicDataFactory)
    {
        this._countryService = countryService;
        this._userService = userService;
        this._dataSourceFactory = dataSourceFactory;
        this._artistsService = artistsService;
        this._playService = playService;
        this._puppeteerService = puppeteerService;
        this._musicDataFactory = musicDataFactory;
    }

    public async Task<ResponseModel> CountryAsync(
        ContextModel context,
        string countryOptions)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ReferencedMessage != null)
        {
            var internalLookup = CommandContextExtensions.GetReferencedMusic(context.ReferencedMessage.Id)
                                 ??
                                 await this._userService.GetReferencedMusic(context.ReferencedMessage.Id);

            if (internalLookup?.Artist != null)
            {
                countryOptions = internalLookup.Artist;
            }
        }

        CountryInfo country = null;
        if (string.IsNullOrWhiteSpace(countryOptions))
        {
            var recentTracks = await this._dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1, true, context.ContextUser.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, context.ContextUser.UserNameLastFM);
            }

            var artistName = recentTracks.Content.RecentTracks.First().ArtistName;

            var foundCountry = this._countryService.GetValidCountry(artistName);

            if (foundCountry == null)
            {
                var artistCall = await this._dataSourceFactory.GetArtistInfoAsync(artistName, context.ContextUser.UserNameLastFM);
                if (artistCall.Success)
                {
                    var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);

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
                        artist == null
                            ? "Sorry, the country or artist you're searching for does not exist."
                            : "Sorry, the artist you're searching for does not have a country associated with them on MusicBrainz.");

                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                if (artist.SpotifyImageUrl != null)
                {
                    response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                }

                PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, artist.Name);

                var description = new StringBuilder();
                foundCountry = this._countryService.GetValidCountry(artist.CountryCode);

                description.AppendLine(
                    $"### :flag_{foundCountry.Code.ToLower()}: {artist.Name}");
                description.AppendLine(
                    $"From **{foundCountry.Name}** ");

                if (artist.Location != null && !string.Equals(artist.Location, foundCountry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    description.AppendLine(
                        $"*{artist.Location}*");
                }

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

                if (artist is { CountryCode: null })
                {
                    var artistCall = await this._dataSourceFactory.GetArtistInfoAsync(artist.Name, context.ContextUser.UserNameLastFM);
                    if (artistCall.Success)
                    {
                        artist = await this._musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);
                    }
                }

                if (artist?.CountryCode != null)
                {
                    var description = new StringBuilder();

                    if (artist.SpotifyImageUrl != null)
                    {
                        response.Embed.WithThumbnailUrl(artist.SpotifyImageUrl);
                    }

                    foundCountry = this._countryService.GetValidCountry(artist.CountryCode);

                    description.AppendLine(
                        $"### :flag_{foundCountry.Code.ToLower()}: {artist.Name}");
                    description.AppendLine(
                        $"From **{foundCountry.Name}** ");

                    if (artist.Location != null && !string.Equals(artist.Location, foundCountry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        description.AppendLine(
                            $"*{artist.Location}*");
                    }

                    PublicProperties.UsedCommandsArtists.TryAdd(context.InteractionId, artist.Name);
                    response.Embed.WithDescription(description.ToString());

                    response.Embed.WithFooter($"Country source: MusicBrainz\n" +
                                           $"Add a country to this command to see top artists");

                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                response.Embed.WithDescription(
                    artist == null
                        ? "Sorry, the country or artist you're searching for does not exist."
                        : "Sorry, the artist you're searching for does not have a country associated with them on MusicBrainz.");
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

        var title = $":flag_{country.Code.ToLower()}: Top artists from {country.Name} for {userTitle}";

        var countryPages = countryArtists.ChunkBy(10);

        var counter = 1;
        var pageCounter = 1;
        foreach (var countryPage in countryPages)
        {
            var countryPageString = new StringBuilder();
            foreach (var genreArtist in countryPage)
            {
                countryPageString.AppendLine($"{counter}. **{genreArtist.ArtistName}** - *{genreArtist.UserPlaycount} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)}*");
                counter++;
            }

            if (country.Code == "UA")
            {
                countryPageString.AppendLine();
                countryPageString.AppendLine("<:ukraine:948301778464694272> [Stand For Ukraine](https://standforukraine.com/)");
            }

            var footer = $"Country source: MusicBrainz\n" +
                         $"Page {pageCounter}/{countryPages.Count} - {countryArtists.Count} total artists - {countryArtists.Sum(s => s.UserPlaycount)} total scrobbles";

            pages.Add(new PageBuilder()
                .WithDescription(countryPageString.ToString())
                .WithTitle(title)
                .WithUrl($"{LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}/library/artists?date_preset=ALL")
                .WithFooter(footer));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> GetTopCountries(ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        TopListSettings topListSettings,
        ResponseMode mode)
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
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}");

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
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
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        if (topListSettings.Billboard && timeSettings.BillboardStartDateTime.HasValue &&
            timeSettings.BillboardEndDateTime.HasValue)
        {
            var previousArtistsCall = await this._dataSourceFactory
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var countries = await this._countryService.GetTopCountriesForTopArtists(artists.Content.TopArtists, true);
        var previousTopCountries = await this._countryService.GetTopCountriesForTopArtists(previousTopArtists, true);

        if (mode == ResponseMode.Image && countries.Any())
        {
            var totalPlays = await this._dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm, timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            artists.Content.TopArtists = await this._artistsService.FillArtistImages(artists.Content.TopArtists);

            var validArtists = countries.First().Artists.Select(s => s.ArtistName.ToLower()).ToArray();
            var firstArtistImage =
                artists.Content.TopArtists.FirstOrDefault(f => validArtists.Contains(f.ArtistName.ToLower()) && f.ArtistImageUrl != null)?.ArtistImageUrl;

            var image = await this._puppeteerService.GetTopList(userTitle, "Top Countries", "countries", timeSettings.Description,
                countries.Count, totalPlays.GetValueOrDefault(), firstArtistImage,
                this._countryService.GetTopListForTopCountries(countries));

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream();
            response.FileName = $"top-countries-{userSettings.DiscordUserId}";
            response.ResponseType = ResponseType.ImageOnly;

            return response;
        }

        var countryPages = countries.ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageCounter = 1;
        var rnd = new Random().Next(0, 4);

        foreach (var countryPage in countryPages)
        {
            var countryPageString = new StringBuilder();
            foreach (var country in countryPage)
            {
                var name =
                    $"**{country.CountryName ?? country.CountryCode}** - *{country.UserPlaycount} {StringExtensions.GetPlaysString(country.UserPlaycount)}*";

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
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}");
        response.Embed.WithAuthor(response.EmbedAuthor);

        Response<TopArtistList> artists;

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await this._dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "topgenres", context.DiscordUser);
                response.CommandResponse = CommandResponse.LastFmError;
                response.ResponseType = ResponseType.Embed;
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
            response.ResponseType = ResponseType.Embed;
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
