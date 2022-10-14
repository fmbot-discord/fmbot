using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using Genius;
using Genius.Models;
using Genius.Models.Song;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Services.ThirdParty;

public class GeniusService
{
    private readonly BotSettings _botSettings;

    public GeniusService(IOptions<BotSettings> botSettings)
    {
        this._botSettings = botSettings.Value;
    }

    public async Task<List<SearchHit>> SearchGeniusAsync(string searchValue, string currentTrackName, string currentTrackArtist)
    {
        var client = new GeniusClient(this._botSettings.Genius.AccessToken);

        var result = await client.SearchClient.Search(searchValue);

        if (result.Response?.Hits == null || !result.Response.Hits.Any())
        {
            return null;
        }

        var results = result.Response.Hits
            .Where(w => !w.Result.PrimaryArtist.Name.Contains("Spotify", StringComparison.CurrentCultureIgnoreCase) &&
                        !w.Result.PrimaryArtist.Name.Contains("Genius", StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(o => o.Result.PyongsCount).ToList();

        if (currentTrackName != null && currentTrackArtist != null)
        {
            results = results.Where(w => w.Result.FullTitle.Contains(currentTrackName, StringComparison.CurrentCultureIgnoreCase) ||
                                         w.Result.FullTitle.Contains(currentTrackArtist, StringComparison.CurrentCultureIgnoreCase) ||
                                         w.Result.TitleWithFeatured.Contains(currentTrackName, StringComparison.CurrentCultureIgnoreCase) ||
                                         w.Result.TitleWithFeatured.Contains(currentTrackArtist, StringComparison.CurrentCultureIgnoreCase) ||
                                         currentTrackName.Contains(w.Result.FullTitle, StringComparison.CurrentCultureIgnoreCase) ||
                                         currentTrackArtist.Contains(w.Result.FullTitle, StringComparison.CurrentCultureIgnoreCase) ||
                                         currentTrackName.Contains(w.Result.TitleWithFeatured, StringComparison.CurrentCultureIgnoreCase) ||
                                         currentTrackArtist.Contains(w.Result.TitleWithFeatured, StringComparison.CurrentCultureIgnoreCase)
            ).ToList();
        }

        return results;
    }

    public async Task<Song> GetSong(ulong id)
    {
        var client = new GeniusClient(this._botSettings.Genius.AccessToken);

        var result = await client.SongClient.GetSong(id);

        if (result.Response?.Song == null)
        {
            return null;
        }

        return result.Response.Song;
    }
}
