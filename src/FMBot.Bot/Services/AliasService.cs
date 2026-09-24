using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Bot.Services;

public class AliasService : Core.AliasService
{
    public AliasService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
        : base(cache, contextFactory)
    {
    }
}
