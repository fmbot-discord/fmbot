using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Models;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using Web.InternalApi;

namespace FMBot.Bot.Builders;

public class EurovisionBuilders
{
    private readonly UserService _userService;
    private readonly EurovisionService _eurovisionService;
    private readonly CountryService _countryService;
    private readonly WhoKnowsTrackService _whoKnowsTrackService;

    public EurovisionBuilders(UserService userService, EurovisionService eurovisionService,
        CountryService countryService, WhoKnowsTrackService whoKnowsTrackService)
    {
        this._userService = userService;
        this._eurovisionService = eurovisionService;
        this._countryService = countryService;
        this._whoKnowsTrackService = whoKnowsTrackService;
    }

    public async Task<ResponseModel> GetEurovisionYear(ContextModel context, int year)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var contest = await this._eurovisionService.GetEntries(year);
        var contestants = contest?.Entries?.ToList();

        if (contestants == null || !contestants.Any())
        {
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            response.Embed.WithDescription($"No known Eurovision contestants for {year} (yet).");
            return response;
        }

        var hostCountry = this._countryService.GetValidCountry(contest.HostCountry);

        var pages = new List<PageBuilder>();
        var pageCounter = 1;
        var eurovisionPages = contestants
            .OrderByDescending(o => o.ReachedFinals)
            .ThenByDescending(o => o.Score)
            .ThenBy(o => o.Draw)
            .ThenBy(o => o.SemiFinalNr)
            .ThenBy(o => o.SemiFinalDraw)
            .ThenByDescending(o => o.SemiFinalScore)
            .Chunk(8)
            .ToList();

