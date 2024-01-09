using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Persistence.Domain.Models;
using MetaBrainz.MusicBrainz;
using MetaBrainz.MusicBrainz.Interfaces.Entities;
using Serilog;

namespace FMBot.Bot.Services;

public class MusicBrainzService
{
    private readonly HttpClient _httpClient;

    public MusicBrainzService(HttpClient httpClient)
    {
        this._httpClient = httpClient;
    }

    public async Task<ArtistUpdated> AddMusicBrainzDataToArtistAsync(Artist artist, bool bypassUpdatedFilter = false)
    {
        try
        {
            var updated = false;

            if (artist.MusicBrainzDate.HasValue && artist.MusicBrainzDate > DateTime.UtcNow.AddDays(-120) && !bypassUpdatedFilter)
            {
                return new ArtistUpdated(artist);
            }

            var api = new Query(this._httpClient);


            var musicBrainzResults = await api.FindArtistsAsync(artist.Name, simple: true);
            Statistics.MusicBrainzApiCalls.Inc();

            var musicBrainzArtist =
                musicBrainzResults.Results
                    .OrderByDescending(o => o.Score)
                    .Select(s => s.Item).FirstOrDefault(f => f.Name?.ToLower() == artist.Name.ToLower());

            if (musicBrainzArtist != null)
            {
                musicBrainzArtist = await api.LookupArtistAsync(musicBrainzArtist.Id, Include.UrlRelationships);
                Statistics.MusicBrainzApiCalls.Inc();

                var startDate = musicBrainzArtist.LifeSpan?.Begin?.NearestDate;
                var endDate = musicBrainzArtist.LifeSpan?.End?.NearestDate;

                artist.MusicBrainzDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
                artist.Location = musicBrainzArtist.Area?.Name;
                artist.CountryCode = GetArtistCountryCode(musicBrainzArtist);
                artist.Type = musicBrainzArtist.Type;
                artist.Disambiguation = musicBrainzArtist.Disambiguation;
                artist.Gender = musicBrainzArtist.Gender;
                artist.StartDate = startDate.HasValue ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc) : null;
                artist.EndDate = endDate.HasValue ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc) : null;
                artist.Mbid = musicBrainzArtist.Id;

                if (musicBrainzArtist.Relationships != null && musicBrainzArtist.Relationships.Any())
                {
                    var links = new List<ArtistLink>();
                    foreach (var relationship in musicBrainzArtist.Relationships.Where(w => w.Url?.Resource != null))
                    {
                        var typeAndUsername = RelationshipToLinkTypeAndUsername(relationship);

                        if (typeAndUsername != null)
                        {
                            links.Add(new ArtistLink
                            {
                                ManuallyAdded = false,
                                ArtistId = artist.Id,
                                Type = typeAndUsername.LinkType,
                                Url = relationship.Url.Resource.ToString(),
                                Username = typeAndUsername.UserName
                            });
                        }
                    }

                    if (links.Count != 0)
                    {
                        artist.ArtistLinks = links;
                    }
                }

                updated = true;
            }

            return new ArtistUpdated(artist, updated);
        }
        catch (Exception e)
        {
            Log.Error(e, "error in musicbrainzservice");
            return new ArtistUpdated(artist);
        }
    }

    private static LinkTypeAndUsername RelationshipToLinkTypeAndUsername(IRelationship relationship)
    {
        var url = relationship.Url.Resource.ToString();

        if (url.EndsWith("/"))
        {
            url = url.TrimEnd('/');
        }

        var lastSlashIndex = url.LastIndexOf('/');
        string username = null;

        if (lastSlashIndex != -1)
        {
            var result = url[(lastSlashIndex + 1)..];

            username = result;
        }

        switch (relationship.Type)
        {
            case "free streaming":
                {
                    if (relationship.Url.Resource.ToString().Contains("spotify", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Spotify, username);
                    }
                    if (relationship.Url.Resource.ToString().Contains("deezer", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Deezer, username);
                    }
                }
                break;
            case "streaming":
                {
                    if (relationship.Url.Resource.ToString().Contains("tidal", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Tidal, username);
                    }
                    if (relationship.Url.Resource.ToString().Contains("music.apple.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.AppleMusic, username);
                    }
                }
                break;
            case "social network":
                {
                    if (relationship.Url.Resource.ToString().Contains("facebook.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Facebook, username);
                    }
                    if (relationship.Url.Resource.ToString().Contains("twitter.com", StringComparison.OrdinalIgnoreCase) ||
                        relationship.Url.Resource.ToString().Contains("x.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Twitter, username);
                    }
                    if (relationship.Url.Resource.ToString().Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.TikTok, username);
                    }
                    if (relationship.Url.Resource.ToString().Contains("instagram.com", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.Instagram, username);
                    }
                }
                break;
            case "bandcamp":
                {
                    return new LinkTypeAndUsername(LinkType.Bandcamp);
                }
            case "soundcloud":
                {
                    return new LinkTypeAndUsername(LinkType.Soundcloud, username);
                }
            case "youtube":
                {
                    return new LinkTypeAndUsername(LinkType.YouTube, username);
                }
            case "official homepage":
                {
                    return new LinkTypeAndUsername(LinkType.OwnWebsite);
                }
            case "last.fm":
                {
                    return new LinkTypeAndUsername(LinkType.LastFm, username);
                }
            case "other databases":
                {
                    if (relationship.Url.Resource.ToString().Contains("rateyourmusic", StringComparison.OrdinalIgnoreCase))
                    {
                        return new LinkTypeAndUsername(LinkType.RateYourMusic, username);
                    }
                }
                break;
            case "discogs":
                {
                    return new LinkTypeAndUsername(LinkType.Discogs, username);
                }
            case "wikidata":
                {
                    return new LinkTypeAndUsername(LinkType.WikiData, username);
                }
            default:
                return null;
        }

        return null;
    }

    private static string GetArtistCountryCode(IArtist musicBrainzArtist)
    {
        var country = musicBrainzArtist.Country
                      ?? musicBrainzArtist.Area?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.Area?.Iso31661Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31662Codes?.FirstOrDefault()
                      ?? musicBrainzArtist.BeginArea?.Iso31661Codes?.FirstOrDefault();

        if (country == null)
        {
            return null;
        }

        return country.Contains('-') ? country.Split("-").First() : country;
    }

    public record ArtistUpdated(Artist Artist, bool Updated = false);
    private record LinkTypeAndUsername(LinkType LinkType, string UserName = null);
}
