using System;

namespace FMBot.Domain.Models;

[Flags]
public enum CensorType : long
{
    ArtistAlbumsNsfw = 0,
    ArtistAlbumsCensored = 1 << 0,
    AlbumCoverNsfw = 2 << 0,
    AlbumCoverCensored = 3 << 0,
    ArtistImageNsfw = 4 << 0,
    ArtistImageCensored = 5 << 0,
    ArtistFeaturedBan = 6 << 0
}
