using FMBot.Core;
using FMBot.Domain.Models;

namespace FMBot.Tests;

public class AffinityServiceTests
{
    private static Dictionary<string, int> Ranks(params string[] names) =>
        names.Select((name, i) => (name, i + 1)).ToDictionary(d => d.name, d => d.Item2);

    private static List<AffinityItemDto> Items(int userId, params string[] names) =>
        names.Select((name, i) => new AffinityItemDto { UserId = userId, Name = name, Position = i + 1, Playcount = 100 - i }).ToList();

    [Test]
    public void GetArtistPoints_SharedTopFive_ScoresTopBucketPlusSharedPoints()
    {
        var own = Ranks("A", "B", "C", "D", "E");
        var other = Items(2, "A", "B", "C", "D", "E");

        Assert.That(AffinityService.GetArtistPoints(own, other), Is.EqualTo(5 * (32 + 2)));
    }

    [Test]
    public void GetArtistPoints_DeepSharedArtists_ScoreLowestBucketPlusSharedPoints()
    {
        var names = Enumerable.Range(1, 240).Select(i => $"artist{i}").ToArray();
        var own = Ranks(names);
        var other = Items(2, names).Where(w => w.Position > 200).ToList();

        Assert.That(AffinityService.GetArtistPoints(own, other), Is.EqualTo(40 * (1 + 2)));
    }

    [Test]
    public void GetArtistPoints_RankIsUsed_TopMatchOutscoresSameArtistDeepInOtherList()
    {
        var own = Ranks("A");
        var matchAtTop = new List<AffinityItemDto> { new() { UserId = 2, Name = "A", Position = 1 } };
        var matchDeep = new List<AffinityItemDto> { new() { UserId = 3, Name = "A", Position = 250 } };

        Assert.That(AffinityService.GetArtistPoints(own, matchAtTop),
            Is.GreaterThan(AffinityService.GetArtistPoints(own, matchDeep)));
    }

    [Test]
    public void GetArtistPoints_NoSharedArtists_ScoresZero()
    {
        var own = Ranks("A", "B");
        var other = Items(2, "C", "D");

        Assert.That(AffinityService.GetArtistPoints(own, other), Is.EqualTo(0));
    }

    [Test]
    public void SetTotalPoints_NormalizesEachComponentByViewer()
    {
        var results = new Dictionary<int, AffinityUser>
        {
            [1] = new() { UserId = 1, ArtistPoints = 1000, GenrePoints = 400, CountryPoints = 200 },
            [2] = new() { UserId = 2, ArtistPoints = 500, GenrePoints = 400, CountryPoints = 0 }
        };

        AffinityService.SetTotalPoints(results, 1);

        Assert.That(results[1].TotalPoints, Is.EqualTo(1.1).Within(1e-9));
        Assert.That(results[2].TotalPoints, Is.EqualTo(0.5 + 0.05).Within(1e-9));
    }

    [Test]
    public void SetTotalPoints_ArtistsDominateRanking()
    {
        var results = new Dictionary<int, AffinityUser>
        {
            [1] = new() { UserId = 1, ArtistPoints = 1000, GenrePoints = 400, CountryPoints = 200 },
            [2] = new() { UserId = 2, ArtistPoints = 300, GenrePoints = 400, CountryPoints = 200 },
            [3] = new() { UserId = 3, ArtistPoints = 400, GenrePoints = 0, CountryPoints = 0 }
        };

        AffinityService.SetTotalPoints(results, 1);

        Assert.That(results[3].TotalPoints, Is.GreaterThan(results[2].TotalPoints));
    }

    [Test]
    public void SetTotalPoints_ViewerWithoutGenresOrCountries_SkipsThoseComponents()
    {
        var results = new Dictionary<int, AffinityUser>
        {
            [1] = new() { UserId = 1, ArtistPoints = 1000 },
            [2] = new() { UserId = 2, ArtistPoints = 250, GenrePoints = 300, CountryPoints = 100 }
        };

        AffinityService.SetTotalPoints(results, 1);

        Assert.That(results[1].TotalPoints, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(results[2].TotalPoints, Is.EqualTo(0.25).Within(1e-9));
    }

    [Test]
    public void SetTotalPoints_ViewerMissing_LeavesTotalsUntouched()
    {
        var results = new Dictionary<int, AffinityUser>
        {
            [2] = new() { UserId = 2, ArtistPoints = 250, TotalPoints = 7 }
        };

        AffinityService.SetTotalPoints(results, 1);

        Assert.That(results[2].TotalPoints, Is.EqualTo(7));
    }
}
