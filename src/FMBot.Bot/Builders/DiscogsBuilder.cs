using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using static ICSharpCode.SharpZipLib.Zip.ZipEntryFactory;

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

    public async Task<ResponseModel> DiscogsCollectionAsync(
        ContextModel context,
        ICommandContext commandContext)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (context.ContextUser.UserDiscogs == null)
        {
            response.Embed.WithDescription("To use the Discogs commands you have to connect a Discogs account.\n\n" +
                                           "Use the `.discogs` command to get started.");
            response.CommandResponse = CommandResponse.UsernameNotSet;
            return response;
        }

        try
        {
            var justUpdated = false;
            if (context.ContextUser.UserDiscogs.ReleasesLastUpdated == null ||
                context.ContextUser.UserDiscogs.ReleasesLastUpdated <= DateTime.UtcNow.AddHours(-1))
            {
                context.ContextUser.UserDiscogs = await this._discogsService.StoreUserReleases(context.ContextUser);
                justUpdated = true;
            }

            var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

            response.EmbedAuthor.WithName($"Discogs collection for {userTitle}");
            response.EmbedAuthor.WithUrl($"https://www.discogs.com/user/{context.ContextUser.UserDiscogs.Username}/collection");
            response.Embed.WithAuthor(response.EmbedAuthor);

            var pages = new List<PageBuilder>();
            var pageCounter = 1;
            var collectionPages = context.ContextUser.DiscogsReleases.Chunk(6);

            foreach (var page in collectionPages)
            {
                var description = new StringBuilder();
                foreach (var item in page)
                {
                    description.AppendLine(StringService.UserDiscogsReleaseToStringWithTitle(item));
                }

                var footer = new StringBuilder();

                footer.AppendLine($"Page {pageCounter}/{collectionPages.Count()} - {context.ContextUser.DiscogsReleases.Count} total");

                footer.AppendLine($"{context.ContextUser.UserDiscogs.MinimumValue} min " +
                                  $"- {context.ContextUser.UserDiscogs.MedianValue} med" +
                                  $"- {context.ContextUser.UserDiscogs.MaximumValue} max");

                if (justUpdated)
                {
                    footer.AppendLine("Last update just now - Updates max once per hour");
                }
                else
                {
                    var diff = DateTime.UtcNow - context.ContextUser.UserDiscogs.ReleasesLastUpdated;

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
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
