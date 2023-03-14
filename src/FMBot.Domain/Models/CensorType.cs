using System;
using FMBot.Domain.Attributes;

namespace FMBot.Domain.Models;

[Flags]
public enum CensorType : long
{
    [Option("None", "None")]
    None = 0,

    [Option("Artist Albums NSFW", "Marks all album covers by this artist as NSFW")]
    ArtistAlbumsNsfw = 1 << 1,
    [Option("Artist Albums Censored", "Censors all album covers by this artist")]
    ArtistAlbumsCensored = 1 << 2,

    [Option("Album Cover NSFW", "Marks album cover as NSFW")]
    AlbumCoverNsfw = 1 << 3,
    [Option("Album Cover Censored", "Censors album cover")]
    AlbumCoverCensored = 1 << 4,

    [Option("Artist image NSFW", "Marks artist image as NSFW")]
    ArtistImageNsfw = 1 << 5,
    [Option("Artist image Censored", "Censors artist image")]
    ArtistImageCensored = 1 << 6,

    [Option("Artist featured ban", "Bans an artist from featured")]
    ArtistFeaturedBan = 1 << 7
}
