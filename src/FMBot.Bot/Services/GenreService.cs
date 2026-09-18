using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Services;

public class GenreService : Core.GenreService
{
    public GenreService(IMemoryCache cache, IOptions<BotSettings> botSettings) : base(cache, botSettings)
    {
    }

    public static string GenresToString(IEnumerable<ArtistGenre> genres)
    {
        return StringService.StringListToLongString(genres.Select(s => s.Name).ToList());
    }
}
