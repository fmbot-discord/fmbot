using System.Text.Json;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Api;
using FMBot.LastFM.Converters;
using FMBot.LastFM.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using LfmArtist = FMBot.LastFM.Models.Artist;

namespace FMBot.Tests;

public class LastFmRepositoryTopListTests
{
    private sealed class FakeLastfmApi : ILastfmApi
    {
        private readonly Func<string, int, int, object> _responder;
        public readonly Dictionary<int, int> AttemptsPerPage = new();

        public FakeLastfmApi(Func<string, int, int, object> responder)
        {
            this._responder = responder;
        }

        public Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call,
            bool generateSignature = false, bool usePrivateKey = false)
        {
            var page = parameters.TryGetValue("page", out var p) ? int.Parse(p) : 1;
            this.AttemptsPerPage[page] = this.AttemptsPerPage.GetValueOrDefault(page) + 1;
            return Task.FromResult((Response<T>)this._responder(call, page, this.AttemptsPerPage[page]));
        }
    }

    private static LastFmRepository CreateRepository(ILastfmApi api)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LastFm:PrivateKey"] = "key",
                ["LastFm:PrivateKeySecret"] = "secret"
            })
            .Build();

        return new LastFmRepository(configuration, api, new MemoryCache(new MemoryCacheOptions()), new HttpClient());
    }

    private static Response<TopAlbumsLfmResponse> AlbumPage(int page, int amount, long total = 0, long totalPages = 0)
    {
        return new Response<TopAlbumsLfmResponse>
        {
            Success = true,
            Content = new TopAlbumsLfmResponse
            {
                TopAlbums = new TopAlbumsLfm
                {
                    Attr = new TopListAttrLfm { Total = total, Page = page, TotalPages = totalPages },
                    Album = Enumerable.Range(0, amount).Select(i => new TopAlbumLfm
                    {
                        Name = $"Album {page}-{i}",
                        Artist = new LfmArtist { Name = "Artist" },
                        Playcount = 10,
                        Url = "https://www.last.fm/music/Artist/Album"
                    }).ToList()
                }
            }
        };
    }

    private static Response<TopArtistsLfmResponse> ArtistPage(int page, int amount)
    {
        return new Response<TopArtistsLfmResponse>
        {
            Success = true,
            Content = new TopArtistsLfmResponse
            {
                TopArtists = new TopArtistsLfm
                {
                    Attr = new TopListAttrLfm { Page = page },
                    Artist = Enumerable.Range(0, amount).Select(i => new TopArtistLfm
                    {
                        Name = $"Artist {page}-{i}",
                        Playcount = 10,
                        Url = "https://www.last.fm/music/Artist"
                    }).ToList()
                }
            }
        };
    }

    private static Response<T> Failure<T>()
    {
        return new Response<T> { Success = false, Error = ResponseStatus.Failure, Message = "Last.fm returned a server error (500)." };
    }

    [Test]
    public async Task TopAlbums_RetriesFailedPageAndKeepsAllPages()
    {
        var api = new FakeLastfmApi((_, page, attempt) => page switch
        {
            1 => AlbumPage(1, 1000, 2500),
            2 => attempt == 1 ? Failure<TopAlbumsLfmResponse>() : AlbumPage(2, 1000),
            _ => AlbumPage(page, 500)
        });

        var result = await CreateRepository(api).GetTopAlbumsAsync("user", TimePeriod.AllTime, 1000, 10, errorRetries: 2);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content.TopAlbums, Has.Count.EqualTo(2500));
        Assert.That(result.Content.TotalAmount, Is.EqualTo(2500));
        Assert.That(api.AttemptsPerPage[2], Is.EqualTo(2));
        Assert.That(api.AttemptsPerPage.ContainsKey(4), Is.False);
    }

    [Test]
    public async Task TopAlbums_SkipsPageThatKeepsFailingAndContinues()
    {
        var api = new FakeLastfmApi((_, page, _) => page switch
        {
            1 => AlbumPage(1, 1000, 2500, totalPages: 3),
            2 => Failure<TopAlbumsLfmResponse>(),
            _ => AlbumPage(page, 500)
        });

        var result = await CreateRepository(api).GetTopAlbumsAsync("user", TimePeriod.AllTime, 1000, 10, errorRetries: 1);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content.TopAlbums, Has.Count.EqualTo(1500));
        Assert.That(result.Content.TopAlbums.Any(a => a.AlbumName.StartsWith("Album 3-")), Is.True);
        Assert.That(api.AttemptsPerPage[2], Is.EqualTo(2));
        Assert.That(api.AttemptsPerPage[3], Is.EqualTo(1));
        Assert.That(api.AttemptsPerPage.ContainsKey(4), Is.False);
    }

    [Test]
    public async Task TopAlbums_StopsAtTotalPagesFromFirstPage()
    {
        var api = new FakeLastfmApi((_, page, _) => AlbumPage(page, 1000, 2000, totalPages: 2));

        var result = await CreateRepository(api).GetTopAlbumsAsync("user", TimePeriod.AllTime, 1000, 200);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content.TopAlbums, Has.Count.EqualTo(2000));
        Assert.That(api.AttemptsPerPage.ContainsKey(3), Is.False);
    }

    [Test]
    public async Task TopAlbums_FirstPageFailureReturnsError()
    {
        var api = new FakeLastfmApi((_, _, _) => Failure<TopAlbumsLfmResponse>());

        var result = await CreateRepository(api).GetTopAlbumsAsync("user", TimePeriod.AllTime, 1000, 10, errorRetries: 0);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.EqualTo(ResponseStatus.Failure));
        Assert.That(api.AttemptsPerPage[1], Is.EqualTo(1));
    }

    [Test]
    public async Task TopAlbums_MapsAlbumFields()
    {
        var api = new FakeLastfmApi((call, _, _) =>
        {
            Assert.That(call, Is.EqualTo("user.getTopAlbums"));
            return new Response<TopAlbumsLfmResponse>
            {
                Success = true,
                Content = new TopAlbumsLfmResponse
                {
                    TopAlbums = new TopAlbumsLfm
                    {
                        Attr = new TopListAttrLfm { Total = 2 },
                        Album =
                        [
                            new TopAlbumLfm
                            {
                                Name = "Blonde",
                                Artist = new LfmArtist { Name = "Frank Ocean" },
                                Playcount = 321,
                                Mbid = "5b4a0fa5-1a1b-4c62-9f1e-3f1d8c6c0a11",
                                Url = "https://www.last.fm/music/Frank+Ocean/Blonde",
                                Image =
                                [
                                    new ImageLfm { Size = "small", Text = "https://lastfm.freetls.fastly.net/i/u/34s/abc.jpg" },
                                    new ImageLfm { Size = "extralarge", Text = "https://lastfm.freetls.fastly.net/i/u/300x300/abc.jpg" }
                                ]
                            },
                            new TopAlbumLfm
                            {
                                Name = "Unknown",
                                Artist = new LfmArtist { Name = "Someone" },
                                Playcount = 1,
                                Mbid = "",
                                Image =
                                [
                                    new ImageLfm { Size = "extralarge", Text = "https://lastfm.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png" }
                                ]
                            }
                        ]
                    }
                }
            };
        });

        var result = await CreateRepository(api).GetTopAlbumsAsync("user", TimePeriod.Weekly, 2);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content.TotalAmount, Is.EqualTo(2));
        var first = result.Content.TopAlbums[0];
        Assert.That(first.AlbumName, Is.EqualTo("Blonde"));
        Assert.That(first.ArtistName, Is.EqualTo("Frank Ocean"));
        Assert.That(first.UserPlaycount, Is.EqualTo(321));
        Assert.That(first.AlbumUrl, Is.EqualTo("https://www.last.fm/music/Frank+Ocean/Blonde"));
        Assert.That(first.AlbumCoverUrl, Is.EqualTo("https://lastfm.freetls.fastly.net/i/u/abc.jpg"));
        Assert.That(first.Mbid, Is.EqualTo(Guid.Parse("5b4a0fa5-1a1b-4c62-9f1e-3f1d8c6c0a11")));
        var second = result.Content.TopAlbums[1];
        Assert.That(second.AlbumCoverUrl, Is.Null);
        Assert.That(second.Mbid, Is.Null);
    }

    [Test]
    public async Task TopArtists_RetriesFailedPageAndKeepsAllPages()
    {
        var api = new FakeLastfmApi((call, page, attempt) =>
        {
            Assert.That(call, Is.EqualTo("user.getTopArtists"));
            return page switch
            {
                1 => ArtistPage(1, 1000),
                2 => attempt == 1 ? Failure<TopArtistsLfmResponse>() : ArtistPage(2, 1000),
                _ => ArtistPage(page, 10)
            };
        });

        var result = await CreateRepository(api).GetTopArtistsAsync("user", TimePeriod.AllTime, 1000, 10, errorRetries: 2);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Content.TopArtists, Has.Count.EqualTo(2010));
        Assert.That(result.Content.TopArtists[0].ArtistName, Is.EqualTo("Artist 1-0"));
        Assert.That(api.AttemptsPerPage[2], Is.EqualTo(2));
    }

    private static readonly JsonSerializerOptions LastfmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LongToStringConverter() }
    };

    [Test]
    public void TopAlbumsResponse_DeserializesRealLastfmPayload()
    {
        const string json = """
            {"topalbums":{"album":[{"artist":{"url":"https://www.last.fm/music/Nine+Inch+Nails","name":"Nine Inch Nails","mbid":"b7ffd2af-418f-4be2-bdd1-22f8b48613da"},"image":[{"size":"small","#text":"https://lastfm-img.freetls.fastly.net/i/u/34s/ca5fe8e286527ad0ecdbfd2464730420.jpg"},{"size":"extralarge","#text":"https://lastfm-img.freetls.fastly.net/i/u/300x300/ca5fe8e286527ad0ecdbfd2464730420.jpg"}],"mbid":"00411973-24c9-4e3e-8f08-63323c642687","url":"https://www.last.fm/music/Nine+Inch+Nails/The+Fragile","playcount":"3156","@attr":{"rank":"1"},"name":"The Fragile"},{"artist":{"url":"https://www.last.fm/music/Lana+Del+Rey","name":"Lana Del Rey","mbid":"b7539c32-53e7-4908-bda3-81449c367da6"},"image":[],"mbid":"","url":"https://www.last.fm/music/Lana+Del+Rey/Born+to+Die","playcount":"2747","@attr":{"rank":"2"},"name":"Born to Die"}],"@attr":{"user":"Cerryn","totalPages":"18861","page":"1","perPage":"2","total":"37721"}}}
            """;

        var response = JsonSerializer.Deserialize<TopAlbumsLfmResponse>(json, LastfmJsonOptions);

        Assert.That(response!.TopAlbums.Album, Has.Count.EqualTo(2));
        Assert.That(response.TopAlbums.Attr.Total, Is.EqualTo(37721));
        Assert.That(response.TopAlbums.Attr.TotalPages, Is.EqualTo(18861));
        var first = response.TopAlbums.Album[0];
        Assert.That(first.Name, Is.EqualTo("The Fragile"));
        Assert.That(first.Artist.Name, Is.EqualTo("Nine Inch Nails"));
        Assert.That(first.Playcount, Is.EqualTo(3156));
        Assert.That(first.Mbid, Is.EqualTo("00411973-24c9-4e3e-8f08-63323c642687"));
        Assert.That(first.Image.Single(i => i.Size == "extralarge").Text, Does.Contain("/u/300x300/"));
    }

    [Test]
    public void TopArtistsResponse_DeserializesRealLastfmPayload()
    {
        const string json = """
            {"topartists":{"artist":[{"streamable":"0","image":[{"size":"extralarge","#text":"https://lastfm-img.freetls.fastly.net/i/u/300x300/2a96cbd8b46e442fc41c2b86b821562f.png"}],"mbid":"b7ffd2af-418f-4be2-bdd1-22f8b48613da","url":"https://www.last.fm/music/Nine+Inch+Nails","playcount":"14407","@attr":{"rank":"1"},"name":"Nine Inch Nails"}],"@attr":{"user":"Cerryn","totalPages":"20122","page":"1","perPage":"1","total":"20122"}}}
            """;

        var response = JsonSerializer.Deserialize<TopArtistsLfmResponse>(json, LastfmJsonOptions);

        Assert.That(response!.TopArtists.Artist, Has.Count.EqualTo(1));
        Assert.That(response.TopArtists.Attr.Total, Is.EqualTo(20122));
        Assert.That(response.TopArtists.Artist[0].Name, Is.EqualTo("Nine Inch Nails"));
        Assert.That(response.TopArtists.Artist[0].Playcount, Is.EqualTo(14407));
        Assert.That(response.TopArtists.Artist[0].Url, Is.EqualTo("https://www.last.fm/music/Nine+Inch+Nails"));
    }
}
