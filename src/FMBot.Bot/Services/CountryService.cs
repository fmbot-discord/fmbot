using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Services;

public class CountryService : Core.CountryService
{
    public CountryService(IOptions<BotSettings> botSettings) : base(botSettings)
    {
    }
}
