using System.Threading.Tasks;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services;

public class InteractionLogService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public InteractionLogService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }
}
