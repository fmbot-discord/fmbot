using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Core;

public interface IIdResolver
{
    Task ResolvePlayIds(IReadOnlyList<UserPlay> plays);

    Task ResolveArtistIds(IReadOnlyList<UserArtist> artists);

    Task ResolveAlbumIds(IReadOnlyList<UserAlbum> albums);

    Task ResolveTrackIds(IReadOnlyList<UserTrack> tracks);

    Task<int?> ResolveTrackId(string artistName, string trackName);
}
