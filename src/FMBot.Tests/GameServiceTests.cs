using FMBot.Bot.Services;
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
}
