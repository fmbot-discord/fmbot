using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class DiscogsBuilder
{
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;

    public DiscogsBuilder(UserService userService, DiscogsService discogsService)
    {
        this._userService = userService;
        this._discogsService = discogsService;
    }

    public async Task<ResponseModel> DiscogsCollectionAsync(ContextModel context,
        UserSettingsModel userSettings,
        string searchValues)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var user = await this._userService.GetUserWithDiscogs(userSettings.DiscordUserId);

        if (user.UserDiscogs == null)
        {
            response.Embed.WithDescription("To use the Discogs commands you have to connect a Discogs account.\n\n" +
                                           "Use the `.discogs` command to get started.");
            response.CommandResponse = CommandResponse.UsernameNotSet;
            return response;
        }

        var justUpdated = false;
        if (user.UserDiscogs.ReleasesLastUpdated == null ||
            user.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddHours(-1))
        {
            user.UserDiscogs = await this._discogsService.StoreUserReleases(user);
            justUpdated = true;
        }

        var releases = await this._discogsService.GetUserCollection(user.UserId);
        if (searchValues != null)
        {
            searchValues = searchValues.ToLower();

            releases = releases.Where(w => w.Release.Title.ToLower().Contains(searchValues) ||
                                           w.Release.Artist.ToLower().Contains(searchValues)).ToList();
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName(!userSettings.DifferentUser
            ? $"Discogs collection for {userTitle}"
            : $"Discogs collection for {userSettings.DiscordUserName}, requested by {userTitle}");

        response.EmbedAuthor.WithUrl($"https://www.discogs.com/user/{user.UserDiscogs.Username}/collection");
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

            if (searchValues != null)
            {
                footer.AppendLine($"Searching for '{Format.Sanitize(searchValues)}'");
            }

            if (searchValues == null)
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

            pages.Add(new PageBuilder()
                .WithDescription(description.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;
        return response;
    }
}
