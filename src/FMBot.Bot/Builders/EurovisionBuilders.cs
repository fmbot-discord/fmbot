using System.Collections.Generic;
using System.Linq;
using System.Text;
using FMBot.Bot.Models;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class EurovisionBuilders
{
    private readonly UserService _userService;
    private readonly EurovisionService _eurovisionService;
    private readonly CountryService _countryService;

    public EurovisionBuilders(UserService userService, EurovisionService eurovisionService,
        CountryService countryService)
    {
        this._userService = userService;
        this._eurovisionService = eurovisionService;
        this._countryService = countryService;
    }

    public async Task<ResponseModel> GetEurovisionOverview(ContextModel context, int year, string country)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var contestants = await this._eurovisionService.GetEntries(year);

        if (contestants == null || !contestants.Any())
        {
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            response.Embed.WithDescription($"No known Eurovision contestants for {year} (yet).");
            return response;
        }

        var pages = new List<PageBuilder>();
        var pageCounter = 1;
        var eurovisionPages = contestants
            .OrderByDescending(o => o.ReachedFinals)
            .ThenByDescending(o => o.Score)
            .ThenBy(o => o.SemiFinalNr)
            .ThenByDescending(o => o.SemiFinalScore)
            .ThenBy(o => o.Draw).Chunk(8)
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
                        $"#{eurovisionEntry.Position} — {validCountry.Name}");
                }
                else
                {
                    pageDescription.Append(
                        $"{validCountry.Name}");
                }

                if (!eurovisionEntry.HasScore)
                {
                    if (eurovisionEntry.ReachedFinals)
                    {
                        pageDescription.Append($" — Finals");
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
                            $" — {eurovisionEntry.SemiFinalNr}{StringExtensions.GetAmountEnd(eurovisionEntry.SemiFinalNr)} semi-final - {eurovisionEntry.Draw}{StringExtensions.GetAmountEnd(eurovisionEntry.Draw)} running");
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
            footer.Append($"Page {pageCounter}/{eurovisionPages.Count()}");
            footer.AppendLine($" - {contestants.Count} total entries");
            footer.Append("Data provided by ESC Discord");

            pages.Add(new PageBuilder()
                .WithTitle($"Eurovision {year} <:eurovision:1084971471610323035>")
                .WithDescription(pageDescription.ToString())
                .WithFooter(footer.ToString()));
            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        response.ResponseType = ResponseType.Paginator;

        return response;
    }
}
