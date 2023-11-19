using System;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using System.Threading.Tasks;
using FMBot.Bot.Services.Guild;
using Fergun.Interactive;
using System.Collections.Generic;
using FMBot.Bot.Services.WhoKnows;
using System.Linq;
using FMBot.Bot.Extensions;
using System.Text;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Extensions;

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
            response.Text = "Crown functionality has been disabled in this server.";
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var pages = new List<PageBuilder>();
        var counter = 1;
        var pageCounter = 1;

        string noResults;

        switch (guildViewType)
        {
            case GuildViewType.Overview:
                {
                    noResults = $"No .fmbot users in this server. \n\n" +
                                $"Use `{context.Prefix}login` to get started, or try `{context.Prefix}refreshmembers` to refresh the cached memberlist.";

                    var (filterStats, filteredGuildUsers) = WhoKnowsService.FilterGuildUsers(guildUsers, guild);

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
                            $"Page {pageCounter}/{userPages.Count} - {guildUsers.Count} total .fmbot users in this server");

                        if (filterStats.FullDescription != null)
                        {
                            footer.AppendLine(filterStats.FullDescription);
                        }

                        pages.Add(new PageBuilder()
                            .WithDescription(crownPageString.ToString())
                            .WithTitle($".fmbot users in {context.DiscordGuild.Name}")
                            .WithFooter(footer.ToString()));
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.Crowns:
                {
                    var topCrownUsers = await this._crownService.GetTopCrownUsersForGuild(guild.GuildId);
                    var guildCrownCount = await this._crownService.GetTotalCrownCountForGuild(guild.GuildId);

                    noResults = $"No top crown users in this server. Use `{context.Prefix}whoknows` to start getting crowns!";

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

                            crownPageString.Append($"{counter}. **{name ?? crownUser.First().User.UserNameLastFM}**");
                            crownPageString.Append($" - *{crownUser.Count()} {StringExtensions.GetCrownsString(crownUser.Count())}*");
                            crownPageString.AppendLine();
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (location >= 0)
                        {
                            footer.AppendLine($"Your ranking: #{location + 1}");
                        }

                        footer.AppendLine(
                            $"Page {pageCounter}/{crownPages.Count} - {guildCrownCount} total active crowns in this server");

                        pages.Add(new PageBuilder()
                            .WithDescription(crownPageString.ToString())
                            .WithTitle($"Users with most crowns in {context.DiscordGuild.Name}")
                            .WithFooter(footer.ToString()));
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.ListeningTime:
                {
                    noResults = $"No .fmbot users in this server, or nobody listened to music recently. \n\n" +
                                $"Maybe try `{context.Prefix}refreshmembers` to refresh the cached memberlist.";

                    var userPlays = await this._playService.GetGuildUsersPlaysForTimeLeaderBoard(guild.GuildId);

                    var userListeningTime =
                        await this._timeService.UserPlaysToGuildLeaderboard(context.DiscordGuild, userPlays, guildUsers);

                    var (filterStats, filteredTopListeningTimeUsers) = WhoKnowsService.FilterWhoKnowsObjects(userListeningTime, guild);

                    var ltPages = filteredTopListeningTimeUsers.Chunk(10).ToList();

                    var requestedUser = filteredTopListeningTimeUsers.FirstOrDefault(f => f.UserId == context.ContextUser.UserId);
                    var location = filteredTopListeningTimeUsers.IndexOf(requestedUser);

                    foreach (var ltPage in ltPages)
                    {
                        var ltPageString = new StringBuilder();
                        foreach (var user in ltPage)
                        {
                            ltPageString.AppendLine($"{counter}. **{WhoKnowsService.NameWithLink(user)}** - *{user.Name}*");
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (requestedUser != null && location >= 0)
                        {
                            footer.AppendLine($"Your ranking: #{location + 1} ({requestedUser.DiscordName})");
                        }

                        footer.AppendLine(
                            $"7 days - From {DateTime.UtcNow.AddDays(-9):MMM dd} to {DateTime.UtcNow.AddDays(-2):MMM dd}");

                        footer.Append($"Page {pageCounter}/{ltPages.Count} - ");
                        footer.Append($"{filteredTopListeningTimeUsers.Count} users - ");
                        footer.Append($"{filteredTopListeningTimeUsers.Sum(s => s.Playcount)} total minutes");

                        if (filterStats.FullDescription != null)
                        {
                            footer.AppendLine();
                            footer.AppendLine(filterStats.FullDescription);
                        }

                        pages.Add(new PageBuilder()
                            .WithDescription(ltPageString.ToString())
                            .WithTitle($"Users with most listening time in {context.DiscordGuild.Name}")
                            .WithFooter(footer.ToString()));
                        pageCounter++;
                    }
                }
                break;
            case GuildViewType.Plays:
                {
                    noResults = $"No .fmbot users in this server that we have a total playcount for. \n\n" +
                                $"Maybe try `{context.Prefix}refreshmembers` to refresh the cached memberlist.";

                    var topPlaycountUsers = await this._playService.GetGuildUsersTotalPlaycount(context.DiscordGuild, guildUsers, guild.GuildId);

                    var (filterStats, filteredPlaycountUsers) = WhoKnowsService.FilterWhoKnowsObjects(topPlaycountUsers, guild);

                    var playcountPages = filteredPlaycountUsers.Chunk(10).ToList();

                    var requestedUser = filteredPlaycountUsers.FirstOrDefault(f => f.UserId == context.ContextUser.UserId);
                    var location = filteredPlaycountUsers.IndexOf(requestedUser);

                    foreach (var playcountPage in playcountPages)
                    {
                        var playcountPageString = new StringBuilder();
                        foreach (var user in playcountPage)
                        {
                            playcountPageString.AppendLine($"{counter}. **{WhoKnowsService.NameWithLink(user)}** - *{user.Playcount} {StringExtensions.GetPlaysString(user.Playcount)}*");
                            counter++;
                        }

                        var footer = new StringBuilder();
                        if (requestedUser != null && location >= 0)
                        {
                            footer.AppendLine($"Your ranking: #{location + 1} ({requestedUser.DiscordName})");
                        }

                        footer.Append($"Page {pageCounter}/{playcountPages.Count} - ");
                        footer.Append($"{filteredPlaycountUsers.Count} users - ");
                        footer.Append($"{filteredPlaycountUsers.Sum(s => s.Playcount)} total plays");

                        if (filterStats.FullDescription != null)
                        {
                            footer.AppendLine();
                            footer.AppendLine(filterStats.FullDescription);
                        }

                        pages.Add(new PageBuilder()
                            .WithDescription(playcountPageString.ToString())
                            .WithTitle($"Users with highest total playcount in {context.DiscordGuild.Name}")
                            .WithFooter(footer.ToString()));
                        pageCounter++;
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(guildViewType), guildViewType, null);
        }


        var fmType = new SelectMenuBuilder()
            .WithPlaceholder("Select member view")
            .WithCustomId(InteractionConstants.GuildMembers)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((GuildViewType[])Enum.GetValues(typeof(GuildViewType))))
        {
            var name = option.GetAttribute<ChoiceDisplayAttribute>().Name;
            var value = Enum.GetName(option);

            var active = option == guildViewType;

            fmType.AddOption(new SelectMenuOptionBuilder(name, value, null, isDefault: active));
        }

        if (!pages.Any())
        {
            response.Embed.WithDescription(noResults);
            response.ResponseType = ResponseType.Embed;
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        response.StaticPaginator = StringService.BuildStaticPaginatorWithSelectMenu(pages, fmType);

        return response;
    }
}
