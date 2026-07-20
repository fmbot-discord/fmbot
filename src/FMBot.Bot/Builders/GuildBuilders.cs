using System;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Services.Guild;
using System.Collections.Generic;
using FMBot.Bot.Services.WhoKnows;
using System.Linq;
using FMBot.Bot.Extensions;
using System.Text;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Extensions;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Builders;

public class GuildBuilders
{
    private readonly GuildService _guildService;
    private readonly CrownService _crownService;
    private readonly PlayService _playService;
    private readonly TimeService _timeService;

    public GuildBuilders(GuildService guildService, CrownService crownService, PlayService playService, TimeService timeService)
    {
        this._guildService = guildService;
        this._crownService = crownService;
        this._playService = playService;
        this._timeService = timeService;
    }

    public async Task<ResponseModel> MemberOverviewAsync(
        ContextModel context,
        Guild guild,
        GuildViewType guildViewType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        if (guild.CrownsDisabled == true && guildViewType == GuildViewType.Crowns)
        {
            response.Text = context.Localize("crown.functionalityDisabled");
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var pageDescriptions = new List<string>();
        var pageFooters = new List<string>();
        var counter = 1;
        var pageCounter = 1;

        string title;
        string noResults;

        switch (guildViewType)
        {
            case GuildViewType.Overview:
                {
                    title = context.Localize("members.overviewTitle", ("server", context.DiscordGuild.Name));
                    noResults = context.Localize("members.overviewNoResults",
                        ("loginCommand", $"{context.Prefix}login"),
                        ("refreshCommand", $"{context.Prefix}refreshmembers"));

                    var (filterStats, filteredGuildUsers) = WhoKnowsService.FilterGuildUsers(guildUsers, guild, context.ContextUser.UserId);
                    var filterDescription = filterStats.GetFullDescription(context.Localizer);

                    var userPages = filteredGuildUsers
                        .OrderByDescending(o => o.Value.LastUsed)
                        .Chunk(10)
                        .ToList();

                    foreach (var userPage in userPages)
                    {
                        var crownPageString = new StringBuilder();
                        foreach (var guildUser in userPage)
                        {
                            crownPageString.Append($"{counter}. **[{StringExtensions.Sanitize(guildUser.Value.UserName) ?? guildUser.Value.UserNameLastFM}]");
                            crownPageString.Append($"({LastfmUrlExtensions.GetUserUrl(guildUser.Value.UserNameLastFM)})**");
                            crownPageString.AppendLine();
                            counter++;
                        }

                        var footer = new StringBuilder();
                        footer.AppendLine(
                            $"-# {context.LocalizeCount("members.overviewPageCounter", guildUsers.Count, ("page", pageCounter.ToString()), ("pages", userPages.Count.ToString()))}");

                        if (filterDescription != null)
                        {
                            footer.AppendLine($"-# {filterDescription}");
                        }

                        pageDescriptions.Add(crownPageString.ToString());
                        pageFooters.Add(footer.ToString());
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.Crowns:
                {
                    var topCrownUsers = await this._crownService.GetTopCrownUsersForGuild(guild.GuildId);
                    var guildCrownCount = await this._crownService.GetTotalCrownCountForGuild(guild.GuildId);

                    title = context.Localize("members.crownsTitle", ("server", context.DiscordGuild.Name));
                    noResults = context.Localize("members.crownsNoResults", ("command", $"{context.Prefix}whoknows"));

                    var crownPages = topCrownUsers.Chunk(10).ToList();

                    var requestedUser = topCrownUsers.FirstOrDefault(f => f.Key == context.ContextUser.UserId);
                    var location = topCrownUsers.IndexOf(requestedUser);

                    foreach (var crownPage in crownPages)
                    {
                        var crownPageString = new StringBuilder();
                        foreach (var crownUser in crownPage)
                        {
                            guildUsers.TryGetValue(crownUser.Key, out var guildUser);

                            string name = null;
                            if (guildUser != null)
                            {
                                name = guildUser.UserName;
                            }

                            crownPageString.AppendLine(context.LocalizeCount("members.crownsEntry", crownUser.Count(),
                                ("rank", counter.ToString()),
                                ("user", name ?? crownUser.First().User.UserNameLastFM)));
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (location >= 0)
                        {
                            footer.AppendLine($"-# {context.Localize("members.yourRanking", ("rank", (location + 1).ToString()))}");
                        }

                        footer.AppendLine(
                            $"-# {context.LocalizeCount("members.crownsPageCounter", guildCrownCount, ("page", pageCounter.ToString()), ("pages", crownPages.Count.ToString()))}");

                        pageDescriptions.Add(crownPageString.ToString());
                        pageFooters.Add(footer.ToString());
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.ListeningTime:
                {
                    title = context.Localize("members.listeningTimeTitle", ("server", context.DiscordGuild.Name));
                    noResults = context.Localize("members.listeningTimeNoResults", ("command", $"{context.Prefix}refreshmembers"));

                    var userPlays = await this._playService.GetGuildUsersPlaysForTimeLeaderBoard(guild.GuildId);

                    var userListeningTime =
                        await this._timeService.UserPlaysToGuildLeaderboard(context.DiscordGuild, userPlays, guildUsers);

                    var (filterStats, filteredTopListeningTimeUsers) = WhoKnowsService.FilterWhoKnowsObjects(userListeningTime, guildUsers,guild, context.ContextUser.UserId);
                    var filterDescription = filterStats.GetFullDescription(context.Localizer);

                    var ltPages = filteredTopListeningTimeUsers.Chunk(10).ToList();

                    var requestedUser = filteredTopListeningTimeUsers.FirstOrDefault(f => f.UserId == context.ContextUser.UserId);
                    var location = filteredTopListeningTimeUsers.IndexOf(requestedUser);

                    foreach (var ltPage in ltPages)
                    {
                        var ltPageString = new StringBuilder();
                        foreach (var user in ltPage)
                        {
                            ltPageString.AppendLine(context.Localize("members.listeningTimeEntry",
                                ("rank", counter.ToString()),
                                ("user", WhoKnowsService.NameWithLink(user, true)),
                                ("time", user.Name)));
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (requestedUser != null && location >= 0)
                        {
                            footer.AppendLine($"-# {context.Localize("members.yourRankingUser", ("rank", (location + 1).ToString()), ("user", requestedUser.DiscordName))}");
                        }

                        footer.AppendLine(
                            $"-# {context.Localize("members.listeningTimeRange", ("from", $"{DateTime.UtcNow.AddDays(-9):MMM dd}"), ("to", $"{DateTime.UtcNow.AddDays(-2):MMM dd}"))}");

                        footer.Append($"-# {context.Localize("shared.pageCounter", ("page", pageCounter.ToString()), ("pages", ltPages.Count.ToString()))} - ");
                        footer.Append($"{context.LocalizeCount("shared.users", filteredTopListeningTimeUsers.Count)} - ");
                        footer.Append(context.LocalizeCount("members.totalMinutes", filteredTopListeningTimeUsers.Sum(s => s.Playcount)));

                        if (filterDescription != null)
                        {
                            footer.AppendLine();
                            footer.Append($"-# {filterDescription}");
                        }

                        pageDescriptions.Add(ltPageString.ToString());
                        pageFooters.Add(footer.ToString());
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.Plays:
                {
                    title = context.Localize("members.playsTitle", ("server", context.DiscordGuild.Name));
                    noResults = context.Localize("members.playsNoResults", ("command", $"{context.Prefix}refreshmembers"));

                    var topPlaycountUsers = await this._playService.GetGuildUsersTotalPlaycount(context.DiscordGuild, guildUsers, guild.GuildId);

                    var (filterStats, filteredPlaycountUsers) = WhoKnowsService.FilterWhoKnowsObjects(topPlaycountUsers, guildUsers,guild, context.ContextUser.UserId);
                    var filterDescription = filterStats.GetFullDescription(context.Localizer);

                    var playcountPages = filteredPlaycountUsers.Chunk(10).ToList();

                    var requestedUser = filteredPlaycountUsers.FirstOrDefault(f => f.UserId == context.ContextUser.UserId);
                    var location = filteredPlaycountUsers.IndexOf(requestedUser);

                    foreach (var playcountPage in playcountPages)
                    {
                        var playcountPageString = new StringBuilder();
                        foreach (var user in playcountPage)
                        {
                            playcountPageString.AppendLine(context.LocalizeCount("members.playsEntry", user.Playcount,
                                ("rank", counter.ToString()),
                                ("user", WhoKnowsService.NameWithLink(user, true))));
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (requestedUser != null && location >= 0)
                        {
                            footer.AppendLine($"-# {context.Localize("members.yourRankingUser", ("rank", (location + 1).ToString()), ("user", requestedUser.DiscordName))}");
                        }

                        footer.Append($"-# {context.Localize("shared.pageCounter", ("page", pageCounter.ToString()), ("pages", playcountPages.Count.ToString()))} - ");
                        footer.Append($"{context.LocalizeCount("shared.users", filteredPlaycountUsers.Count)} - ");
                        footer.Append(context.LocalizeCount("members.totalPlays", filteredPlaycountUsers.Sum(s => s.Playcount)));

                        if (filterDescription != null)
                        {
                            footer.AppendLine();
                            footer.Append($"-# {filterDescription}");
                        }

                        pageDescriptions.Add(playcountPageString.ToString());
                        pageFooters.Add(footer.ToString());
                        pageCounter++;
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(guildViewType), guildViewType, null);
        }

        var viewType = new StringMenuProperties(InteractionConstants.GuildMembers)
            .WithPlaceholder(context.Localize("members.selectViewPlaceholder"))
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((GuildViewType[])Enum.GetValues(typeof(GuildViewType))))
        {
            var name = context.LocalizeOption(option);
            var value = Enum.GetName(option);

            var active = option == guildViewType;

            viewType.AddOption(name, value, isDefault: active);
        }

        if (!pageDescriptions.Any())
        {
            response.CommandResponse = CommandResponse.NotFound;
        }

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;

        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());

                var pageFooter = pageFooters.ElementAtOrDefault(p.CurrentPageIndex);
                if (pageFooter != null)
                {
                    container.WithSeparator();
                    container.WithTextDisplay(pageFooter.TrimEnd());
                }
            }
            else
            {
                container.WithTextDisplay(noResults);
            }

            container.AddComponents(viewType);

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2)
                .WithComponents([container])
                .Build();
        }
    }
}
