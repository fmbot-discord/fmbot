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

    private static List<UserPlay> GenerateHistoricalPlays(
        params (string Artist, string? Album, string Track, int Count)[] segments)
    {
        var plays = new List<UserPlay>();
        var time = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var (artist, album, track, count) in segments)
        {
            for (var i = 0; i < count; i++)
            {
                plays.Add(new UserPlay
                {
                    ArtistName = artist,
                    AlbumName = album,
                    TrackName = track,
                    TimePlayed = time
                });
                time = time.AddMinutes(3);
            }
        }

        return plays;
    }

    [Test]
    public void HistoricalStreaks_BelowThreshold_ReturnsEmpty()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", "Album A", "Track A", 24),
            ("Artist B", "Album B", "Track B", 24));

        var result = PlayService.GetHistoricalStreaks(1, plays);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void HistoricalStreaks_SingleBinge_MergesDimensionsIntoOneStreak()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist B", "Album B", "Track B", 5),
            ("Artist A", "Album A", "Track A", 30),
            ("Artist B", "Album B", "Track B", 5));

        var result = PlayService.GetHistoricalStreaks(1, plays);

        Assert.That(result, Has.Count.EqualTo(1));
        var streak = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(streak.ArtistName, Is.EqualTo("Artist A"));
            Assert.That(streak.ArtistPlaycount, Is.EqualTo(30));
            Assert.That(streak.AlbumName, Is.EqualTo("Album A"));
            Assert.That(streak.AlbumPlaycount, Is.EqualTo(30));
            Assert.That(streak.TrackName, Is.EqualTo("Track A"));
            Assert.That(streak.TrackPlaycount, Is.EqualTo(30));
            Assert.That(streak.StreakStarted, Is.EqualTo(plays[5].TimePlayed));
            Assert.That(streak.StreakEnded, Is.EqualTo(plays[34].TimePlayed));
        });
    }

    [Test]
    public void HistoricalStreaks_TrackRunInsideArtistRun_GetsOwnStreakWithArtistContext()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", "Album 1", "Track 1", 10),
            ("Artist A", "Album 2", "Track 2", 30),
            ("Artist A", "Album 3", "Track 3", 10),
            ("Artist B", "Album B", "Track B", 5));

        var result = PlayService.GetHistoricalStreaks(1, plays);

        Assert.That(result, Has.Count.EqualTo(2));

        var artistStreak = result[0];
        var trackStreak = result[1];
        Assert.Multiple(() =>
        {
            Assert.That(artistStreak.ArtistName, Is.EqualTo("Artist A"));
            Assert.That(artistStreak.ArtistPlaycount, Is.EqualTo(50));
            Assert.That(artistStreak.TrackName, Is.Null);
            Assert.That(artistStreak.StreakStarted, Is.EqualTo(plays[0].TimePlayed));

            Assert.That(trackStreak.ArtistPlaycount, Is.Null);
            Assert.That(trackStreak.ArtistName, Is.EqualTo("Artist A"));
            Assert.That(trackStreak.TrackName, Is.EqualTo("Track 2"));
            Assert.That(trackStreak.TrackPlaycount, Is.EqualTo(30));
            Assert.That(trackStreak.AlbumName, Is.EqualTo("Album 2"));
            Assert.That(trackStreak.AlbumPlaycount, Is.EqualTo(30));
            Assert.That(trackStreak.StreakStarted, Is.EqualTo(plays[10].TimePlayed));
            Assert.That(trackStreak.StreakEnded, Is.EqualTo(plays[39].TimePlayed));
        });
    }

    [Test]
    public void HistoricalStreaks_CaseInsensitiveContinuation()
    {
        var plays = GenerateHistoricalPlays(
            ("artist a", "album a", "track a", 15),
            ("Artist A", "Album A", "Track A", 15),
            ("Artist B", "Album B", "Track B", 5));

        var result = PlayService.GetHistoricalStreaks(1, plays);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].ArtistPlaycount, Is.EqualTo(30));
    }

    [Test]
    public void HistoricalStreaks_GenreRun_ContinuesAcrossArtists()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", "Album A", "Track A", 15),
            ("Artist B", "Album B", "Track B", 15),
            ("Artist C", "Album C", "Track C", 5));
        foreach (var play in plays)
        {
            play.ArtistId = play.ArtistName switch
            {
                "Artist A" => 1,
                "Artist B" => 2,
                _ => 3
            };
        }

        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock", "indie"] },
            { 2, ["rock"] },
            { 3, ["pop"] }
        };

        var result = PlayService.GetHistoricalStreaks(1, plays, genreMap);

        Assert.That(result, Has.Count.EqualTo(1));
        var streak = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(streak.ArtistName, Is.Null);
            Assert.That(streak.GenreStreaks, Has.Count.EqualTo(1));
            Assert.That(streak.GenreStreaks[0].GenreName, Is.EqualTo("rock"));
            Assert.That(streak.GenreStreaks[0].Playcount, Is.EqualTo(30));
            Assert.That(streak.StreakStarted, Is.EqualTo(plays[0].TimePlayed));
            Assert.That(streak.StreakEnded, Is.EqualTo(plays[29].TimePlayed));
        });
    }

    [Test]
    public void HistoricalStreaks_GenreRunSameStartAsArtistRun_MergesIntoOneStreak()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", "Album A", "Track A", 30),
            ("Artist B", "Album B", "Track B", 5));
        foreach (var play in plays)
        {
            play.ArtistId = play.ArtistName == "Artist A" ? 1 : 2;
        }

        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock"] },
            { 2, ["pop"] }
        };

        var result = PlayService.GetHistoricalStreaks(1, plays, genreMap);

        Assert.That(result, Has.Count.EqualTo(1));
        var streak = result[0];
        Assert.Multiple(() =>
        {
            Assert.That(streak.ArtistName, Is.EqualTo("Artist A"));
            Assert.That(streak.ArtistPlaycount, Is.EqualTo(30));
            Assert.That(streak.GenreStreaks, Has.Count.EqualTo(1));
            Assert.That(streak.GenreStreaks[0].GenreName, Is.EqualTo("rock"));
            Assert.That(streak.GenreStreaks[0].Playcount, Is.EqualTo(30));
        });
    }

    [Test]
    public void HistoricalStreaks_MissingArtistId_BreaksGenreRun()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", "Album A", "Track A", 15),
            ("Artist B", "Album B", "Track B", 1),
            ("Artist A", "Album A", "Track A", 15));
        foreach (var play in plays.Where(w => w.ArtistName == "Artist A"))
        {
            play.ArtistId = 1;
        }

        var genreMap = new Dictionary<int, List<string>>
        {
            { 1, ["rock"] }
        };

        var result = PlayService.GetHistoricalStreaks(1, plays, genreMap);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void HistoricalStreaks_NullAlbums_BreakAlbumRunWithoutError()
    {
        var plays = GenerateHistoricalPlays(
            ("Artist A", null, "Track A", 30),
            ("Artist B", "Album B", "Track B", 5));

        var result = PlayService.GetHistoricalStreaks(1, plays);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result[0].ArtistPlaycount, Is.EqualTo(30));
            Assert.That(result[0].AlbumName, Is.Null);
            Assert.That(result[0].AlbumPlaycount, Is.Null);
            Assert.That(result[0].TrackPlaycount, Is.EqualTo(30));
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
