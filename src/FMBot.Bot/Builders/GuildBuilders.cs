using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Services.Guild;

namespace FMBot.Bot.Builders;

public class GuildBuilders
{
    private readonly GuildService guildService;

    public GuildBuilders(GuildService guildService)
    {
        this.guildService = guildService;
    }

    public async Task<ResponseModel> MemberOverviewAsync(
        ContextModel context,
        Guild guild,
        UserSettingsModel userSettings,
        GuildViewType guildViewType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        if (guild.CrownsDisabled == true)
        {
            response.Text = "Crown functionality has been disabled in this server.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        return response;

    }
}
