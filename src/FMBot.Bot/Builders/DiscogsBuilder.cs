using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class DiscogsBuilder
{
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;
    private readonly InteractiveService _interactivity;
    private readonly ArtistsService _artistsService;

    public DiscogsBuilder(UserService userService, DiscogsService discogsService, InteractiveService interactiveService, ArtistsService artistsService)
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
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithDescription($"Click the button below to get your Discogs login link.");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder()
            .WithButton("Get login link", style: ButtonStyle.Primary, customId: InteractionConstants.Discogs.StartAuth);

        return response;
    }

    public async Task<ResponseModel> DiscogsToggleCollectionValue(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var result = await this._discogsService.ToggleCollectionValueHidden(context.ContextUser.UserId);

        if (result == true)
        {
            response.Embed.WithDescription($"Your Discogs collection value is now hidden from all .fmbot commands.");
        }
        else
        {
            response.Embed.WithDescription($"Your Discogs collection value is now visible in all .fmbot commands.");
        }

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> DiscogsRemove(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._discogsService.RemoveDiscogs(context.ContextUser.UserId);

        response.Embed.WithDescription($"Your Discogs account has been removed from your Discogs account.");

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<ResponseModel> DiscogsLoginAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var discogsAuth = await this._discogsService.GetDiscogsAuthLink();

        response.Embed.WithDescription($"Login to Discogs with the button below and authorize .fmbot. \n" +
                                       $"After authorizing a code will be shown.\n\n" +
                                       $"**Copy the code and send it in this chat.**");
        response.Embed.WithFooter($"Do not share the code outside of this DM conversation");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder()
            .WithButton("Login to Discogs", style: ButtonStyle.Link, url: discogsAuth.LoginUrl);

        var dm = await context.DiscordUser.SendMessageAsync("", false, response.Embed.Build(), components: response.Components.Build());
        response.Embed.Footer = null;

        var result = await this._interactivity.NextMessageAsync(x => x.Channel.Id == dm.Channel.Id, timeout: TimeSpan.FromMinutes(15));

        if (!result.IsSuccess)
        {
            await context.DiscordUser.SendMessageAsync("Something went wrong while trying to connect your Discogs account.");
            response.CommandResponse = CommandResponse.Error;
            return response;
        }

        if (result.IsTimeout)
        {
            await dm.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription($"❌ Login failed.. link timed out.\n\n" +
                                        $"Re-run the `{context.Prefix}discogs` command to try again.")
                    .WithColor(DiscordConstants.WarningColorOrange)
                    .Build();
            });
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        if (result.Value?.Content == null || !Regex.IsMatch(result.Value.Content, @"^[a-zA-Z]+$") || result.Value.Content.Length != 10)
        {
            response.Embed.WithDescription($"Login failed, incorrect input.\n\n" +
                                        $"Re-run the `{context.Prefix}discogs` command to try again.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            await context.DiscordUser.SendMessageAsync("", false, response.Embed.Build());
            return response;
        }

        var user = await this._discogsService.ConfirmDiscogsAuth(context.ContextUser.UserId, discogsAuth, result.Value.Content);

        if (user.Identity != null)
        {
            await this._discogsService.StoreDiscogsAuth(context.ContextUser.UserId, user.Auth, user.Identity);

            response.Embed.WithDescription($"✅ Your Discogs account '[{user.Identity.Username}]({Constants.DiscogsUserUrl}{user.Identity.Username})' has been connected.\n" +
                                        $"Run the `{context.Prefix}collection` command to view your collection.");
            response.CommandResponse = CommandResponse.Ok;
            await context.DiscordUser.SendMessageAsync("", false, response.Embed.Build());
        }
        else
        {
            response.Embed.WithDescription($"Could not connect a Discogs account with provided code.\n\n" +
                                        $"Re-run the `{context.Prefix}discogs` command to try again.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            await context.DiscordUser.SendMessageAsync("", false, response.Embed.Build());
        }

        return response;
    }

    public async Task<ResponseModel> DiscogsCollectionAsync(ContextModel context,
        UserSettingsModel userSettings,
        DiscogsCollectionSettings collectionSettings,
        string searchValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var user = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

        if (user.UserDiscogs == null)
        {
            if (!userSettings.DifferentUser)
            {
                response.Embed.WithDescription("To use the Discogs commands you have to connect a Discogs account.\n\n" +
                                               $"Use the `{context.Prefix}discogs` command to get started.");
            }
            else
            {
                response.Embed.WithDescription("The user you're trying to look up has not setup their Discogs account yet.");
            }

            response.CommandResponse = CommandResponse.UsernameNotSet;
            response.Embed.Color = DiscordConstants.WarningColorOrange;
            return response;
        }

        var justUpdated = false;
        if (user.UserDiscogs.ReleasesLastUpdated == null ||
            user.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddHours(-1))
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
            releases = releases.Where(w => collectionSettings.Formats.Contains(DiscogsCollectionSettings.ToDiscogsFormat(w.Release.Format).format)).ToList();
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName(!userSettings.DifferentUser
            ? $"Discogs collection for {userTitle}"
            : $"Discogs collection for {userSettings.DisplayName}, requested by {userTitle}");

        response.EmbedAuthor.WithUrl($"{Constants.DiscogsUserUrl}{user.UserDiscogs.Username}/collection");
        response.Embed.WithAuthor(response.EmbedAuthor);

        var pages = new List<PageBuilder>();
        var pageCounter = 1;
        var collectionPages = releases.Chunk(6);

        foreach (var page in collectionPages)
        {
            var description = new StringBuilder();
            foreach (var item in page)
            {
                description.AppendLine(StringService.UserDiscogsReleaseToStringWithTitle(item));
            }

            var footer = new StringBuilder();

            footer.AppendLine($"Page {pageCounter}/{collectionPages.Count()} - {releases.Count} total");

            if (collectionSettings.Formats.Any())
            {
                footer.Append("Format filter: ");
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
                footer.AppendLine($"Searching for '{StringExtensions.Sanitize(searchValues)}'");
            }

            if (searchValues == null && user.UserDiscogs.HideValue != true)
            {
                footer.AppendLine($"{user.UserDiscogs.MinimumValue} min " +
                                  $"- {user.UserDiscogs.MedianValue} med" +
                                  $"- {user.UserDiscogs.MaximumValue} max");
            }

            if (justUpdated)
            {
                footer.AppendLine("Last update just now - Updates max once per hour");
            }
            else
            {
                var diff = DateTime.UtcNow - user.UserDiscogs.ReleasesLastUpdated;

                footer.AppendLine($"Last update {(int)diff.Value.TotalMinutes}m ago - " +
                                  $"Updates max once per hour");
            }

            if (releases.Count >= 95 &&
                user.UserType == UserType.User &&
                pageCounter == 17)
            {
                description.AppendLine("Only the first 100 items of your collection are fetched and stored.\n" +
                                       $"Want to see your whole collection? [Get .fmbot supporter here.]({Constants.GetSupporterOverviewLink})");
            }

            pages.Add(new PageBuilder()
                .WithDescription(description.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No collection could be found or there are no results.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
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
                response.Embed.WithDescription("To use the top artists commands with Discogs you have to connect a Discogs account.\n\n" +
                                               $"Use the `{context.Prefix}discogs` command to get started.");
            }
            else
            {
                response.Embed.WithDescription("The user you're trying to look up has not setup their Discogs account yet.");
            }

            response.CommandResponse = CommandResponse.UsernameNotSet;
            return response;
        }

        if (user.UserDiscogs.ReleasesLastUpdated == null ||
            user.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddHours(-1))
        {
            user.UserDiscogs = await this._discogsService.StoreUserReleases(user);
            user.UserDiscogs = await this._discogsService.UpdateCollectionValue(user.UserId);
        }

        var pages = new List<PageBuilder>();

        string userTitle;
        if (!userSettings.DifferentUser)
        {
            if (!context.SlashCommand)
            {
                response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
            }
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }
        else
        {
            userTitle =
                $"{user.UserDiscogs.Username}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
        }
        var userUrl =
            $"{Constants.DiscogsUserUrl}{user.UserDiscogs.Username}/collection";

        response.EmbedAuthor.WithName($"Top {timeSettings.Description.ToLower()} Discogs artists for {userTitle}");
        response.EmbedAuthor.WithUrl(userUrl);
        var topArtists = new List<TopDiscogsArtist>();

        foreach (var item in user.DiscogsReleases
                     .Where(w => timeSettings.StartDateTime == null || timeSettings.StartDateTime <= w.DateAdded && w.DateAdded <= timeSettings.EndDateTime)
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
        var pageCounter = 1;

        foreach (var artistPage in artistPages)
        {
            var artistPageString = new StringBuilder();
            foreach (var artist in artistPage)
            {
                var name =
                    $"**[{artist.ArtistName}]({artist.ArtistUrl})** ({artist.UserReleasesInCollection} {StringExtensions.GetReleasesString(artist.UserReleasesInCollection)})";

                // TODO for those who know how to deal with this: honor Billboard :)
                artistPageString.Append($"{counter}. ");
                artistPageString.AppendLine(name);

                counter++;
            }
            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{artistPages.Count}");
            footer.Append($" - {topArtists.Count} different artists added to collection in this time period");
            pages.Add(new PageBuilder()
                .WithDescription(artistPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }
        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No Discogs artists added in this time period.")
                .WithAuthor(response.EmbedAuthor));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }

    public static ResponseModel DiscogsManage(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Components = new ComponentBuilder()
                .WithButton(context.ContextUser.UserDiscogs.HideValue == true ? "Show value" : "Hide value", InteractionConstants.Discogs.ToggleCollectionValue, ButtonStyle.Secondary)
                .WithButton("Re-login", InteractionConstants.Discogs.StartAuth, ButtonStyle.Secondary)
                .WithButton("Remove connection", InteractionConstants.Discogs.RemoveAccount, ButtonStyle.Danger)
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

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.Embed.WithTitle("Manage Discogs connection");

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

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm, useCachedArtists: true,
            userId: context.ContextUser.UserId, redirectsEnabled: redirectsEnabled, interactionId: context.InteractionId);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        return response;
    }
}
