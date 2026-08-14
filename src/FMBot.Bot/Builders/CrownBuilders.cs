using FMBot.Bot.Models;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;
using System.Text;
using Guild = FMBot.Persistence.Domain.Models.Guild;
using System.Web;
using System;
using System.Linq;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using System.Collections.Generic;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Factories;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Attributes;
using NetCord;
using NetCord.Rest;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class CrownBuilders
{
    private readonly CrownService _crownService;
    private readonly UserService _userService;
    private readonly ArtistsService _artistsService;
    private readonly GuildService _guildService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly MusicDataFactory _musicDataFactory;
    private readonly WhoKnowsArtistService _whoKnowsArtistService;

    public CrownBuilders(CrownService crownService,
        ArtistsService artistsService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        GuildService guildService,
        MusicDataFactory musicDataFactory,
        WhoKnowsArtistService whoKnowsArtistService)
    {
        this._crownService = crownService;
        this._artistsService = artistsService;
        this._dataSourceFactory = dataSourceFactory;
        this._userService = userService;
        this._guildService = guildService;
        this._musicDataFactory = musicDataFactory;
        this._whoKnowsArtistService = whoKnowsArtistService;
    }

    public async Task<ResponseModel> CrownAsync(
        ContextModel context,
        Guild guild,
        string artistValues,
        UserSettingsModel challengerSettings = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (guild.CrownsDisabled == true)
        {
            response.Text = context.Localize("crown.functionalityDisabled");
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        if (string.IsNullOrWhiteSpace(artistValues))
        {
            var recentTracks =
                await this._dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, sessionKey: context.ContextUser.SessionKeyLastFm, useCache: true);

            if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
            {
                return GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, context.ContextUser.UserNameLastFM, context.Localizer);
            }

            var currentTrack = recentTracks.Content.RecentTracks[0];
            artistValues = currentTrack.ArtistName;
        }

        var artistSearch = await this._artistsService.SearchArtist(response, context.DiscordUser, context.Localizer, artistValues,
            context.ContextUser.UserNameLastFM, context.ContextUser.SessionKeyLastFm,
            userId: context.ContextUser.UserId, interactionId: context.InteractionId,
            referencedMessage: context.ReferencedMessage, discordGuildId: context.DiscordGuild?.Id);
        if (artistSearch.Artist == null)
        {
            return artistSearch.Response;
        }

        var cachedArtist = await this._musicDataFactory.GetOrStoreArtistAsync(artistSearch.Artist, artistSearch.Artist.ArtistName);

        var whoKnowsContext = await this._whoKnowsArtistService.GetFilteredUsersForArtist(context.DiscordGuild,
            context.ContextUser, artistSearch.Artist.ArtistName, artistSearch.Artist.UserPlaycount);

        CrownModel crownModel = null;
        if (whoKnowsContext.FilteredUsersWithArtist.Count >= 1)
        {
            crownModel = await this._crownService.GetAndUpdateCrownForArtist(whoKnowsContext.FilteredUsersWithArtist,
                whoKnowsContext.GuildUsers, whoKnowsContext.Guild, artistSearch.Artist.ArtistName);
        }

        var artistCrowns = await this._crownService.GetCrownsForArtist(guild.GuildId, artistSearch.Artist.ArtistName);

        var userIds = artistCrowns.Select(s => s.UserId).ToHashSet();
        var users = await this._userService.GetMultipleUsers(userIds);

        response.Components = new ActionRowProperties()
            .WithButton("WhoKnows", $"{InteractionConstants.Artist.WhoKnows}:{cachedArtist.Id}", style: ButtonStyle.Secondary, emote: EmojiProperties.Standard("📋"));

        if (!artistCrowns.Any(a => a.Active))
        {
            var minPlaycount = whoKnowsContext.Guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown;

            var noCrownsDescription = new StringBuilder();
            noCrownsDescription.AppendLine(context.Localize("crown.noKnownCrowns",
                ("artist", artistSearch.Artist.ArtistName),
                ("threshold", minPlaycount.Format(context.NumberFormat))));

            var contextUserPlaycount = artistSearch.Artist.UserPlaycount ?? 0;
            if (contextUserPlaycount > 0 && contextUserPlaycount < minPlaycount)
            {
                noCrownsDescription.AppendLine(context.Localize("crown.needMorePlays",
                    ("plays", context.LocalizeCount("shared.plays", minPlaycount - contextUserPlaycount))));
            }

            response.Embed.WithDescription(noCrownsDescription.ToString());
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var currentCrown = artistCrowns
            .Where(w => w.Active)
            .OrderByDescending(o => o.CurrentPlaycount)
            .First();

        response.Embed.WithDescription(CrownDuelToString(context, whoKnowsContext, currentCrown,
            challengerSettings?.UserId ?? context.ContextUser.UserId, crownModel, artistSearch.Artist.ArtistName));

        if (currentCrown.StartPlaycount != currentCrown.CurrentPlaycount ||
            currentCrown.Created.Date < DateTime.UtcNow.Date)
        {
            var userArtistUrl =
                $"{LastfmUrlExtensions.GetUserUrl(users[currentCrown.UserId].UserNameLastFM)}/library/music/{HttpUtility.UrlEncode(artistSearch.Artist.ArtistName)}";

            whoKnowsContext.GuildUsers.TryGetValue(currentCrown.UserId, out var currentGuildUser);
            response.Embed.AddField(context.Localize("crown.fieldCurrentHolder"), CrownToString(currentGuildUser, users[currentCrown.UserId], currentCrown, context, userArtistUrl));
        }

        if (artistCrowns.Count > 1)
        {
            var crownHistory = new StringBuilder();

            foreach (var artistCrown in artistCrowns
                         .OrderByDescending(o => o.Modified)
                         .Take(10)
                         .Where(w => !w.Active))
            {
                whoKnowsContext.GuildUsers.TryGetValue(artistCrown.UserId, out var guildUser);

                crownHistory.AppendLine(CrownToString(guildUser, users[artistCrown.UserId], artistCrown, context));
            }

            if (artistCrowns.Count(w => !w.Active) > 10)
            {
                crownHistory.AppendLine(context.LocalizeCount("crown.moreStealsHidden", artistCrowns.Count(w => !w.Active) - 10));
            }

            response.Embed.AddField(context.Localize("shared.crownHistory"), crownHistory.ToString());

            if (artistCrowns.Count(w => !w.Active) > 10)
            {
                var firstCrown = artistCrowns.OrderBy(o => o.Created).First();

                whoKnowsContext.GuildUsers.TryGetValue(firstCrown.UserId, out var firstGuildUser);

                response.Embed.AddField(context.Localize("crown.fieldFirstHolder"), CrownToString(firstGuildUser, users[firstCrown.UserId], firstCrown, context));
            }
        }

        response.Embed.WithTitle(context.Localize("crown.titleFor", ("artist", currentCrown.ArtistName)));

        return response;
    }

    private static string CrownDuelToString(ContextModel context, WhoKnowsArtistContext whoKnowsContext,
        UserCrown currentCrown, int challengerUserId, CrownModel crownModel, string artistName)
    {
        var holder = GetListener(whoKnowsContext, currentCrown.UserId);
        if (holder == null)
        {
            return context.Localize("crown.holderLeftServer");
        }

        var holderName = DisplayName(holder);
        var holderPlaycount = currentCrown.CurrentPlaycount;
        var artistUrl = LastfmUrlExtensions.GetArtistUrl(artistName);
        var holderLink = WhoKnowsService.NameWithLink(holder);

        if (challengerUserId != context.ContextUser.UserId &&
            GetListener(whoKnowsContext, challengerUserId) == null)
        {
            challengerUserId = context.ContextUser.UserId;
        }

        WhoKnowsObjectWithUser opponent;
        var previousHolderLeft = false;
        var opponentIsRunnerUp = false;

        if (crownModel?.Stolen == true && crownModel.PreviousCrown != null)
        {
            opponent = GetListener(whoKnowsContext, crownModel.PreviousCrown.UserId);
            previousHolderLeft = opponent == null;
        }
        else if (challengerUserId == currentCrown.UserId)
        {
            opponent = whoKnowsContext.FilteredUsersWithArtist
                .Where(w => w.UserId != currentCrown.UserId)
                .MaxBy(o => o.Playcount);
            opponentIsRunnerUp = true;
        }
        else
        {
            opponent = GetListener(whoKnowsContext, challengerUserId);
        }

        var opponentPlaycount = opponent?.Playcount ?? 0;

        var description = new StringBuilder();
        description.AppendLine(context.LocalizeCount("crown.rowHolder", holderPlaycount, ("user", holderLink)));

        if (crownModel?.Claimed == true)
        {
            description.AppendLine();
            description.AppendLine(context.Localize("crown.verdictClaimed",
                ("winner", holderName),
                ("artist", artistName),
                ("url", artistUrl),
                ("plays", context.LocalizeCount("shared.plays", holderPlaycount))));

            return description.ToString();
        }

        if (opponent == null)
        {
            description.AppendLine();

            if (previousHolderLeft)
            {
                description.AppendLine(context.Localize("crown.verdictClaimed",
                    ("winner", holderName),
                    ("artist", artistName),
                    ("url", artistUrl),
                    ("plays", context.LocalizeCount("shared.plays", holderPlaycount))));
                description.AppendLine(context.Localize("crown.previousHolderLeft"));
            }
            else
            {
                description.AppendLine(context.Localize("crown.verdictUncontested",
                    ("winner", holderName),
                    ("artist", artistName),
                    ("url", artistUrl)));
            }

            return description.ToString();
        }

        if (opponentIsRunnerUp && opponentPlaycount * 2 < holderPlaycount)
        {
            description.AppendLine();
            description.AppendLine(context.Localize("crown.verdictSafe",
                ("winner", holderName),
                ("artist", artistName),
                ("url", artistUrl),
                ("plays", context.LocalizeCount("shared.plays", holderPlaycount))));

            return description.ToString();
        }

        var opponentName = DisplayName(opponent);

        description.AppendLine(context.LocalizeCount("crown.rowChallenger", opponentPlaycount,
            ("user", WhoKnowsService.NameWithLink(opponent))));
        description.AppendLine();

        if (crownModel?.Stolen == true)
        {
            description.AppendLine(context.Localize("crown.verdictStolen",
                ("winner", holderName),
                ("loser", opponentName),
                ("artist", artistName),
                ("url", artistUrl),
                ("plays", context.LocalizeCount("shared.plays", holderPlaycount))));
        }
        else if (opponentPlaycount > holderPlaycount)
        {
            description.AppendLine(context.Localize("crown.verdictNoSteal", ("loser", opponentName)));
        }
        else if (opponentPlaycount == holderPlaycount)
        {
            description.AppendLine(context.Localize("crown.verdictTied",
                ("winner", holderName),
                ("loser", opponentName),
                ("artist", artistName),
                ("url", artistUrl),
                ("plays", context.LocalizeCount("shared.plays", holderPlaycount))));
        }
        else
        {
            description.AppendLine(context.Localize("crown.verdictKeeps",
                ("winner", holderName),
                ("loser", opponentName),
                ("artist", artistName),
                ("url", artistUrl),
                ("plays", context.LocalizeCount("shared.plays", holderPlaycount - opponentPlaycount))));
        }

        if (PublicProperties.IssuesAtLastFm && crownModel?.Stolen != true)
        {
            description.AppendLine();
            description.AppendLine(context.Localize("crown.stealDisabledLastFm"));
        }

        return description.ToString();
    }

    private static string DisplayName(WhoKnowsObjectWithUser user)
    {
        return StringExtensions.Sanitize(string.IsNullOrWhiteSpace(user.DiscordName)
            ? user.LastFMUsername
            : user.DiscordName);
    }

    private static WhoKnowsObjectWithUser GetListener(WhoKnowsArtistContext whoKnowsContext, int userId)
    {
        var listener = whoKnowsContext.FilteredUsersWithArtist.FirstOrDefault(f => f.UserId == userId);
        if (listener != null)
        {
            return listener;
        }

        if (!whoKnowsContext.GuildUsers.TryGetValue(userId, out var guildUser))
        {
            return null;
        }

        return new WhoKnowsObjectWithUser
        {
            UserId = guildUser.UserId,
            DiscordName = guildUser.UserName,
            LastFMUsername = guildUser.UserNameLastFM,
            Playcount = 0
        };
    }

    private static string CrownToString(FullGuildUser guildUser, User user, UserCrown crown, ContextModel context, string url = null)
    {
        var userDisplay = url != null
            ? $"**{StringExtensions.MarkdownLink(guildUser?.UserName ?? user.UserNameLastFM, url)}**"
            : $"**{guildUser?.UserName ?? user.UserNameLastFM}**";

        return context.LocalizeCount("crown.historyEntry", crown.CurrentPlaycount,
            ("from", $"<t:{((DateTimeOffset)crown.Created).ToUnixTimeSeconds()}:D>"),
            ("to", $"<t:{((DateTimeOffset)crown.Modified).ToUnixTimeSeconds()}:D>"),
            ("user", userDisplay),
            ("start", crown.StartPlaycount.Format(context.NumberFormat)));
    }

    public async Task<ResponseModel> CrownOverviewAsync(
        ContextModel context,
        Guild guild,
        UserSettingsModel userSettings,
        CrownViewType crownViewType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        if (guild.CrownsDisabled == true)
        {
            response.Text = context.Localize("crown.functionalityDisabled");
            response.ResponseType = ResponseType.Text;
            response.CommandResponse = CommandResponse.Disabled;
            return response;
        }

        var userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        var userCrowns = await this._crownService.GetCrownsForUser(guild, userSettings.UserId, crownViewType);

        string title;
        if (crownViewType == CrownViewType.Stolen)
        {
            title = userSettings.DifferentUser
                ? context.Localize("crown.stolenTitleOther", ("user", userSettings.UserNameLastFm), ("requester", userTitle))
                : context.Localize("crown.stolenTitleSelf", ("user", userTitle));
        }
        else
        {
            title = userSettings.DifferentUser
                ? context.Localize("crown.overviewTitleOther", ("user", userSettings.UserNameLastFm), ("requester", userTitle))
                : context.Localize("crown.overviewTitleSelf", ("user", userTitle));
        }

        var noResults = crownViewType == CrownViewType.Stolen
            ? context.Localize("crown.noStolenCrowns")
            : context.Localize("crown.noCrownsYet",
                ("command", $"{context.Prefix}whoknows"),
                ("threshold", (guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown).Format(context.NumberFormat)));

        var viewType =  new StringMenuProperties(InteractionConstants.User.CrownSelectMenu)
            .WithPlaceholder(context.Localize("crown.selectViewPlaceholder"))
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in ((CrownViewType[])Enum.GetValues(typeof(CrownViewType))))
        {
            var name = context.LocalizeOption(option);
            var value = $"{userSettings.DiscordUserId}-{context.ContextUser.DiscordUserId}-{Enum.GetName(option)}";

            var active = option == crownViewType;

            viewType.AddOptions(new StringMenuSelectOptionProperties(name, value)
            {
                Default = active
            });
        }

        var pageDescriptions = new List<string>();

        var crownPages = userCrowns.ChunkBy(10);

        var counter = 1;
        foreach (var crownPage in crownPages)
        {
            var crownPageString = new StringBuilder();
            foreach (var userCrown in crownPage)
            {
                crownPageString.Append(crownViewType != CrownViewType.Stolen
                    ? context.LocalizeCount("crown.entryClaimed", userCrown.CurrentPlaycount,
                        ("rank", counter.ToString()),
                        ("artist", userCrown.ArtistName),
                        ("timestamp", $"<t:{((DateTimeOffset)userCrown.Created).ToUnixTimeSeconds()}:R>"))
                    : context.LocalizeCount("crown.entryStolen", userCrown.CurrentPlaycount,
                        ("rank", counter.ToString()),
                        ("artist", userCrown.ArtistName),
                        ("timestamp", $"<t:{((DateTimeOffset)userCrown.Modified).ToUnixTimeSeconds()}:R>")));

                crownPageString.AppendLine();

                counter++;
            }

            pageDescriptions.Add(crownPageString.ToString());
        }

        if (!userCrowns.Any())
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

            container.WithAccentColor(DiscordConstants.LastFmColorRed);
            container.WithTextDisplay($"### {title}");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
                container.WithSeparator();
                container.WithTextDisplay(
                    $"-# {context.LocalizeCount("crown.pageCounterTotal", userCrowns.Count, ("page", (p.CurrentPageIndex + 1).ToString()), ("pages", pageDescriptions.Count.ToString()))}");
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