        foreach (var page in eurovisionPages)
        {
            var pageDescription = new StringBuilder();

            foreach (var eurovisionEntry in page)
            {
                var validCountry = this._countryService.GetValidCountry(eurovisionEntry.EntryCode);

                if (eurovisionEntry.HasPosition)
                {
                    pageDescription.Append(
                        $"#{eurovisionEntry.Position} — {validCountry?.Name ?? eurovisionEntry.EntryCode}");
                }
                else
                {
                    pageDescription.Append(
                        $"{validCountry?.Name ?? eurovisionEntry.EntryCode}");
                }

                if (!eurovisionEntry.HasScore)
                {
                    if (eurovisionEntry.ReachedFinals)
                    {
                        pageDescription.Append($" — Grand final");
                        if (eurovisionEntry.HasDraw)
                        {
                            pageDescription.Append(
                                $" — {eurovisionEntry.Draw}{StringExtensions.GetAmountEnd(eurovisionEntry.Draw)} running");
                        }
                    }
                    else if (eurovisionEntry.HasSemiFinalScore)
                    {
                        pageDescription.Append(
                            $" — {eurovisionEntry.SemiFinalNr}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalNr)} semi-final - {eurovisionEntry.SemiFinalScore} points");
                    }
                    else
                    {
                        pageDescription.Append(
                            $" — {eurovisionEntry.SemiFinalNr}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalNr)} semi-final - {eurovisionEntry.SemiFinalDraw}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalDraw)} running");
                    }
                }
                else
                {
                    pageDescription.Append($" — {eurovisionEntry.Score}");
                }

                pageDescription.AppendLine();
                pageDescription.Append($":flag_{eurovisionEntry.EntryCode}:  ");
                if (!string.IsNullOrWhiteSpace(eurovisionEntry.VideoLink))
                {
                    pageDescription.Append(
                        $"**[{eurovisionEntry.Artist} - {eurovisionEntry.Title}]({eurovisionEntry.VideoLink})**");
                }
                else
                {
                    pageDescription.Append($"**{eurovisionEntry.Artist} - {eurovisionEntry.Title}**");
                }

                pageDescription.AppendLine();
                pageDescription.AppendLine();
            }

            var footer = new StringBuilder();


            footer.AppendLine("Data provided by ESC Discord / ranked.be");
            footer.Append($"Page {pageCounter}/{eurovisionPages.Count()}");
            footer.Append($" - {contestants.Count} total entries");
            footer.Append($" - Add country for details");

            pages.Add(new PageBuilder()
                .WithTitle($"Eurovision {year} in {contest.HostCity}, {hostCountry.Name} <:eurovision:1084971471610323035>")
                .WithDescription(pageDescription.ToString())
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        response.ResponseType = ResponseType.Paginator;

        return response;
    }

    public async Task<ResponseModel> GetEurovisionCountryYear(ContextModel context, CountryInfo country, int year)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var entry = await this._eurovisionService.GetEntry(year, country.Code);
        var votes = await this._eurovisionService.GetVotesForEntry(year, country.Code);

        if (entry == null)
        {
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            response.Embed.WithDescription($"Could not find Eurovision entry for {country.Name} in {year} (yet).");
            return response;
        }

        response.Embed.WithTitle($":flag_{country.Code.ToLower()}: Eurovision entry for {country.Name} in {year}");
        var description = new StringBuilder();
        description.AppendLine($"### [{entry.Title} by {entry.Artist}]({entry.VideoLink})");
        description.Append($"Sung in **{entry.Languages}**");
        if (!string.IsNullOrWhiteSpace(entry.ExtraLanguages))
        {
            description.Append($" (with {entry.ExtraLanguages})");
        }
        response.Embed.WithDescription(description.ToString());

        var positionDesc = new StringBuilder();
        if (entry.HasPosition)
        {
            positionDesc.Append($"- **#{entry.Position}** in Grand final");
            if (entry.HasScore)
            {
                positionDesc.Append($" with **{entry.Score} points**");
            }

            if (entry.Position == 1)
            {
                positionDesc.Append($" 👑");
            }

            positionDesc.AppendLine();
        }
        else if (entry.HasDraw)
        {
            positionDesc.AppendLine(
                $"- **{entry.Draw}{StringExtensions.GetAmountEnd(entry.Draw)} running** in Grand final");
        }

        if (entry.HasSemiFinalPosition)
        {
            positionDesc.Append(
                $"- **#{entry.SemiFinalPosition}** in {entry.SemiFinalNr}{StringExtensions.GetAmountEnd(entry.SemiFinalNr)} semi-final");
            if (entry.HasSemiFinalScore)
            {
                positionDesc.Append($" with **{entry.SemiFinalScore} points**");
            }

            positionDesc.AppendLine();
        }
        else if (entry.HasSemiFinalDraw)
        {
            positionDesc.AppendLine(
                $"- **{entry.SemiFinalDraw}{StringExtensions.GetAmountEnd(entry.SemiFinalDraw)} running** in {entry.SemiFinalNr}{StringExtensions.GetAmountEnd(entry.SemiFinalNr)} semi-final");
        }

        if (positionDesc.Length > 0)
        {
            response.Embed.AddField("Positions", positionDesc.ToString());
        }

        var juryVotes = votes
            .Where(w => string.Equals(w.ToCountry, country.Code, StringComparison.OrdinalIgnoreCase)
                        && w.VoteType == VoteType.JuryVotes
                        && w.Points > 0).ToList();
        if (juryVotes.Any())
        {
            var juryVotesDesc = new StringBuilder();
            juryVotesDesc.AppendLine($"**{juryVotes.Count}**  ·  Total");
            foreach (var vote in juryVotes
                         .OrderByDescending(o => o.Points)
                         .Take(8))
            {
                var votedCountry = this._countryService.GetValidCountry(vote.FromCountry);
                juryVotesDesc.AppendLine($"**{vote.Points}**  ·  {votedCountry.Emoji} {votedCountry.Name}");
            }

            response.Embed.AddField("Received jury points", juryVotesDesc.ToString(), true);
        }

        var teleVotes = votes
            .Where(w => string.Equals(w.ToCountry, country.Code, StringComparison.OrdinalIgnoreCase)
                        && w.VoteType == VoteType.TeleVotes
                        && w.Points > 0).ToList();
        if (teleVotes.Any())
        {
            var teleVotesDesc = new StringBuilder();
            teleVotesDesc.AppendLine($"**{teleVotes.Count}**  ·  Total");
            foreach (var vote in teleVotes
                         .OrderByDescending(o => o.Points)
                         .Take(8))
            {
                var votedCountry = this._countryService.GetValidCountry(vote.FromCountry);
                teleVotesDesc.AppendLine($"**{vote.Points}**  ·  {votedCountry.Emoji} {votedCountry.Name}");
            }

            response.Embed.AddField("Received televote points", teleVotesDesc.ToString(), true);
        }

        var artistName = entry.Artist.Split(" and ")[0];
        response.ReferencedMusic = new ReferencedMusic
        {
            Artist = artistName,
            Track = entry.Title
        };

        if (context.ContextUser != null)
        {
            var userPlaycount = await this._whoKnowsTrackService.GetTrackPlayCountForUser(artistName,
                entry.Title, context.ContextUser.UserId);

            if (userPlaycount > 0)
            {
                response.Embed.WithFooter(
                    $"{userPlaycount.Format(context.NumberFormat)} {StringExtensions.GetPlaysString(userPlaycount)}");
            }
        }

        return response;
    }
}
