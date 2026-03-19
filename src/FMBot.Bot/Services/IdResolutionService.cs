using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;
using Serilog;
using Web.InternalApi;

namespace FMBot.Bot.Services;

public class IdResolutionService(IdResolution.IdResolutionClient client)
{
    public async Task ResolvePlayIds(IReadOnlyList<UserPlay> plays)
    {
        if (plays == null || plays.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolvePlayIdsRequest();
            for (var i = 0; i < plays.Count; i++)
            {
                request.Plays.Add(new PlayToResolve
                {
                    Index = i,
                    ArtistName = plays[i].ArtistName ?? "",
                    AlbumName = plays[i].AlbumName ?? "",
                    TrackName = plays[i].TrackName ?? ""
                });
            }

            var reply = await client.ResolvePlayIdsAsync(request);

            foreach (var resolved in reply.Plays)
            {
                if (resolved.Index >= 0 && resolved.Index < plays.Count)
                {
                    var play = plays[resolved.Index];
                    if (resolved.ArtistId != 0) play.ArtistId = resolved.ArtistId;
                    if (resolved.AlbumId != 0) play.AlbumId = resolved.AlbumId;
                    if (resolved.TrackId != 0) play.TrackId = resolved.TrackId;
                }
            }

            Log.Debug("IdResolution: Resolved IDs for {playCount} plays", plays.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve play IDs, continuing without IDs");
        }
    }

    public async Task ResolveArtistIds(IReadOnlyList<UserArtist> artists)
    {
        if (artists == null || artists.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveArtistIdsRequest();
            request.ArtistNames.AddRange(artists.Select(a => a.Name ?? ""));

            var reply = await client.ResolveArtistIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in reply.Mappings.Where(m => m.ArtistId != 0))
            {
                mappings.TryAdd(m.ArtistName, m.ArtistId);
            }

            foreach (var artist in artists)
            {
                if (artist.Name != null && mappings.TryGetValue(artist.Name, out var artistId))
                {
                    artist.ArtistId = artistId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} artist IDs",
                mappings.Count, artists.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve artist IDs, continuing without IDs");
        }
    }

    public async Task ResolveAlbumIds(IReadOnlyList<UserAlbum> albums)
    {
        if (albums == null || albums.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveAlbumIdsRequest();
            request.Albums.AddRange(albums.Select(a => new AlbumKey
            {
                ArtistName = a.ArtistName ?? "",
                AlbumName = a.Name ?? ""
            }));

            var reply = await client.ResolveAlbumIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in reply.Mappings.Where(m => m.AlbumId != 0))
            {
                mappings.TryAdd($"{m.ArtistName?.ToLowerInvariant()}\0{m.AlbumName?.ToLowerInvariant()}", m.AlbumId);
            }

            foreach (var album in albums)
            {
                var key = $"{album.ArtistName?.ToLowerInvariant()}\0{album.Name?.ToLowerInvariant()}";
                if (mappings.TryGetValue(key, out var albumId))
                {
                    album.AlbumId = albumId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} album IDs",
                mappings.Count, albums.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve album IDs, continuing without IDs");
        }
    }

    public async Task ResolveTrackIds(IReadOnlyList<UserTrack> tracks)
    {
        if (tracks == null || tracks.Count == 0)
        {
            return;
        }

        try
        {
            var request = new ResolveTrackIdsRequest();
            request.Tracks.AddRange(tracks.Select(t => new TrackKey
            {
                ArtistName = t.ArtistName ?? "",
                TrackName = t.Name ?? ""
            }));

            var reply = await client.ResolveTrackIdsAsync(request);

            var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in reply.Mappings.Where(m => m.TrackId != 0))
            {
                mappings.TryAdd($"{m.ArtistName?.ToLowerInvariant()}\0{m.TrackName?.ToLowerInvariant()}", m.TrackId);
            }

            foreach (var track in tracks)
            {
                var key = $"{track.ArtistName?.ToLowerInvariant()}\0{track.Name?.ToLowerInvariant()}";
                if (mappings.TryGetValue(key, out var trackId))
                {
                    track.TrackId = trackId;
                }
            }

            Log.Debug("IdResolution: Resolved {resolvedCount}/{totalCount} track IDs",
                mappings.Count, tracks.Count);
        }
        catch (Exception e)
        {
            Log.Warning(e, "IdResolution: Failed to resolve track IDs, continuing without IDs");
        }
    }
}
