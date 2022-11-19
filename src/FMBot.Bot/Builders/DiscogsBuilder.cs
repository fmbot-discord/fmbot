using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
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

    public async Task<ResponseModel> DiscogsCollectionAsync(
        ContextModel context,
        ICommandContext commandContext)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {

            var user = await this._userService.GetUserWithDiscogs(context.DiscordUser);

            var collection = await this._discogsService.StoreUserReleases(user);

            var description = new StringBuilder();
            foreach (var item in user.DiscogsReleases.Take(10))
            {
                description.AppendLine($"{item.Release.DiscogsMaster.Title} - {item.Release.DiscogsMaster.Artist}");
            }

            response.Embed.WithDescription(description.ToString());

            return response;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
