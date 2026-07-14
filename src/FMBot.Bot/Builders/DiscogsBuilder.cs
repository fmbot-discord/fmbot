using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class DiscogsBuilder
{
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;
    private readonly InteractiveService _interactivity;
    private readonly ArtistsService _artistsService;

    public DiscogsBuilder(UserService userService, DiscogsService discogsService, InteractiveService interactiveService,
        ArtistsService artistsService)
    {
        this._userService = userService;
        this._discogsService = discogsService;
        this._interactivity = interactiveService;
        this._artistsService = artistsService;
    }

    public ResponseModel DiscogsLoginGetLinkAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        response.ComponentsContainer.WithTextDisplay($"Click the button below to get your Discogs login link.");
        response.ComponentsContainer.WithActionRow(new ActionRowProperties().AddComponents(
            new ButtonProperties(InteractionConstants.Discogs.StartAuth, "Get login link", ButtonStyle.Primary)));

        return response;
    }

    public async Task<ResponseModel> DiscogsToggleCollectionValue(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var result = await this._discogsService.ToggleCollectionValueHidden(context.ContextUser.UserId);

        if (result == true)
        {
            response.ComponentsContainer.WithTextDisplay($"Your Discogs collection value is now hidden from all .fmbot commands.");
        }
        else
        {
            response.ComponentsContainer.WithTextDisplay($"Your Discogs collection value is now visible in all .fmbot commands.");
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> DiscogsRemove(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        await this._discogsService.RemoveDiscogs(context.ContextUser.UserId);

        response.ComponentsContainer.WithTextDisplay($"Your Discogs has been unlinked from your .fmbot account.");

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> DiscogsLoginAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2,
        };

        var discogsAuth = await this._discogsService.GetDiscogsAuthLink();

        var loginContainer = new ComponentContainerProperties();
        loginContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        loginContainer.WithTextDisplay($"Login to Discogs with the button below and authorize .fmbot. \n" +
                                       $"After authorizing a code will be shown.\n\n" +
                                       $"**Copy the code and send it in this chat.**\n\n" +
                                       $"-# Do not share the code outside of this DM conversation");
        loginContainer.WithActionRow(new ActionRowProperties().AddComponents(
            new LinkButtonProperties(discogsAuth.LoginUrl, "Login to Discogs")));

        var dmChannel = await context.DiscordUser.GetDMChannelAsync();
        var dm = await dmChannel.SendMessageAsync(new MessageProperties
        {
            Components = [loginContainer],
            Flags = MessageFlags.IsComponentsV2
        });

        var result =
            await this._interactivity.NextMessageAsync(x => x.ChannelId == dmChannel.Id, timeout: TimeSpan.FromMinutes(15));

        if (!result.IsSuccess)
        {
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Content = "Something went wrong while trying to connect your Discogs account."
            });
            response.CommandResponse = CommandResponse.Error;
            return response;
        }

        if (result.IsTimeout)
        {
            var timeoutContainer = new ComponentContainerProperties();
            timeoutContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            timeoutContainer.WithTextDisplay($"❌ Login failed.. link timed out.\n\n" +
                                             $"Re-run the `{context.Prefix}discogs` command to try again.");

            await dm.ModifyAsync(m =>
            {
                m.Components = [timeoutContainer];
            });
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        if (result.Value?.Content == null || !Regex.IsMatch(result.Value.Content, @"^[a-zA-Z]+$") ||
            result.Value.Content.Length != 10)
        {
            var wrongInputContainer = new ComponentContainerProperties();
            wrongInputContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            wrongInputContainer.WithTextDisplay($"Login failed, incorrect input.\n\n" +
                                                $"Re-run the `{context.Prefix}discogs` command to try again.");

            response.CommandResponse = CommandResponse.WrongInput;
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Components = [wrongInputContainer],
                Flags = MessageFlags.IsComponentsV2
            });
            return response;
        }

        var user = await this._discogsService.ConfirmDiscogsAuth(context.ContextUser.UserId, discogsAuth,
            result.Value.Content);

        var resultContainer = new ComponentContainerProperties();
        if (user.Identity != null)
        {
            await this._discogsService.StoreDiscogsAuth(context.ContextUser.UserId, user.Auth, user.Identity);

            resultContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
            resultContainer.WithTextDisplay(
                $"✅ Your Discogs account '[{user.Identity.Username}]({Constants.DiscogsUserUrl}{user.Identity.Username})' has been connected.\n" +
                $"Run the `{context.Prefix}collection` command to view your collection.");
            response.CommandResponse = CommandResponse.Ok;
        }
        else
        {
            resultContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            resultContainer.WithTextDisplay($"Could not connect a Discogs account with provided code.\n\n" +
                                            $"Re-run the `{context.Prefix}discogs` command to try again.");
            response.CommandResponse = CommandResponse.WrongInput;
        }

        await dmChannel.SendMessageAsync(new MessageProperties
        {
            Components = [resultContainer],
            Flags = MessageFlags.IsComponentsV2
        });

        return response;
    }

    public async Task<ResponseModel> DiscogsCollectionAsync(ContextModel context,
        UserSettingsModel userSettings,
        DiscogsCollectionSettings collectionSettings,
        string searchValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var user = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

        if (user.UserDiscogs == null)
        {
            if (!userSettings.DifferentUser)
            {
                response.ComponentsContainer.WithTextDisplay(
                    "To use the Discogs commands you have to connect a Discogs account.\n\n" +
                    $"Use the `{context.Prefix}discogs` command to get started.");
            }
            else
            {
                response.ComponentsContainer.WithTextDisplay(
                    "The user you're trying to look up has not set up their Discogs account yet.");
            }

            response.CommandResponse = CommandResponse.UsernameNotSet;
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            return response;
        }

        var justUpdated = false;
        if (user.UserDiscogs.ReleasesLastUpdated == null ||
            user.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddMinutes(-2))
        {
            user.UserDiscogs = await this._discogsService.StoreUserReleases(user);
            user.UserDiscogs = await this._discogsService.UpdateCollectionValue(user.UserId);
            justUpdated = true;
        }

        var releases = await this._discogsService.GetUserCollection(user.UserId);
        if (!string.IsNullOrWhiteSpace(searchValues))
        {
            searchValues = searchValues.ToLower();

            releases = releases.Where(w => w.Release.Title.ToLower().Contains(searchValues) ||
                                           w.Release.Artist.ToLower().Contains(searchValues)).ToList();
        }

        if (collectionSettings.Formats.Count > 0)
        {
            releases = releases.Where(w =>
                    collectionSettings.Formats.Contains(DiscogsCollectionSettings.ToDiscogsFormat(w.Release.Format)
                        .format))
                .ToList();
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        var title = StringExtensions.MarkdownLink(!userSettings.DifferentUser
                ? $"Discogs collection for {userTitle}"
                : $"Discogs collection for {userSettings.DisplayName}, requested by {userTitle}",
            $"{Constants.DiscogsUserUrl}{user.UserDiscogs.Username}/collection");

        var pageDescriptions = new List<string>();
        var pageFooters = new List<string>();
        var pageCounter = 1;
        var collectionPages = releases.Chunk(6).ToList();

        foreach (var page in collectionPages)
        {
            var description = new StringBuilder();
            foreach (var item in page)
            {
                description.AppendLine(StringService.UserDiscogsReleaseToStringWithTitle(item));
            }

            var footer = new StringBuilder();

            footer.AppendLine($"-# Page {pageCounter}/{collectionPages.Count} - {releases.Count} total");

            if (collectionSettings.Formats.Any())
            {
                footer.Append("-# Format filter: ");
                for (var index = 0; index < collectionSettings.Formats.Count; index++)
                {
                    if (index > 0)
                    {
                        footer.Append(", ");
                    }

                    var format = collectionSettings.Formats[index];
                    footer.Append(format.ToString());
                }

                footer.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(searchValues))
            {
                footer.AppendLine($"-# Searching for '{StringExtensions.Sanitize(searchValues)}'");
            }

            if (string.IsNullOrWhiteSpace(searchValues) && user.UserDiscogs.HideValue != true)
            {
                footer.AppendLine($"-# {user.UserDiscogs.MinimumValue} min " +
                                  $"- {user.UserDiscogs.MedianValue} med" +
                                  $"- {user.UserDiscogs.MaximumValue} max");
            }

            if (justUpdated)
            {
                // footer.AppendLine("Last update just now - Updates max once per minute");
            }
            else
            {
                var diff = DateTime.UtcNow - user.UserDiscogs.ReleasesLastUpdated;

                footer.AppendLine($"-# Last update {(int)diff.Value.TotalSeconds}s ago - " +
                                  $"Updates max once every two minutes");
            }

            if (releases.Count >= 95 &&
                user.UserType == UserType.User &&
                pageCounter == 17)
            {
                description.AppendLine("Only the first 100 items of your collection are fetched and stored.\n" +
                                       $"Want to see your whole collection? [Get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})");
            }

            pageDescriptions.Add(description.ToString());
            pageFooters.Add(footer.ToString());

            pageCounter++;
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;

        response.ResponseType = ResponseType.Paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());

                var pageFooter = pageFooters.ElementAtOrDefault(p.CurrentPageIndex);
                if (pageFooter != null)
                {
                    container.WithSeparator();
                    container.WithTextDisplay(pageFooter.TrimEnd());
                }
            }
            else
            {
                container.WithTextDisplay("No collection could be found or there are no results.");
            }

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    public async Task<ResponseModel> DiscogsTopArtistsAsync(
        ContextModel context,
        TopListSettings topListSettings,
        TimeSettingsModel timeSettings,
        UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var user = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

        if (user.UserDiscogs == null)
        {
            if (!userSettings.DifferentUser)
            {
                response.ComponentsContainer.WithTextDisplay(
                    "To use the top artists commands with Discogs you have to connect a Discogs account.\n\n" +
                    $"Use the `{context.Prefix}discogs` command to get started.");
            }
            else
            {
                response.ComponentsContainer.WithTextDisplay(
                    "The user you're trying to look up has not set up their Discogs account yet.");
            }

            response.CommandResponse = CommandResponse.UsernameNotSet;
            response.ComponentsContainer.WithAccentColor(DiscordConstants.WarningColorOrange);
            response.ResponseType = ResponseType.ComponentsV2;
            return response;
        }

        if (user.UserDiscogs.ReleasesLastUpdated == null ||
            user.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddMinutes(-1))
        {
            user.UserDiscogs = await this._discogsService.StoreUserReleases(user);
            user.UserDiscogs = await this._discogsService.UpdateCollectionValue(user.UserId);
        }

        user.DiscogsReleases = await this._discogsService.GetUserCollection(user.UserId);

        string userTitle;
        if (!userSettings.DifferentUser)
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            userTitle =
                $"{user.UserDiscogs.Username}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }

        var userUrl =
            $"{Constants.DiscogsUserUrl}{user.UserDiscogs.Username}/collection";

        var title = StringExtensions.MarkdownLink(
            $"Top {timeSettings.Description.ToLower()} Discogs artists for {userTitle}", userUrl);
        var topArtists = new List<TopDiscogsArtist>();

        foreach (var item in user.DiscogsReleases
                     .Where(w => timeSettings.StartDateTime == null || timeSettings.StartDateTime <= w.DateAdded &&
                         w.DateAdded <= timeSettings.EndDateTime)
                     .GroupBy(g => g.Release.Artist))
        {
            topArtists.Add(new TopDiscogsArtist
            {
                ArtistName = item.Key,
                ArtistUrl = $"https://www.discogs.com/artist/{item.First().Release.ArtistDiscogsId}",
                UserReleasesInCollection = item.Count(),
                FirstAdded = item.OrderBy(o => o.DateAdded).First().DateAdded
            });
        }

        var artistPages = topArtists.OrderByDescending(s => s.UserReleasesInCollection).ToList()
            .ChunkBy((int)topListSettings.EmbedSize);

        var counter = 1;
        var pageDescriptions = new List<string>();

        foreach (var artistPage in artistPages)
        {
            var artistPageString = new StringBuilder();
            foreach (var artist in artistPage)
            {
                var name =
                    $"**{StringExtensions.MarkdownLink(artist.ArtistName, artist.ArtistUrl)}** ({artist.UserReleasesInCollection} {StringExtensions.GetReleasesString(artist.UserReleasesInCollection)})";

                // TODO for those who know how to deal with this: honor Billboard :)
                artistPageString.Append($"{counter}. ");
                artistPageString.AppendLine(name);

                counter++;
            }

            pageDescriptions.Add(artistPageString.ToString());
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;
        response.ResponseType = ResponseType.Paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
                container.WithSeparator();
                container.WithTextDisplay(
                    $"-# Page {p.CurrentPageIndex + 1}/{pageDescriptions.Count} - {topArtists.Count} different artists added to collection in this time period");
            }
            else
            {
                container.WithTextDisplay("No Discogs artists added in this time period.");
            }

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }

    public static ResponseModel DiscogsManage(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var description = new StringBuilder();

        description.AppendLine("Use the buttons below to manage the Discogs integration for your account.");
        description.AppendLine();

        if (context.ContextUser.UserDiscogs.HideValue == true)
        {
            description.AppendLine("- Show value - Shows the value of your collection in commands");
        }
        else
        {
            description.AppendLine("- Hide value - Hides the value of your collection in commands");
        }

        description.AppendLine("- Re-login - Re-authorize .fmbot");
        description.AppendLine("- Remove connection - Remove Discogs from your .fmbot account");

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        response.ComponentsContainer.WithTextDisplay("### Manage Discogs connection");
        response.ComponentsContainer.WithSeparator();
        response.ComponentsContainer.WithTextDisplay(description.ToString().TrimEnd());
        response.ComponentsContainer.WithActionRow(new ActionRowProperties().AddComponents(
            new ButtonProperties(InteractionConstants.Discogs.ToggleCollectionValue,
                context.ContextUser.UserDiscogs.HideValue == true ? "Show value" : "Hide value",
                ButtonStyle.Secondary),
            new ButtonProperties(InteractionConstants.Discogs.StartAuth, "Re-login", ButtonStyle.Secondary),
            new ButtonProperties(InteractionConstants.Discogs.RemoveAccount, "Remove connection", ButtonStyle.Danger)));

        return response;
    }

    public async Task<ResponseModel> WhoHasDiscogsAsync(
        ContextModel context,
        ResponseMode mode,
        string artistValues,
        bool displayRoleSelector = false,
        List<ulong> roles = null,
        bool redirectsEnabled = true)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, context.Localizer, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedArtists: true,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled,
            interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        return response;
    }
}
