using FMBot.Core;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Services;

public class UpdateService : Core.UpdateService
{
    public UpdateService(IDbContextFactory<FMBotDbContext> contextFactory,
        IMemoryCache cache,
        IOptions<BotSettings> botSettings,
        IDataSourceFactory dataSourceFactory,
        SmallIndexRepository smallIndexRepository,
        AliasService aliasService,
        UserLookup userLookup,
        IIdResolver idResolver,
        IdBackfillService idBackfillService)
        : base(contextFactory, cache, botSettings, dataSourceFactory, smallIndexRepository, aliasService, userLookup,
            idResolver, idBackfillService)
    {
    }
}
