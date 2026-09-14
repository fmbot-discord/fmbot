using System.Collections.Generic;
using System.Linq;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Tests;

[TestFixture]
public class GameServiceTests
{
    [Test]
    [TestCase("Yesterday", "Yesterday", true, Description = "Exact match should be correct")]
    [TestCase("Yesterday", "Yesterda", true, Description = "One character typo should be correct")]
    [TestCase("Dazey and the Scouts", "Dazey and the Scxxts", true,
        Description = "Two character typos in long name should be correct")]
    [TestCase("Yesterday...", "Yesterday…", true, Description = "Different types of dots should be equal")]
    [TestCase("Dazey and the Scouts", "Dazey & the Scouts", true, Description = "'and' and '&' should be equal")]
    [TestCase("Where I’ve been isn’t where I’m going", "Where I've Been, Isn't Where I'm Going", true, Description = "'and' and '&' should be equal")]
    [TestCase("MUNA", "muna", true, Description = "Case should not matter")]
    [TestCase("Sufjan Stevens", "Suffjan Stevens", true, Description = "Common misspelling should be accepted")]
    [TestCase("Björk", "Bjork", true, Description = "Special characters should be normalized")]
    [TestCase("CHVRCHES", "Chvrches", true, Description = "Stylized capitalization should not matter")]
    [TestCase("Twenty One Pilots", "twenty one pilots", true, Description = "All lowercase should be accepted")]
    [TestCase("AC/DC", "ACDC", true, Description = "Special characters should be removed")]
    [TestCase("$uicideboy$", "Suicideboys", true, Description = "Special characters should be normalized")]
    // Negative cases
    [TestCase("Yesterday", "Tomorrow", false, Description = "Completely different words should be incorrect")]
    [TestCase("Nine inch nails", "Four inch nails", false, Description = "Completely different words should be incorrect")]
    public void AnswerIsRight_ValidatesCorrectly(string correctAnswer, string userInput, bool expectedResult)
    {
        // Arrange
        var session = new JumbleSession
        {
            CorrectAnswer = correctAnswer
        };

        // Act
        var result = GameService.AnswerIsRight(session, userInput);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult),
            $"Expected '{userInput}' to be {(expectedResult ? "accepted" : "rejected")} for correct answer '{correctAnswer}'");
    }

    [Test]
    public void AnswerIsRight_HandlesDiacritics()
    {
        // Arrange
        var session = new JumbleSession
        {
            CorrectAnswer = "Sigur Rós"
        };

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(GameService.AnswerIsRight(session, "Sigur Ros"), Is.True, "Should accept without diacritics");
            Assert.That(GameService.AnswerIsRight(session, "sigur ros"), Is.True,
                "Should accept lowercase without diacritics");
            Assert.That(GameService.AnswerIsRight(session, "SIGUR ROS"), Is.True,
                "Should accept uppercase without diacritics");
        });
    }

    [Test]
    public void AnswerIsRight_HandlesSpecialCharacters()
    {
        // Arrange
        var session = new JumbleSession
        {
            CorrectAnswer = "Motörhead"
        };

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(GameService.AnswerIsRight(session, "Motorhead"), Is.True, "Should accept without umlaut");
            Assert.That(GameService.AnswerIsRight(session, "MOTORHEAD"), Is.True,
                "Should accept uppercase without umlaut");
            Assert.That(GameService.AnswerIsRight(session, "MotorHead"), Is.True, "Should accept different casing");
        });
    }

    [Test]
    public void AnswerIsRight_HandlesQuotationMarks()
    {
        // Arrange
        var session = new JumbleSession
        {
            CorrectAnswer = "Guns N' Roses"
        };

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.That(GameService.AnswerIsRight(session, "Guns N Roses"), Is.True,
                "Should accept without apostrophe");
            Assert.That(GameService.AnswerIsRight(session, "Guns and Roses"), Is.True, "Should accept with 'and'");
            Assert.That(GameService.AnswerIsRight(session, "guns n roses"), Is.True, "Should accept lowercase");
        });
    }

    [Test]
    [TestCase("Abbey Road", "Abbey Road (Remastered)", true, Description = "Should accept with edition suffix in input")]
    [TestCase("Abbey Road", "Abbey Road (Deluxe Edition)", true, Description = "Should accept with different edition suffix")]
    [TestCase("The Album", "The Album - The 1st Album", true, Description = "Should accept with K-pop album suffix")]
    [TestCase("The Album", "The Album - The 2nd Mini Album", true, Description = "Should accept with K-pop mini album suffix")]
    [TestCase("The Album", "The Album - The 3rd Album Repackage", true, Description = "Should accept with K-pop repackage suffix")]
    [TestCase("Album Name", "Album Name (Live) (Remastered)", true, Description = "Should accept with multiple edition suffixes")]
    // Additional edge cases
    [TestCase("Album Name", "Album Name (2024 Master)", true, Description = "Should accept with year in edition")]
    [TestCase("Album Name", "Album Name (Super Deluxe Box Set)", true, Description = "Should accept with complex edition name")]
    public void AnswerIsRight_HandlesEditionSuffixes(string correctAnswer, string userInput, bool expectedResult)
    {
        // Arrange
        var session = new JumbleSession
        {
            CorrectAnswer = correctAnswer
        };

        // Act
        var result = GameService.AnswerIsRight(session, userInput);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult),
            $"Expected '{userInput}' to be {(expectedResult ? "accepted" : "rejected")} for correct answer '{correctAnswer}'");
    }

    [Test]
    public void OrderHints_KeepsGiveawayHintsOutOfTheOpening()
    {
        for (var i = 0; i < 50; i++)
        {
            var hints = new List<JumbleSessionHint>
            {
                new(JumbleHintType.PopularTrack, "track"),
                new(JumbleHintType.Playcount, "plays"),
                new(JumbleHintType.OtherAlbum, "album"),
                new(JumbleHintType.MoreGenres, "more genres"),
                new(JumbleHintType.StartDate, "start"),
                new(JumbleHintType.FirstLetter, "letter"),
                new(JumbleHintType.Genre, "genre")
            };

            var ordered = GameService.OrderHints(hints);

            Assert.Multiple(() =>
            {
                Assert.That(ordered, Has.Count.EqualTo(7));
                Assert.That(ordered.Count(c => c.HintShown), Is.EqualTo(3));
                Assert.That(ordered.Take(3).Select(s => s.Type),
                    Is.EquivalentTo(new[] { JumbleHintType.Playcount, JumbleHintType.StartDate, JumbleHintType.Genre }));
                Assert.That(ordered.Skip(3).Select(s => s.Type),
                    Is.EqualTo(new[]
                    {
                        JumbleHintType.MoreGenres, JumbleHintType.FirstLetter, JumbleHintType.OtherAlbum,
                        JumbleHintType.PopularTrack
                    }));
                Assert.That(ordered.Select(s => s.Order), Is.EqualTo(Enumerable.Range(0, 7)));
            });
        }
    }

    [Test]
    public void BuildExclusionSet_AlwaysExcludesTodayAndCapsAtSixtyPercentOfPool()
    {
        var recent = Enumerable.Range(0, 400)
            .Select(i => new JumbleSession
            {
                CorrectAnswer = $"artist {i}",
                DateStarted = i < 5 ? System.DateTime.Today.AddHours(1) : System.DateTime.Today.AddDays(-1 - i)
            })
            .ToList();

        var excluded = GameService.BuildExclusionSet(recent, s => s.CorrectAnswer, 100);

        Assert.Multiple(() =>
        {
            Assert.That(excluded, Has.Count.EqualTo(60));
            Assert.That(excluded, Does.Contain("artist 0").And.Contain("artist 4"));
            Assert.That(excluded, Does.Contain("artist 5").And.Not.Contain("artist 399"));
        });

        var tiny = GameService.BuildExclusionSet(recent, s => s.CorrectAnswer, 3);
        Assert.That(tiny, Has.Count.EqualTo(5), "today's answers are always excluded even past the cap");
    }

    [Test]
    public void PickHintCandidate_ExcludesNamesThatGiveAwayTheAnswer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GameService.PickHintCandidate(null, "Weezer"), Is.Null);
            Assert.That(GameService.PickHintCandidate(["Weezer", "Weezer (Deluxe)"], "Weezer"), Is.Null);
            Assert.That(GameService.PickHintCandidate(["Weezer", "Pinkerton (Deluxe Edition)"], "Weezer"), Is.EqualTo("Pinkerton"));
            Assert.That(GameService.PickHintCandidate(["IGOR", "Igor's Theme", "EARFQUAKE"], "IGOR"), Is.EqualTo("EARFQUAKE"));
            Assert.That(GameService.PickHintCandidate(["Chromakopia", "Flower Boy"], "Chromakopia", "Tyler, The Creator"), Is.EqualTo("Flower Boy"));
            Assert.That(GameService.PickHintCandidate(["Strings of Fear", "Other Song"], "6 Feet Deep", "Strings of Fear"), Is.EqualTo("Other Song"));
            Assert.That(GameService.PickHintCandidate(["Strings of Fear"], "6 Feet Deep", null), Is.EqualTo("Strings of Fear"));
        });
    }

    [TestCase("Weezer", "W")]
    [TestCase("  the strokes", "T")]
    [TestCase("$uicideboy$", "U")]
    [TestCase("(G)I-DLE", "G")]
    [TestCase("*NSYNC", "N")]
    [TestCase("21 Savage", "2")]
    [TestCase("Édith Piaf", "É")]
    [TestCase("𝔐orbid", "𝔐")]
    [TestCase("¥$", null)]
    [TestCase("", null)]
    [TestCase(null, null)]
    public void FirstLetter_SkipsSymbolsAndKeepsWholeCharacters(string? answer, string? expected)
    {
        Assert.That(GameService.FirstLetter(answer), Is.EqualTo(expected));
    }

    [Test]
    public void TracksFromOtherAlbums_DropsTracksOnTheAlbumBeingGuessed()
    {
        var tracks = new List<ArtistHintTrack>
        {
            new() { Name = "EARFQUAKE", AlbumName = "IGOR" },
            new() { Name = "NEW MAGIC WAND", AlbumName = "IGOR (Deluxe)" },
            new() { Name = "See You Again", AlbumName = "Flower Boy" },
            new() { Name = "Yonkers", AlbumName = null }
        };

        Assert.Multiple(() =>
        {
            Assert.That(GameService.TracksFromOtherAlbums(tracks, "IGOR"), Is.EqualTo(new[] { "See You Again" }));
            Assert.That(GameService.TracksFromOtherAlbums(null, "IGOR"), Is.Null);
        });
    }
}
