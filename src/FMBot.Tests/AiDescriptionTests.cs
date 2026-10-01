using FMBot.Bot.Services;

namespace FMBot.Tests;

public class AiDescriptionTests
{
    private const string Grounding =
        "ENTITY: artist\nNAME: Muse\n\nSOURCE (Last.fm biography):\n" +
        "Muse are an English rock band from Teignmouth, formed in 1994. They sold 1200000 copies of their " +
        "breakthrough album and are known for their energetic live performances.\n\n" +
        "METADATA (verified):\nCountry: United Kingdom\nGenres: alternative rock, progressive rock";

    [Test]
    public void ValidTwoSentenceDescriptionIsAccepted()
    {
        var valid = OpenAiService.TryValidateDescription(
            "Muse are an English rock band formed in Teignmouth in 1994. They are known for their energetic live " +
            "performances and a blend of alternative and progressive rock.", Grounding, out var cleaned,
            out _);

        Assert.That(valid, Is.True);
        Assert.That(cleaned, Does.StartWith("Muse are an English rock band"));
    }

    [Test]
    public void InsufficientBailOutIsRejectedWithItsOwnReason()
    {
        var valid = OpenAiService.TryValidateDescription("INSUFFICIENT", Grounding, out _, out var reason);

        Assert.That(valid, Is.False);
        Assert.That(reason, Is.EqualTo("insufficient"));
    }

    [Test]
    public void UngroundedYearIsRejected()
    {
        var valid = OpenAiService.TryValidateDescription(
            "Muse are an English rock band formed in Teignmouth in 1812. They are known for their energetic live " +
            "performances and a blend of alternative and progressive rock.", Grounding, out _, out _);

        Assert.That(valid, Is.False);
    }

    [Test]
    public void GroundedNumberWithSeparatorsIsAccepted()
    {
        var valid = OpenAiService.TryValidateDescription(
            "Muse sold 1,200,000 copies of their breakthrough album and became a mainstay of British rock. " +
            "They are known for their energetic live performances.", Grounding, out _, out _);

        Assert.That(valid, Is.True);
    }

    [Test]
    public void AbbreviationDoesNotCountAsASentenceEnd()
    {
        const string grounding = "SOURCE: Dr. Dre released The Chronic in 1992, a landmark West Coast hip hop album.";

        var valid = OpenAiService.TryValidateDescription(
            "Dr. Dre released the album in 1992 and it became a landmark of West Coast hip hop. It remains widely " +
            "celebrated today.", grounding, out _, out _);

        Assert.That(valid, Is.True);
    }

    [Test]
    public void WrappingCurlyQuotesAreStripped()
    {
        var valid = OpenAiService.TryValidateDescription(
            "“Muse are an English rock band formed in Teignmouth in 1994. They play alternative and progressive " +
            "rock.”", Grounding, out var cleaned, out _);

        Assert.That(valid, Is.True);
        Assert.That(cleaned, Does.Not.StartWith("“"));
        Assert.That(cleaned, Does.Not.EndWith("”"));
    }

    [Test]
    public void SentencesEndingInsideCurlyQuotesAreAccepted()
    {
        const string grounding =
            "ENTITY: artist\nNAME: FLO\n\nSOURCE (Last.fm biography):\nFLO are a British R&B trio who debuted in " +
            "2022 with Cardboard Box. Their 2024 singles include Walk Like This, Caught Up and Check.";

        var valid = OpenAiService.TryValidateDescription(
            "FLO are a Grammy-nominated British R&B trio who debuted in 2022 with “Cardboard Box.” Their 2024 " +
            "singles include the TikTok-trending “Walk Like This,” guitar-focused “Caught Up,” and fast-paced pop " +
            "track “Check.”", grounding, out var cleaned, out _);

        Assert.That(valid, Is.True);
        Assert.That(cleaned, Does.StartWith("FLO are a Grammy-nominated"));
    }

    [Test]
    public void ChartPositionIsNotTreatedAsAHeading()
    {
        const string grounding =
            "ENTITY: album\nNAME: petal\nARTIST: Ariana Grande\n\nSOURCE (Last.fm album wiki):\npetal was " +
            "co-written and executively produced by Ariana Grande and ILYA. Its lead single Hate That I Made You " +
            "Love Me debuted at number 1 on the Billboard Hot 100.";

        var valid = OpenAiService.TryValidateDescription(
            "petal was co-written and executively produced by Ariana Grande and Swedish producer ILYA. Its lead " +
            "single, “Hate That I Made You Love Me,” debuted at #1 on the Billboard Hot 100.", grounding, out _,
            out _);

        Assert.That(valid, Is.True);
    }

    [Test]
    public void NewLinesAreCollapsed()
    {
        var valid = OpenAiService.TryValidateDescription(
            "Muse are an English rock band formed in Teignmouth in 1994.\n\nThey play alternative and progressive " +
            "rock.", Grounding, out var cleaned, out _);

        Assert.That(valid, Is.True);
        Assert.That(cleaned, Does.Not.Contain("\n"));
    }

    [Test]
    [TestCase("**Muse** are an English rock band formed in Teignmouth in 1994. They play alternative rock.")]
    [TestCase("Muse are an English rock band. More at https://www.last.fm/music/Muse for their full history.")]
    [TestCase("# Muse\nAn English rock band formed in Teignmouth in 1994. They play alternative rock music.")]
    [TestCase("Muse are an English rock band formed in Teignmouth. See <a href=\"x\">here</a> for more info.")]
    [TestCase("Muse are an English rock band formed in Teignmouth in 1994. ```They play alternative rock.```")]
    public void MarkdownAndLinkContaminationIsRejected(string raw)
    {
        Assert.That(OpenAiService.TryValidateDescription(raw, Grounding, out _, out _), Is.False);
    }

    [Test]
    [TestCase("As an AI, I cannot write a description for this artist without more detail to work from.")]
    [TestCase("There is not enough information here to describe this artist in any meaningful way at all.")]
    [TestCase("The provided text does not say much about this artist beyond the fact that they exist.")]
    public void RefusalAndMetaCommentaryIsRejected(string raw)
    {
        Assert.That(OpenAiService.TryValidateDescription(raw, Grounding, out _, out _), Is.False);
    }
}
