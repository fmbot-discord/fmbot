using System.Text.Encodings.Web;

namespace FMBot.Domain.Extensions;

public static class LastfmUrlExtensions
{
    public static string GetArtistUrl(string artistName)
    {
        return $"https://last.fm/music/{UrlEncoder.Default.Encode(artistName)}";
    }

    public static string GetAlbumUrl(string artistName, string albumName)
    {
        if (albumName == null)
        {
            return null;
        }

        return $"https://last.fm/music/{UrlEncoder.Default.Encode(artistName)}/{UrlEncoder.Default.Encode(albumName)}";
    }

    public static string GetTrackUrl(string artistName, string trackName)
    {
        return $"https://last.fm/music/{UrlEncoder.Default.Encode(artistName)}/_/{UrlEncoder.Default.Encode(trackName)}";
    }

    public static string GetUserUrl(string userName, string addOn = null)
    {
        var url =  $"https://last.fm/user/{UrlEncoder.Default.Encode(userName)}";

        if (addOn != null)
        {
            url += addOn;
        }

        return url;
    }
}
