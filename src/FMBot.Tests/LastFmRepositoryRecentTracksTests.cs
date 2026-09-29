using FMBot.Domain.Types;
using FMBot.LastFM.Api;
using FMBot.LastFM.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace FMBot.Tests;

public class LastFmRepositoryRecentTracksTests
{
    private sealed class FakeLastfmApi : ILastfmApi
    {
        public int Calls;

        public Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call,
            bool generateSignature = false, bool usePrivateKey = false)
        {
            this.Calls++;
            object response = new Response<RecentTracksListLfmResponseModel>
            {
                Success = true,
                Content = new RecentTracksListLfmResponseModel
                {
                    RecentTracks = new RecentTracksLfmList
                    {
                        AttributesLfm = new AttributesLfm { Total = 3 },
                        Track =
                        [
                            Track("Radiohead", "Reckoner", 3),
                            Track("Portishead", "Roads", 2),
                            Track("Radiohead", "Nude", 1)
                        ]
                    }
                }
            };
            return Task.FromResult((Response<T>)response);
        }

        private static RecentTrackLfm Track(string artist, string name, long uts) => new()
        {
            Name = name,
            Url = new Uri($"https://www.last.fm/music/{artist}/_/{name}"),
            Artist = new SmallArtist { Text = artist },
            Date = new Date { Uts = uts }
        };
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

    [Test]
    public async Task RecentTracks_ReplacingFetchedContent_DoesNotChangeCachedList()
    {
        var api = new FakeLastfmApi();
        var repository = CreateRepository(api);

        var first = await repository.GetRecentTracksAsync("user", 3, useCache: true);
        first.Content = first.Content with
        {
            RecentTracks = first.Content.RecentTracks.Where(w => w.ArtistName == "Radiohead").ToList(),
            TotalAmount = 999
        };

        var second = await repository.GetRecentTracksAsync("user", 3, useCache: true);

        Assert.Multiple(() =>
        {
            Assert.That(api.Calls, Is.EqualTo(1));
            Assert.That(second.Content.RecentTracks.Select(s => s.TrackName),
                Is.EqualTo(new[] { "Reckoner", "Roads", "Nude" }));
            Assert.That(second.Content.TotalAmount, Is.EqualTo(3));
        });
    }
}
