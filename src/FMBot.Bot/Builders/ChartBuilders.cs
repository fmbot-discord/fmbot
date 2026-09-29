using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Bot.Extensions;
using FMBot.Core.Charts;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using NetCord;
using NetCord.Rest;
using SkiaSharp;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class ChartBuilders
{
    private readonly ChartService _chartService;
    private readonly SupporterService _supporterService;
    private readonly ChartDataService _chartDataService;

    public ChartBuilders(ChartService chartService,
        SupporterService supporterService,
        ChartDataService chartDataService)
    {
        this._chartService = chartService;
        this._supporterService = supporterService;
        this._chartDataService = chartDataService;
    }

    private static ResponseModel BuildChartValidationError(
        ContextModel context,
        string message,
        string chartType,
        ChartSettings chartSettings,
        string userNameLastFm)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
            CommandResponse = CommandResponse.WrongInput
        };

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                context.DiscordUser.Id, chartType, chartSettings, userNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(message)]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(message));
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);

        return response;
    }

    public async Task<ResponseModel> AlbumChartAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        ChartSettings chartSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed,
        };

        var data = await this._chartDataService.GetAlbumsAsync(chartSettings, userSettings.UserId,
            userSettings.UserNameLastFm);

        switch (data.Status)
        {
            case ChartDataStatus.TooManyImages:
                return BuildChartValidationError(context,
                    context.Localize("chart.tooManyImages", ("max", ChartRenderer.MaxImages.ToString()),
                        ("size", $"{(int)Math.Sqrt(ChartRenderer.MaxImages)}x{(int)Math.Sqrt(ChartRenderer.MaxImages)}")),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            case ChartDataStatus.NotEnough:
            {
                var count = data.Amount;
                var reply = new StringBuilder();
                if (chartSettings.FilteredArtist != null)
                {
                    reply.AppendLine(context.Localize("chart.notEnoughAlbumsArtist",
                        ("amount", count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("artist", chartSettings.FilteredArtist.Name),
                        ("url", LastfmUrlExtensions.GetArtistUrl(chartSettings.FilteredArtist.Name)),
                        ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                    reply.AppendLine();
                    reply.AppendLine(context.Localize("chart.tryDifferentArtistFilter",
                        ("periods", Constants.CompactTimePeriodList)));
                }
                else if (chartSettings.HasGenreFilter)
                {
                    reply.AppendLine(context.LocalizeCount("chart.notEnoughAlbumsGenre",
                        chartSettings.FilteredGenres.Count,
                        ("amount", count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("genres", string.Join("**, **", chartSettings.FilteredGenres.Select(StringExtensions.Sanitize))),
                        ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                    reply.AppendLine();
                    reply.AppendLine(context.Localize("chart.tryDifferentGenres",
                        ("periods", Constants.CompactTimePeriodList)));
                }
                else
                {
                    reply.AppendLine(context.Localize("chart.notEnoughAlbums",
                        ("amount", count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("period", context.Localizer.PeriodLabel(chartSettings.TimeSettings))));
                    reply.AppendLine();
                    reply.AppendLine(context.Localize("chart.tryDifferent",
                        ("periods", Constants.CompactTimePeriodList)));
                }

                if (chartSettings.SkipWithoutImage && chartSettings.FilteredArtist == null && !chartSettings.HasGenreFilter)
                {
                    reply.AppendLine();
                    reply.AppendLine(context.Localize("chart.extraAlbumsRequired",
                        ("amount", data.Extra.ToString())));
                }

                return BuildChartValidationError(context, reply.ToString(),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            }
            case ChartDataStatus.NotEnoughReleaseYear:
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughReleaseYear",
                        ("year", chartSettings.ReleaseYearFilter.Value.ToString()),
                        ("amount", data.Amount.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            case ChartDataStatus.NotEnoughReleaseDecade:
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughReleaseDecade",
                        ("decade", chartSettings.ReleaseDecadeFilter.Value.ToString()),
                        ("amount", data.Amount.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
            case ChartDataStatus.NotEnoughNonSingles:
                return BuildChartValidationError(context,
                    context.Localize("chart.notEnoughNonSingles",
                        ("amount", data.Amount.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList)),
                    InteractionConstants.Chart.AlbumType, chartSettings, userSettings.UserNameLastFm);
        }

        var url =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/albums?{chartSettings.TimespanUrlString}";
        var embedTitle = new StringBuilder();
        embedTitle.Append(context.Localize("chart.albumChartTitle",
            ("size", $"{chartSettings.Width}x{chartSettings.Height}"),
            ("timespan", context.Localizer.PeriodLabel(chartSettings.TimeSettings)),
            ("url", url),
            ("user", userSettings.DisplayName)));

        var embedDescription = new StringBuilder();

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix,
            context.Localizer);

        var nsfwAllowed = context.DiscordChannel.NsfwAllowed(context.DiscordGuild);
        using var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                context.LocalizeCount("chart.albumsFiltered", chartSettings.CensoredItems.Value));
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayProperties(context.Localize("chart.containsNsfwCovers")));
        }

        response.FileDescription = StringExtensions.TruncateLongString(chartSettings.FileDescription.ToString(), 1024);
        response.FileName =
            $"album-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}{ChartRenderer.ChartFileExtension}";

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            {
                Description = StringExtensions.TruncateLongString(response.FileDescription, 256),
                Spoiler = chartSettings.ContainsNsfw
            };

        response.ComponentsContainer.AddComponent(new MediaGalleryProperties
        {
            mediaGallery
        });

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(embedDescription.ToString()));
        }

        var footerText = !userSettings.DifferentUser
            ? context.LocalizeCount("chart.userScrobbles", context.ContextUser.TotalPlaycount.GetValueOrDefault(),
                ("user", userSettings.UserNameLastFm))
            : context.Localize("chart.requestedBy",
                ("user", await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)));

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                               context.DiscordUser.Id, InteractionConstants.Chart.AlbumType,
                               chartSettings, userSettings.UserNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(footerText)]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(footerText));
        }

        var encoded = ChartRenderer.EncodeChart(chart);
        response.Stream = encoded.AsStream(true);
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.StringMenus.Add(context.SelectMenu);
        }

        if (supporter != null)
        {
            var actionRow = new ActionRowProperties();
            actionRow.WithButton(context.Localize("buttons.getFmbotSupporter"),
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "chart-broughtby"),
                style: ButtonStyle.Secondary);
            response.ComponentsV2.AddComponent(actionRow);
        }

        return response;
    }

    public async Task<ResponseModel> ArtistChartAsync(
        ContextModel context,
        UserSettingsModel userSettings,
        ChartSettings chartSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ImageWithEmbed,
        };

        var data = await this._chartDataService.GetArtistsAsync(chartSettings, userSettings.UserNameLastFm);

        switch (data.Status)
        {
            case ChartDataStatus.TooManyImages:
                return BuildChartValidationError(context,
                    context.Localize("chart.tooManyImages", ("max", ChartRenderer.MaxImages.ToString()),
                        ("size", $"{(int)Math.Sqrt(ChartRenderer.MaxImages)}x{(int)Math.Sqrt(ChartRenderer.MaxImages)}")),
                    InteractionConstants.Chart.ArtistType, chartSettings, userSettings.UserNameLastFm);
            case ChartDataStatus.NotEnough:
            {
                var count = data.Amount;

                string reply;
                if (chartSettings.HasGenreFilter)
                {
                    reply = context.LocalizeCount("chart.notEnoughArtistsGenre",
                        chartSettings.FilteredGenres.Count,
                        ("amount", count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("genres", string.Join("**, **", chartSettings.FilteredGenres.Select(g => StringExtensions.Sanitize(g)))),
                        ("periods", Constants.CompactTimePeriodList));
                }
                else
                {
                    reply = context.Localize("chart.notEnoughArtists",
                        ("amount", count.ToString()),
                        ("required", chartSettings.ImagesNeeded.ToString()),
                        ("periods", Constants.CompactTimePeriodList));

                    if (chartSettings.SkipWithoutImage)
                    {
                        reply += "\n\n" + context.Localize("chart.extraArtistsRequired",
                            ("amount", data.Extra.ToString()));
                    }
                }

                return BuildChartValidationError(context, reply,
                    InteractionConstants.Chart.ArtistType, chartSettings, userSettings.UserNameLastFm);
            }
        }

        var url =
            $"{LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}/library/artists?{chartSettings.TimespanUrlString}";

        var embedTitle = new StringBuilder();

        embedTitle.Append(context.Localize("chart.artistChartTitle",
            ("size", $"{chartSettings.Width}x{chartSettings.Height}"),
            ("timespan", context.Localizer.PeriodLabel(chartSettings.TimeSettings)),
            ("url", url),
            ("user", userSettings.DisplayName)));

        var embedDescription = new StringBuilder();

        var footer = new StringBuilder();
        if (!userSettings.DifferentUser)
        {
            footer.AppendLine(context.LocalizeCount("chart.userScrobbles",
                context.ContextUser.TotalPlaycount.GetValueOrDefault(),
                ("user", userSettings.UserNameLastFm)));
        }
        else
        {
            footer.AppendLine(context.Localize("chart.requestedBy",
                ("user", await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser))));
        }

        footer.AppendLine(context.Localize("chart.imageSource"));

        var supporter =
            await this._supporterService.GetRandomSupporter(context.DiscordGuild, context.ContextUser.UserType);
        ChartService.AddSettingsToDescription(chartSettings, embedDescription, supporter, context.Prefix,
            context.Localizer);

        var nsfwAllowed = context.DiscordChannel.NsfwAllowed(context.DiscordGuild);
        using var chart = await this._chartService.GenerateChartAsync(chartSettings);

        if (chartSettings.CensoredItems is > 0)
        {
            embedDescription.AppendLine(
                context.LocalizeCount("chart.artistsFiltered", chartSettings.CensoredItems.Value));
        }

        if (chartSettings.ContainsNsfw && !nsfwAllowed)
        {
            response.ComponentsContainer.AddComponent(
                new TextDisplayProperties(context.Localize("chart.containsNsfwImages")));
        }

        response.FileDescription = StringExtensions.TruncateLongString(chartSettings.FileDescription.ToString(), 1024);
        response.FileName =
            $"artist-chart-{chartSettings.Width}w-{chartSettings.Height}h-{chartSettings.TimeSettings.TimePeriod}-{userSettings.UserNameLastFm}{ChartRenderer.ChartFileExtension}";

        var mediaGallery =
            new MediaGalleryItemProperties(new ComponentMediaProperties($"attachment://{response.FileName}"))
            {
                Description = StringExtensions.TruncateLongString(response.FileDescription, 256),
                Spoiler = chartSettings.ContainsNsfw
            };

        response.ComponentsContainer.AddComponent(new MediaGalleryProperties
        {
            mediaGallery
        });

        response.ComponentsContainer.AddComponent(new TextDisplayProperties($"**{embedTitle}**"));

        if (embedDescription.Length > 0)
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(embedDescription.ToString()));
        }

        if (context.SelectMenu == null)
        {
            var editCustomId = InteractionConstants.Chart.BuildEditCustomId(
                               context.DiscordUser.Id, InteractionConstants.Chart.ArtistType,
                               chartSettings, userSettings.UserNameLastFm);

            response.ComponentsContainer.AddComponent(
                new ComponentSectionProperties(
                    new ButtonProperties(editCustomId, context.Localize("buttons.edit"), ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(footer.ToString())]
                });
        }
        else
        {
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(footer.ToString()));
        }

        var encoded = ChartRenderer.EncodeChart(chart);
        response.Stream = encoded.AsStream(true);
        response.ResponseType = ResponseType.ComponentsV2;
        response.ComponentsContainer.WithAccentColor(DiscordConstants.LastFmColorRed);

        if (context.SelectMenu != null)
        {
            response.Embed.WithDescription($"**{embedTitle}**");
            response.Spoiler = chartSettings.ContainsNsfw;
            response.ResponseType = ResponseType.Embed;
            response.StringMenus.Add(context.SelectMenu);
        }

        if (supporter != null)
        {
            var actionRow = new ActionRowProperties();
            actionRow.WithButton(context.Localize("buttons.getFmbotSupporter"),
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "chart-broughtby"),
                style: ButtonStyle.Secondary);
            response.ComponentsV2.AddComponent(actionRow);
        }

        return response;
    }
}
