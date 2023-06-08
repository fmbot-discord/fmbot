using System.Text.Encodings.Web;

namespace FMBot.LastFM.Extensions;

public static class LastfmUrlExtensions
{
    public static string GetArtistUrl(string artistName)
    {
        return $"https://www.last.fm/music/{UrlEncoder.Default.Encode(artistName)}";
    }

    public static string GetAlbumUrl(string artistName, string albumName)
    {
        return $"https://www.last.fm/music/{UrlEncoder.Default.Encode(artistName)}/{UrlEncoder.Default.Encode(albumName)}";
    }
}
