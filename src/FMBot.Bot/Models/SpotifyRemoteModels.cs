using System.Collections.Generic;
using System.Linq;
using SpotifyAPI.Web;

namespace FMBot.Bot.Models;

public enum RemoteActionResult
{
    Ok,
    NotConnected,
    PremiumRequired,
    NoActiveDevice,
    NotFound,
    Restriction,
    Error
}

public class RemoteTrack
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public string AlbumName { get; set; }
    public string AlbumUri { get; set; }
    public string AlbumImageUrl { get; set; }

    public static RemoteTrack From(FullTrack track)
    {
        if (track == null)
        {
            return null;
        }

        return new RemoteTrack
        {
            Id = track.Id,
            Uri = track.Uri,
            Name = track.Name,
            ArtistName = track.Artists?.FirstOrDefault()?.Name,
            AlbumName = track.Album?.Name,
            AlbumUri = track.Album?.Uri,
            AlbumImageUrl = track.Album?.Images?.FirstOrDefault()?.Url
        };
    }
}

public class RemoteAlbum
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public string AlbumImageUrl { get; set; }
    public List<RemoteTrack> Tracks { get; set; } = [];
}

public class RemoteArtist
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ImageUrl { get; set; }
}
