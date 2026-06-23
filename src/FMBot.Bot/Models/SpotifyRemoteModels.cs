using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

    public static RemoteAlbum From(FullAlbum album)
    {
        if (album == null)
        {
            return null;
        }

        var imageUrl = album.Images?.FirstOrDefault()?.Url;
        var artistName = album.Artists?.FirstOrDefault()?.Name;

        return new RemoteAlbum
        {
            Id = album.Id,
            Uri = album.Uri,
            Name = album.Name,
            ArtistName = artistName,
            AlbumImageUrl = imageUrl,
            Tracks = album.Tracks?.Items?
                .Select(t => new RemoteTrack
                {
                    Id = t.Id,
                    Uri = t.Uri,
                    Name = t.Name,
                    ArtistName = t.Artists?.FirstOrDefault()?.Name ?? artistName,
                    AlbumName = album.Name,
                    AlbumUri = album.Uri,
                    AlbumImageUrl = imageUrl
                }).ToList() ?? []
        };
    }
}

public class RemoteArtist
{
    public string Id { get; set; }
    public string Uri { get; set; }
    public string Name { get; set; }
    public string ImageUrl { get; set; }

    public static RemoteArtist From(FullArtist artist)
    {
        if (artist == null)
        {
            return null;
        }

        return new RemoteArtist
        {
            Id = artist.Id,
            Uri = artist.Uri,
            Name = artist.Name,
            ImageUrl = artist.Images?.FirstOrDefault()?.Url
        };
    }
}

public class SpotifyTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; }
}
