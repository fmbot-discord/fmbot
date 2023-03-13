using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

[Flags]
public enum CensorType : long
{
    [Option("Artist Albums NSFW", "Marks all album covers by this artist as NSFW")]
    ArtistAlbumsNsfw = 0,
    [Option("Artist Albums Censored", "Censors all album covers by this artist")]
    ArtistAlbumsCensored = 1 << 0,

    [Option("Album Cover NSFW", "Marks album cover as NSFW")]
    AlbumCoverNsfw = 2 << 0,
    [Option("Album Cover Censored", "Censors album cover")]
    AlbumCoverCensored = 3 << 0,

    [Option("Artist image NSFW", "Marks artist image as NSFW")]
    ArtistImageNsfw = 4 << 0,
    [Option("Artist image Censored", "Censors artist image")]
    ArtistImageCensored = 5 << 0,

    [Option("Artist featured ban", "Bans an artist from featured")]
    ArtistFeaturedBan = 6 << 0
}
