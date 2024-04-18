using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using FMBot.Bot.Models;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Primitives;

namespace FMBot.Bot.Builders;

public class EurovisionBuilders
{
    private readonly UserService _userService;
    private readonly EurovisionService _eurovisionService;

    public EurovisionBuilders(UserService userService, EurovisionService eurovisionService)
    {
        this._userService = userService;
        this._eurovisionService = eurovisionService;
    }

    public async Task<ResponseModel> GetEurovisionOverview(ContextModel context, int year, string country)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._eurovisionService.UpdateEurovisionData();

        PublicProperties.EurovisionYears.TryGetValue(year, out var contestants);

        var pages = new List<PageBuilder>();
        var pageCounter = 1;
        var eurovisionPages = contestants.OrderByDescending(o => o.PointsFinal).ThenByDescending(o => o.SfNum.HasValue).ThenBy(o => o.SfNum).Chunk(8);

        foreach (var page in eurovisionPages)
        {
            var pageDescription = new StringBuilder();

            foreach (var eurovisionEntry in page)
            {

                if (eurovisionEntry.PlaceContest.HasValue)
                {
                    pageDescription.Append(
                        $"#{eurovisionEntry.PlaceContest} — {eurovisionEntry.ToCountry}");
                }
                else
                {
                    pageDescription.Append(
                        $"{eurovisionEntry.ToCountry}");
                }

                if (eurovisionEntry.SfNum.HasValue && !eurovisionEntry.PointsFinal.HasValue)
                {
                    pageDescription.Append(
                        $" — {eurovisionEntry.SfNum}{StringExtensions.GetAmountEnd((int)eurovisionEntry.SfNum.Value)} semi-final - {eurovisionEntry.RunningSf}{StringExtensions.GetAmountEnd((int)eurovisionEntry.RunningSf.GetValueOrDefault())} running");
                }
                else if (!eurovisionEntry.PointsFinal.HasValue && eurovisionEntry.RunningFinal.HasValue)
                {
                    pageDescription.Append($" — Finals");
                    if (eurovisionEntry.RunningFinal.HasValue)
                    {
                        pageDescription.Append($" — {eurovisionEntry.RunningFinal}{StringExtensions.GetAmountEnd((int)eurovisionEntry.RunningFinal.GetValueOrDefault())} running");
                    }
                }
                else if (eurovisionEntry.PointsFinal.HasValue)
                {
                    pageDescription.Append($" — {eurovisionEntry.PointsFinal}");
                }

                pageDescription.AppendLine();
                pageDescription.Append($":flag_{eurovisionEntry.ToCountryId}:  ");
                if (!string.IsNullOrWhiteSpace(eurovisionEntry.YoutubeUrl))
                {
                    pageDescription.Append($"**[{eurovisionEntry.Performer} - {eurovisionEntry.Song}]({eurovisionEntry.YoutubeUrl})**");
                }
                else
                {
                    pageDescription.Append($"**{eurovisionEntry.Performer} - {eurovisionEntry.Song}**");
                }
                pageDescription.AppendLine();
                pageDescription.AppendLine();
            }

            var footer = new StringBuilder();
            footer.Append($"Page {pageCounter}/{eurovisionPages.Count()}");
            footer.Append($" - {contestants.Count} total entries");

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
