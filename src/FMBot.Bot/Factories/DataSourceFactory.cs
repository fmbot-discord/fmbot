using FMBot.Core;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Factories;

public class DataSourceFactory : Core.DataSourceFactory
{
    public DataSourceFactory(ILastfmRepository lastfmRepository,
        IPlayDataSourceRepository playDataSourceRepository,
        ListeningTimeService timeService,
        IMemoryCache cache,
        IDbContextFactory<FMBotDbContext> contextFactory,
        AliasService aliasService,
        IOptions<BotSettings> botSettings,
        IIdResolver idResolutionService)
        : base(lastfmRepository, playDataSourceRepository, timeService, cache, contextFactory, aliasService,
            botSettings, idResolutionService)
    {
    }
}
