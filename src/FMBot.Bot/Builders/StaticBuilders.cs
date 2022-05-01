using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly CommandService _service;

    public StaticBuilders(CommandService service)
    {
        this._service = service;
    }
}
