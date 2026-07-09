using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Tests;

public class PlayServiceTest
{
    [Test]
    public void GetStreak_NoLastPlays_ReturnsNull()
    {
        // Arrange
        var recentTracks = new RecentTrack();
        var lastPlays = new List<UserPlay>();

        // Act
        var result = PlayService.GetCurrentStreak(1, recentTracks, lastPlays);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void GetStreak_ArtistStreakDetected_ReturnsExpectedStreak()
    {
        // Arrange
        var recentTracks =
            new RecentTrack
            {
                ArtistName = "Artist A",
                AlbumName = "Album A",
                TrackName = "Track A",
                TimePlayed = DateTime.UtcNow
            };


        var lastPlays = new List<UserPlay>
        {
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album A",
                TrackName = "Track A",
                TimePlayed = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album A",
                TrackName = "Track B",
                TimePlayed = DateTime.UtcNow.AddHours(-2)
            },
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album B",
                TrackName = "Track B",
                TimePlayed = DateTime.UtcNow.AddHours(-3)
            },
            new()
            {
                ArtistName = "Artist B",
                AlbumName = "Album B",
                TrackName = "Track B",
                TimePlayed = DateTime.UtcNow.AddHours(-4)
            }
        };

        // Act
        var result = PlayService.GetCurrentStreak(1, recentTracks, lastPlays);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ArtistName, Is.EqualTo("Artist A"));
            Assert.That(result.ArtistPlaycount, Is.EqualTo(4));
            Assert.That(result.AlbumPlaycount, Is.EqualTo(3));
            Assert.That(result.TrackPlaycount, Is.EqualTo(2));
            Assert.That(result.StreakStarted, Is.EqualTo(lastPlays[2].TimePlayed));
        });
    }

    [Test]
    public void GetStreak_AlbumChangedHalfway_ReturnsExpectedStreak()
    {
        // Arrange
        var recentTracks = 
                    new RecentTrack
                    {
                        ArtistName = "Artist A",
                        AlbumName = "Album A",
                        TrackName = "Track A",
                        TimePlayed = DateTime.UtcNow
                    };

        var lastPlays = new List<UserPlay>
        {
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album A",
                TrackName = "Track A",
                TimePlayed = DateTime.UtcNow.AddHours(-1)
            },
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album B",
                TrackName = "Track A",
                TimePlayed = DateTime.UtcNow.AddHours(-2)
            },
            new()
            {
                ArtistName = "Artist A",
                AlbumName = "Album A",
                TrackName = "Track A",
                TimePlayed = DateTime.UtcNow.AddHours(-3)
            }
        };

        // Act
        var result = PlayService.GetCurrentStreak(1, recentTracks, lastPlays);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result.ArtistPlaycount, Is.EqualTo(4));
            Assert.That(result.ArtistName, Is.EqualTo("Artist A"));

            Assert.That(result.AlbumPlaycount, Is.EqualTo(2));
            Assert.That(result.AlbumName, Is.EqualTo("Album A"));

            Assert.That(result.TrackPlaycount, Is.EqualTo(4));
            Assert.That(result.TrackName, Is.EqualTo("Track A"));

            Assert.That(result.StreakStarted, Is.EqualTo(lastPlays[2].TimePlayed));
        });
    }

    [Test]
    public void GenreStreak_ContinuesAcrossArtistsSharingGenre()
    {
        var candidates = PlayService.SeedGenreStreakCandidates(["rock"], DateTime.UtcNow);
        var plays = new List<UserPlay>
        {
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow.AddHours(-1) },
            new() { ArtistId = 2, TimePlayed = DateTime.UtcNow.AddHours(-2) },
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow.AddHours(-3) }
        };
        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock", "indie"] },
            { 2, ["rock"] }
        };

        var anyAlive = PlayService.WalkGenreStreak(plays, candidates, genreMap);

        Assert.Multiple(() =>
        {
            Assert.That(anyAlive, Is.True);
            Assert.That(candidates[0].Playcount, Is.EqualTo(4));
            Assert.That(candidates[0].Alive, Is.True);
            Assert.That(candidates[0].StreakStarted, Is.EqualTo(plays[2].TimePlayed));
        });
    }

    [Test]
    public void GenreStreak_MissingGenreDataBreaksStreak()
    {
        var candidates = PlayService.SeedGenreStreakCandidates(["rock"], DateTime.UtcNow);
        var plays = new List<UserPlay>
        {
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow.AddHours(-1) },
            new() { ArtistId = null, TimePlayed = DateTime.UtcNow.AddHours(-2) },
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow.AddHours(-3) }
        };
        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock"] }
        };

        var anyAlive = PlayService.WalkGenreStreak(plays, candidates, genreMap);

        Assert.Multiple(() =>
        {
            Assert.That(anyAlive, Is.False);
            Assert.That(candidates[0].Playcount, Is.EqualTo(2));
            Assert.That(candidates[0].Alive, Is.False);
        });
    }

    [Test]
    public void GenreStreak_MultipleConcurrentGenres()
    {
        var candidates = PlayService.SeedGenreStreakCandidates(["rock", "metal"], DateTime.UtcNow);
        var plays = new List<UserPlay>
        {
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow.AddHours(-1) },
            new() { ArtistId = 2, TimePlayed = DateTime.UtcNow.AddHours(-2) },
            new() { ArtistId = 2, TimePlayed = DateTime.UtcNow.AddHours(-3) }
        };
        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock", "metal"] },
            { 2, ["rock"] }
        };

        var anyAlive = PlayService.WalkGenreStreak(plays, candidates, genreMap);

        var rock = candidates.First(f => f.GenreName == "rock");
        var metal = candidates.First(f => f.GenreName == "metal");
        Assert.Multiple(() =>
        {
            Assert.That(anyAlive, Is.True);
            Assert.That(rock.Playcount, Is.EqualTo(4));
            Assert.That(rock.Alive, Is.True);
            Assert.That(metal.Playcount, Is.EqualTo(2));
            Assert.That(metal.Alive, Is.False);
        });
    }

    [Test]
    public void GenreStreak_NoSeedGenres_ReturnsEmpty()
    {
        var fromNull = PlayService.SeedGenreStreakCandidates(null, DateTime.UtcNow);
        var fromEmpty = PlayService.SeedGenreStreakCandidates([], DateTime.UtcNow);

        var anyAlive = PlayService.WalkGenreStreak(new List<UserPlay>
        {
            new() { ArtistId = 1, TimePlayed = DateTime.UtcNow }
        }, fromNull, new Dictionary<int, List<string>>());

        Assert.Multiple(() =>
        {
            Assert.That(fromNull, Is.Empty);
            Assert.That(fromEmpty, Is.Empty);
            Assert.That(anyAlive, Is.False);
        });
    }

    [Test]
    public void GenreStreak_GenreOnlyStreak_SaveGating()
    {
        var bigGenreStreak = new UserStreak
        {
            ArtistPlaycount = 1,
            AlbumPlaycount = 1,
            TrackPlaycount = 1,
            GenreStreaks = [new UserGenreStreak { GenreName = "rock", Playcount = 30 }]
        };
        var smallGenreStreak = new UserStreak
        {
            ArtistPlaycount = 1,
            AlbumPlaycount = 1,
            TrackPlaycount = 1,
            GenreStreaks = [new UserGenreStreak { GenreName = "rock", Playcount = 10 }]
        };

        Assert.Multiple(() =>
        {
            Assert.That(PlayService.StreakExists(bigGenreStreak), Is.True);
            Assert.That(PlayService.ShouldSaveStreak(bigGenreStreak), Is.True);
            Assert.That(PlayService.StreakExists(smallGenreStreak), Is.True);
            Assert.That(PlayService.ShouldSaveStreak(smallGenreStreak), Is.False);
        });
    }

    [Test]
    public void GenreStreak_StreakToText_RendersTopThreeGenres()
    {
        var streak = new UserStreak
        {
            GenreStreaks =
            [
                new UserGenreStreak { GenreName = "indie rock", Playcount = 12 },
                new UserGenreStreak { GenreName = "rock", Playcount = 40 },
                new UserGenreStreak { GenreName = "shoegaze", Playcount = 8 },
                new UserGenreStreak { GenreName = "dream pop", Playcount = 5 }
            ],
            StreakStarted = DateTime.UtcNow.AddHours(-6),
            StreakEnded = DateTime.UtcNow
        };

        var text = PlayService.StreakToText(streak, NumberFormat.NoSeparator, false);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("**Rock**"));
            Assert.That(text, Does.Contain("**Indie Rock**"));
            Assert.That(text, Does.Contain("**Shoegaze**"));
            Assert.That(text, Does.Not.Contain("Dream Pop"));
            Assert.That(text.IndexOf("Rock", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("Indie Rock", StringComparison.Ordinal)));
        });
    }
}
