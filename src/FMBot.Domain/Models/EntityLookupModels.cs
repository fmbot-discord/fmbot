namespace FMBot.Domain.Models;

public class EntityImageUrl
{
    public int? Id { get; set; }
    public string ArtistName { get; set; }
    public string Name { get; set; }
    public string AlbumName { get; set; }
    public string ImageUrl { get; set; }
}

public class EntityPlaycount
{
    public string ArtistName { get; set; }
    public string Name { get; set; }
    public long Playcount { get; set; }
}

public class EntitySearchDetails
{
    public string ArtistName { get; set; }
    public string Name { get; set; }
    public string ImageUrl { get; set; }
    public string CountryCode { get; set; }
    public string[] Genres { get; set; }
    public string AlbumName { get; set; }
    public string AlbumType { get; set; }
    public string ReleaseDate { get; set; }
    public int? DurationMs { get; set; }
    public float? Danceability { get; set; }
    public float? Energy { get; set; }
    public float? Acousticness { get; set; }
    public float? Instrumentalness { get; set; }
    public float? Valence { get; set; }
}

public class FriendEntitySearchResult
{
    public string Name { get; set; }
    public string ArtistName { get; set; }
    public int Listeners { get; set; }
    public long Playcount { get; set; }
    public int[] UserIds { get; set; }
    public int[] UserPlaycounts { get; set; }
}

public class FriendLookup
{
    public int? FriendUserId { get; set; }
    public string UserNameLastFM { get; set; }
}
