using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
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
        var lastPlays = new List<UserPlayTs>();

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


        var lastPlays = new List<UserPlayTs>
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
            Assert.That(result.ArtistPlaycount, Is.EqualTo(3));
            Assert.That(result.AlbumPlaycount, Is.EqualTo(2));
            Assert.That(result.TrackPlaycount, Is.EqualTo(1));
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

        var lastPlays = new List<UserPlayTs>
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
            Assert.That(result.ArtistPlaycount, Is.EqualTo(3));
            Assert.That(result.ArtistName, Is.EqualTo("Artist A"));

            Assert.That(result.AlbumPlaycount, Is.EqualTo(1));
            Assert.That(result.AlbumName, Is.Null);

            Assert.That(result.TrackPlaycount, Is.EqualTo(3));
            Assert.That(result.TrackName, Is.EqualTo("Track A"));

            Assert.That(result.StreakStarted, Is.EqualTo(lastPlays[2].TimePlayed));
        });
    }
}
