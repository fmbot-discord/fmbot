using System.Threading.Tasks;
using FMBot.Bot.Factories;
using FMBot.Core.Charts;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services;

public class ChartImageFallback(IDataSourceFactory dataSourceFactory, MusicDataFactory musicDataFactory)
    : IChartImageFallback
{
    public async Task<string> GetAlbumCoverAsync(TopAlbum album, string lastFmUserName)
    {
        var albumCall = await dataSourceFactory.GetAlbumInfoAsync(album.ArtistName, album.AlbumName, lastFmUserName);
        if (albumCall.Success && albumCall.Content?.AlbumUrl != null)
        {
            var storedAlbum = await musicDataFactory.GetOrStoreAlbumAsync(albumCall.Content);
            return storedAlbum?.SpotifyImageUrl;
        }

        return null;
    }

    public async Task<string> GetArtistImageAsync(TopArtist artist, string lastFmUserName)
    {
        var artistCall = await dataSourceFactory.GetArtistInfoAsync(artist.ArtistName, lastFmUserName);
        if (artistCall.Success && artistCall.Content?.ArtistUrl != null)
        {
            var storedArtist = await musicDataFactory.GetOrStoreArtistAsync(artistCall.Content);
            return storedArtist?.SpotifyImageUrl;
        }

        return null;
    }
}
