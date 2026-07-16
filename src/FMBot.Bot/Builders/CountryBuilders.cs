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
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Images.Generators;
using NetCord.Rest;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class CountryBuilders(
    CountryService countryService,
    UserService userService,
    IDataSourceFactory dataSourceFactory,
    ArtistsService artistsService,
    PlayService playService,
    PuppeteerService puppeteerService,
    MusicDataFactory musicDataFactory)
{
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
                                 await userService.GetReferencedMusic(context.ReferencedMessage.Id);

            if (internalLookup?.Artist != null)
            {
                countryOptions = internalLookup.Artist;
            }
        }

        CountryInfo country = null;
        if (string.IsNullOrWhiteSpace(countryOptions))
        {
            var recentTracks = await dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, 1,
                true, context.ContextUser.SessionKeyLastFm);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks,
                    context.ContextUser.UserNameLastFM, context.Localizer);
            }

            var artistName = recentTracks.Content.RecentTracks.First().ArtistName;

            var foundCountry = countryService.GetValidCountry(artistName);

            if (foundCountry == null)
            {
                var artistCall =
                    await dataSourceFactory.GetArtistInfoAsync(artistName, context.ContextUser.UserNameLastFM);
                if (artistCall.Success)
                {
                    var cachedArtist = await musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);

                    switch (cachedArtist)
                    {
                        case { Mbid: null }:
                            response.Embed.WithDescription($"Sorry, the artist **{StringExtensions.Sanitize(cachedArtist.Name)}** was not found on MusicBrainz.");
                            response.CommandResponse = CommandResponse.NotFound;
                            response.ResponseType = ResponseType.Embed;
                            return response;
                        case { CountryCode: null }:
                            response.Embed.WithDescription($"Sorry, the artist **{StringExtensions.Sanitize(cachedArtist.Name)}** does not have a country associated with them on MusicBrainz.");
                            response.CommandResponse = CommandResponse.NotFound;
                            response.ResponseType = ResponseType.Embed;
                            return response;
                    }

                    country = countryService.GetValidCountry(cachedArtist.CountryCode);
                }
            }
            else
            {
                country = foundCountry;
            }

            if (country != null)
            {
                var artist = await artistsService.GetArtistFromDatabase(artistName);

                if (artist?.CountryCode == null)
                {
                    response.Embed.WithDescription(
                        artist == null
                            ? "Sorry, the country or artist you're searching for does not exist in our database."
                            : $"Sorry, the artist **{StringExtensions.Sanitize(artist.Name)}** does not have a country associated with them on MusicBrainz.");

                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                if (artist.SpotifyImageUrl != null)
                {
                    response.Embed.WithThumbnail(artist.SpotifyImageUrl);

                    var accentColor = await artistsService.GetArtistAccentColorAsync(
                        artist.SpotifyImageUrl, artist.Id, artist.Name);
                    response.Embed.WithColor(accentColor);
                }

                response.ReferencedMusic = new ReferencedMusic { Artist = artist.Name };

                var description = new StringBuilder();
                foundCountry = countryService.GetValidCountry(artist.CountryCode);

                if (foundCountry == null)
                {
                    response.Embed.WithDescription(
                        $"Sorry, the artist **{StringExtensions.Sanitize(artist.Name)}** has a country code (`{StringExtensions.Sanitize(artist.CountryCode)}`) that we couldn't resolve to a country.");
                    response.CommandResponse = CommandResponse.NotFound;
                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                description.AppendLine(
                    $"### :flag_{foundCountry.Code.ToLower()}: {artist.Name}");
                description.AppendLine(
                    $"From **{foundCountry.Name}** ");

                if (artist.Location != null &&
                    !string.Equals(artist.Location, foundCountry.Name, StringComparison.OrdinalIgnoreCase))
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
            var foundCountry = countryService.GetValidCountry(countryOptions);

            if (foundCountry == null)
            {
                var artist = await artistsService.GetArtistFromDatabase(countryOptions);

                if (artist is { CountryCode: null })
                {
                    var artistCall =
                        await dataSourceFactory.GetArtistInfoAsync(artist.Name,
                            context.ContextUser.UserNameLastFM);
                    if (artistCall.Success)
                    {
                        artist = await musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);
                    }
                }

                if (artist?.CountryCode != null)
                {
                    var description = new StringBuilder();

                    if (artist.SpotifyImageUrl != null)
                    {
                        response.Embed.WithThumbnail(artist.SpotifyImageUrl);

                        var accentColor = await artistsService.GetArtistAccentColorAsync(
                            artist.SpotifyImageUrl, artist.Id, artist.Name);
                        response.Embed.WithColor(accentColor);
                    }

                    foundCountry = countryService.GetValidCountry(artist.CountryCode);

                    if (foundCountry == null)
                    {
                        response.Embed.WithDescription(
                            $"Sorry, the artist **{StringExtensions.Sanitize(artist.Name)}** has a country code (`{StringExtensions.Sanitize(artist.CountryCode)}`) that we couldn't resolve to a country.");
                        response.CommandResponse = CommandResponse.NotFound;
                        response.ResponseType = ResponseType.Embed;
                        return response;
                    }

                    description.AppendLine(
                        $"### :flag_{foundCountry.Code.ToLower()}: {artist.Name}");
                    description.AppendLine(
                        $"From **{foundCountry.Name}** ");

                    if (artist.Location != null && !string.Equals(artist.Location, foundCountry.Name,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        description.AppendLine(
                            $"*{artist.Location}*");
                    }

                    response.ReferencedMusic = new ReferencedMusic { Artist = artist.Name };
                    response.Embed.WithDescription(description.ToString());

                    response.Embed.WithFooter($"Country source: MusicBrainz\n" +
                                              $"Add a country to this command to see top artists");

                    response.ResponseType = ResponseType.Embed;
                    return response;
                }

                response.Embed.WithDescription(
                    artist == null
                        ? "Sorry, the country or artist you're searching for does not exist in our database."
                        : $"Sorry, the artist **{StringExtensions.Sanitize(artist.Name)}** does not have a country associated with them on MusicBrainz.");
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

        var countryArtists = await countryService.GetUserArtistsForCountry(context.ContextUser.UserId, country.Code);

        if (!countryArtists.Any())
        {
            response.Embed.WithDescription("Sorry, we couldn't find any top artists for your selected country.");
            response.CommandResponse = CommandResponse.NotFound;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var userTitle = await userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
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
                countryPageString.AppendLine(
                    $"{counter}. **{genreArtist.ArtistName}** - *{genreArtist.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(genreArtist.UserPlaycount)}*");
                counter++;
            }

            if (country.Code == "UA")
            {
                countryPageString.AppendLine();
                countryPageString.AppendLine(
                    "<:ukraine:948301778464694272> [Stand For Ukraine](https://standforukraine.com/)");
            }

            var footer = $"Country source: MusicBrainz\n" +
                         $"Page {pageCounter}/{countryPages.Count} - {countryArtists.Count.Format(context.NumberFormat)} total artists - {countryArtists.Sum(s => s.UserPlaycount).Format(context.NumberFormat)} total scrobbles";

            pages.Add(new PageBuilder()
                .WithDescription(countryPageString.ToString())
                .WithTitle(title)
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithUrl(
                    $"{LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}/library/artists?date_preset=ALL")
                .WithFooter(footer));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public async Task<ResponseModel> TopCountriesAsync(ContextModel context,
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

        var userTitle = await userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        if (userSettings.DifferentUser)
        {
            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist countries for {userTitle}");
        response.EmbedAuthor.WithUrl(
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}");

        Response<TopArtistList> artists;
        var previousTopArtists = new List<TopArtist>();

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000, useCache: true);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "topgenres", context.Localizer, context.DiscordUser);
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
                    TopArtists = await artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                }
            };
        }
        else
        {
            artists = new Response<TopArtistList>
            {
                Content = await playService.GetUserTopArtists(userSettings.UserId,
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
            var previousArtistsCall = await dataSourceFactory
                .GetTopArtistsForCustomTimePeriodAsync(userSettings.UserNameLastFm,
                    timeSettings.BillboardStartDateTime.Value, timeSettings.BillboardEndDateTime.Value, 200);

            if (previousArtistsCall.Success)
            {
                previousTopArtists.AddRange(previousArtistsCall.Content.TopArtists);
            }
        }

        var countries = await countryService.GetTopCountriesForTopArtists(artists.Content.TopArtists, true);
        var previousTopCountries = await countryService.GetTopCountriesForTopArtists(previousTopArtists, true);

        if (mode == ResponseMode.Image && countries.Any())
        {
            var totalPlays = await dataSourceFactory.GetScrobbleCountFromDateAsync(userSettings.UserNameLastFm,
                timeSettings.TimeFrom,
                userSettings.SessionKeyLastFm, timeSettings.TimeUntil);
            artists.Content.TopArtists = await artistsService.FillArtistImages(artists.Content.TopArtists);

            var validArtists = countries.First().Artists.Select(s => s.ArtistName.ToLower()).ToArray();
            var firstArtistImage =
                artists.Content.TopArtists
                    .FirstOrDefault(f => validArtists.Contains(f.ArtistName.ToLower()) && f.ArtistImageUrl != null)
                    ?.ArtistImageUrl;

            using var image = await puppeteerService.GetTopList(userTitle, "Top Countries", "countries",
                timeSettings.Description,
                countries.Count, totalPlays.GetValueOrDefault(), firstArtistImage,
                countryService.GetTopListForTopCountries(countries), context.NumberFormat);

            var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            response.Stream = encoded.AsStream(true);
            response.FileName = $"top-countries-{userSettings.UserId}.png";
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
                    $"**{country.CountryName ?? country.CountryCode}** - *{country.UserPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(country.UserPlaycount)}*";

                if (topListSettings.Billboard && previousTopCountries.Any())
                {
                    var previousTopGenre =
                        previousTopCountries.FirstOrDefault(f => f.CountryCode == country.CountryCode);
                    int? previousPosition =
                        previousTopGenre == null ? null : previousTopCountries.IndexOf(previousTopGenre);

                    countryPageString.AppendLine(StringService.GetBillboardLine($"`{country.Artists.Count}` · {name}",
                        counter - 1, previousPosition, false).Text);
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

            if (rnd == 1 && !topListSettings.Billboard && context.SelectMenu == null)
            {
                footer.AppendLine("View as billboard by adding 'billboard' or 'bb'");
            }

            pages.Add(new PageBuilder()
                .WithDescription(countryPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithColor(DiscordConstants.LastFmColorRed)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages, selectMenuBuilder: context.SelectMenu);

        return response;
    }

    public async Task<ResponseModel> GetTopCountryChart(
        ContextModel context,
        UserSettingsModel userSettings,
        TimeSettingsModel timeSettings,
        CountryChartTheme theme = CountryChartTheme.Dark)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed
        };

        var url =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{timeSettings.UrlParameter}";
        var embedTitle = new StringBuilder();
        embedTitle.Append(
            $"[Top {timeSettings.Description.ToLower()} artist]({url}) countries for {userSettings.DisplayName}");

        Response<TopArtistList> artists;

        if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
        {
            artists = await dataSourceFactory.GetTopArtistsAsync(userSettings.UserNameLastFm,
                timeSettings, 1000);

            if (!artists.Success || artists.Content == null)
            {
                response.Embed.ErrorResponse(artists.Error, artists.Message, "countrychart", context.Localizer, context.DiscordUser);
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
                    TopArtists = await artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                }
            };
        }
        else
        {
            artists = new Response<TopArtistList>
            {
                Content = await playService.GetUserTopArtists(userSettings.UserId,
                    timeSettings.PlayDays.GetValueOrDefault())
            };
        }

        if (artists.Content.TopArtists == null || artists.Content.TopArtists.Count == 0)
        {
            response.Embed.WithDescription(
                $"Sorry, you or the user you're searching for don't have enough top artists in the selected time period.\n\n" +
                $"Please try again later or try a different time period.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.NoScrobbles;
            response.ResponseType = ResponseType.Embed;
            return response;
        }

        var countries = await countryService.GetTopCountriesForTopArtists(artists.Content.TopArtists, true);

        using var image = await puppeteerService.GetWorldArtistMap(countries, theme);
        var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        response.Stream = encoded.AsStream(true);
        response.FileName = "artist-map.png";

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"));

        response.ComponentsContainer.AddComponent(new MediaGalleryProperties
        {
            mediaGallery
        });

        var themeMenu = new StringMenuProperties(InteractionConstants.CountryChartTheme)
            .WithPlaceholder("Change map theme")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var themeOption in Enum.GetValues<CountryChartTheme>())
        {
            var value =
                $"{Enum.GetName(themeOption)}-{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}-{timeSettings.Description}";

            themeMenu.AddOptions(new StringMenuSelectOptionProperties(themeOption.ToString(), value)
            {
                Default = themeOption == theme
            });
        }

        response.ComponentsContainer.AddComponent(themeMenu);
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        return response;
    }
}
