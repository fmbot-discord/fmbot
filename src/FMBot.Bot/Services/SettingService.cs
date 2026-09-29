using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;
using NetCord.Services.Commands;
using NetCord.Gateway;
using DiscordGuild = NetCord.Gateway.Guild;

namespace FMBot.Bot.Services;

public class SettingService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public SettingService(IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._contextFactory = contextFactory;
    }

    public static TimeZoneInfo ResolveTimeZone(string timeZone, TimeZoneInfo fallback = null) =>
        Core.TimePeriodParser.ResolveTimeZone(timeZone, fallback);

    public static TimeSettingsModel GetTimePeriod(string options,
        TimePeriod defaultTimePeriod = TimePeriod.Weekly,
        DateTime? registeredLastFm = null,
        bool cachedOnly = false,
        bool dailyTimePeriods = true,
        string timeZone = null,
        Language language = Language.English) =>
        Core.TimePeriodParser.GetTimePeriod(options, defaultTimePeriod, registeredLastFm, cachedOnly, dailyTimePeriods,
            timeZone, language);

    public static (Language? Language, string NewSearchValue) GetLanguage(string extraOptions)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return (null, extraOptions);
        }

        foreach (var word in extraOptions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var language = LanguageExtensions.FromUserInput(word);
            if (language.HasValue)
            {
                return (language, ContainsAndRemove(extraOptions, [word], true));
            }
        }

        return (null, extraOptions);
    }

    public static DiscogsCollectionSettings SetDiscogsCollectionSettings(string extraOptions = null)
    {
        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = new List<DiscogsFormat>(),
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return collectionSettings;
        }

        var searchTerms = collectionSettings.NewSearchValue.Split(' ');

        foreach (var word in searchTerms)
        {
            var discogsFormat = DiscogsCollectionSettings.ToDiscogsFormat(word);
            if (discogsFormat.value != null)
            {
                collectionSettings.NewSearchValue = ContainsAndRemove(collectionSettings.NewSearchValue, new[] { word });
                collectionSettings.Formats.Add(discogsFormat.format);
            }
        }

        var miscFormats = new[] { "misc", "miscellaneous" };
        if (Contains(extraOptions, miscFormats))
        {
            collectionSettings.NewSearchValue = ContainsAndRemove(collectionSettings.NewSearchValue, miscFormats);
            collectionSettings.Formats.Add(DiscogsFormat.Miscellaneous);
        }

        collectionSettings.Formats = collectionSettings.Formats.Distinct().ToList();

        return collectionSettings;
    }

    public static TopListSettings SetTopListSettings(string extraOptions = null)
    {
        var topListSettings = new TopListSettings
        {
            Billboard = false,
            EmbedSize = EmbedSize.Default,
            Discogs = false,
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return topListSettings;
        }

        var billboard = new[] { "bb", "billboard", "compare" };
        if (Contains(extraOptions, billboard))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, billboard);
            topListSettings.Billboard = true;
        }

        var extraLarge = new[] { "xl", "xxl", "extralarge" };
        var extraSmall = new[] { "xs", "xxs", "extrasmall" };
        if (Contains(extraOptions, extraLarge))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, extraLarge);
            topListSettings.EmbedSize = EmbedSize.Large;
            topListSettings.ListAmount = 1000;
        }
        else if (Contains(extraOptions, extraSmall))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, extraSmall);
            topListSettings.EmbedSize = EmbedSize.Small;
        }

        var discogs = new[] { "dc", "discogs" };
        if (Contains(extraOptions, discogs))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, discogs);
            topListSettings.Discogs = true;
        }

        var timeListened = new[] { "tl", "timelistened" };
        if (Contains(extraOptions, timeListened))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, timeListened);
            topListSettings.Type = TopListType.TimeListened;
        }

        var noSingles = new[] { "ns", "nosingles", "hidesingles", "filtersingles" };
        if (Contains(extraOptions, noSingles))
        {
            topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue, noSingles);
            topListSettings.FilterSingles = true;
        }

        foreach (var option in extraOptions.Split(" "))
        {
            if (option.StartsWith("r:", StringComparison.OrdinalIgnoreCase) ||
                option.StartsWith("released:", StringComparison.OrdinalIgnoreCase))
            {
                var yearString = option
                    .Replace("r:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("released:", "", StringComparison.OrdinalIgnoreCase);

                if (int.TryParse(yearString, out var year) && year <= DateTime.UtcNow.Year && year >= 1900)
                {
                    topListSettings.ReleaseYearFilter = year;
                    topListSettings.NewSearchValue =
                        ContainsAndRemove(topListSettings.NewSearchValue, [$"r:{year}", $"released:{year}", option]);
                }
            }

            if (option.StartsWith("d:", StringComparison.OrdinalIgnoreCase) ||
                option.StartsWith("decade:", StringComparison.OrdinalIgnoreCase))
            {
                var yearString = option
                    .Replace("d:", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("decade:", "", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd('s')
                    .TrimEnd('S');

                if (int.TryParse(yearString, out var year))
                {
                    if (year < 100)
                    {
                        year += year < 30 ? 2000 : 1900;
                    }

                    year = (year / 10) * 10;

                    if (year <= DateTime.UtcNow.Year && year >= 1900)
                    {
                        topListSettings.ReleaseDecadeFilter = year;
                        topListSettings.NewSearchValue = ContainsAndRemove(topListSettings.NewSearchValue,
                            [$"d:{year}", $"decade:{year}", $"d:{year}s", $"decade:{year}s", option]);
                    }
                }
            }
        }

        return topListSettings;
    }

    public static (ResponseMode mode, string newSearchValue) SetMode(string extraOptions, ResponseMode? userMode)
    {
        var newSearchValue = extraOptions;

        var image = new[] { "img", "image" };
        if (Contains(extraOptions, image))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, image);
            userMode = ResponseMode.Image;
        }

        var embed = new[] { "embed", "text", "txt" };
        if (Contains(extraOptions, embed))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embed);
            userMode = ResponseMode.Embed;
        }

        userMode ??= ResponseMode.Embed;

        return (userMode.Value, newSearchValue);
    }

    public static CountryChartTheme GetWorldMapTheme(string extraOptions)
    {
        if (Contains(extraOptions, ["light"]))
        {
            return CountryChartTheme.Light;
        }

        if (Contains(extraOptions, ["ocean", "atlas"]))
        {
            return CountryChartTheme.Ocean;
        }

        if (Contains(extraOptions, ["synthwave", "synth", "neon"]))
        {
            return CountryChartTheme.Synthwave;
        }

        return CountryChartTheme.Dark;
    }

    private static (WhoKnowsResponseMode mode, string newSearchValue) SetWhoKnowsMode(string extraOptions, WhoKnowsResponseMode? userMode, bool supportImageMode)
    {
        var newSearchValue = extraOptions;

        var image = new[] { "img", "image" };
        if (Contains(extraOptions, image) && supportImageMode)
        {
            newSearchValue = ContainsAndRemove(newSearchValue, image);
            userMode = WhoKnowsResponseMode.Image;
        }

        var embed = new[] { "embed", "text", "txt" };
        if (Contains(extraOptions, embed))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embed);
            userMode = WhoKnowsResponseMode.Default;
        }

        var pages = new[] { "pp", "page", "pages", "pagination" };
        if (Contains(extraOptions, pages))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, pages);
            userMode = WhoKnowsResponseMode.Pagination;
        }

        userMode ??= WhoKnowsResponseMode.Default;

        return (userMode.Value, newSearchValue);
    }

    public static WhoKnowsSettings SetWhoKnowsSettings(WhoKnowsSettings currentWhoKnowsSettings, string extraOptions,
        UserType userType = UserType.User, bool globalWhoKnows = false, bool supportImageMode = true)
    {
        var whoKnowsSettings = currentWhoKnowsSettings;

        if (extraOptions == null)
        {
            return whoKnowsSettings;
        }

        var mode = SetWhoKnowsMode(extraOptions, currentWhoKnowsSettings.ResponseMode, supportImageMode);

        whoKnowsSettings.ResponseMode = mode.mode;
        whoKnowsSettings.NewSearchValue = mode.newSearchValue;

        if (globalWhoKnows)
        {
            var hidePrivateUsers = new[] { "hp", "hideprivate", "hideprivateusers" };
            if (Contains(extraOptions, hidePrivateUsers))
            {
                whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, hidePrivateUsers);
                whoKnowsSettings.HidePrivateUsers = true;
            }
        }

        var adminView = new[] { "av", "adminview" };
        if (Contains(extraOptions, adminView) && userType is UserType.Admin or UserType.Owner)
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, adminView);
            whoKnowsSettings.AdminView = true;
        }

        var roleFilter = new[] { "rf", "rolefilter", "rolepicker", "roleselector" };
        if (Contains(extraOptions, roleFilter))
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, roleFilter);
            whoKnowsSettings.DisplayRoleFilter = true;
        }

        var qualityFilter = new[] { "nf", "nofilter" };
        if (Contains(extraOptions, qualityFilter))
        {
            whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, qualityFilter);
            whoKnowsSettings.QualityFilterDisabled = true;
        }

        var (enabled, newSearchValue) = RedirectsEnabled(whoKnowsSettings.NewSearchValue);
        whoKnowsSettings.RedirectsEnabled = enabled;
        whoKnowsSettings.NewSearchValue = newSearchValue;

        return whoKnowsSettings;
    }

    public static (bool Enabled, string NewSearchValue) OrderByPlaycount(string extraOptions)
    {
        var noRedirect = new[] { "plays", "playcount", "orderbyplays", "orderbyplaycount" };
        if (Contains(extraOptions, noRedirect))
        {
            return (true, ContainsAndRemove(extraOptions, noRedirect));
        }

        return (false, extraOptions);
    }

    public static (bool Enabled, string NewSearchValue) RedirectsEnabled(string extraOptions)
    {
        var noRedirect = new[] { "nr", "noredirect" };
        if (Contains(extraOptions, noRedirect))
        {
            return (false, ContainsAndRemove(extraOptions, noRedirect));
        }

        return (true, extraOptions);
    }

    public static (bool Enabled, string NewSearchValue) EditModeEnabled(string extraOptions)
    {
        var editMode = new[] { "edit", "editmode" };
        if (Contains(extraOptions, editMode))
        {
            return (true, ContainsAndRemove(extraOptions, editMode));
        }

        return (false, extraOptions);
    }

    public static (bool Enabled, string NewSearchValue) HideSingles(string extraOptions)
    {
        var noSingles = new[] { "ns", "nosingles", "hidesingles", "filtersingles" };
        if (Contains(extraOptions, noSingles))
        {
            return (true, ContainsAndRemove(extraOptions, noSingles));
        }

        return (false, extraOptions);
    }

    public static (bool User, string NewSearchValue) IsUserView(string extraOptions)
    {
        var guild = new[] { "server", "guild" };
        if (Contains(extraOptions, guild))
        {
            return (false, ContainsAndRemove(extraOptions, guild));
        }

        return (true, extraOptions);
    }

    public static (UpdateType updateType, bool optionPicked) GetUpdateType(string extraOptions, bool isSupporter)
    {
        var updateType = new UpdateType();
        var optionPicked = false;

        var full = new[] { "full", "force", "f" };
        if (Contains(extraOptions, full))
        {
            updateType |= UpdateType.Full;
            optionPicked = true;
        }
        else
        {
            var allPlays = new[] { "plays", "allplays" };
            if (Contains(extraOptions, allPlays))
            {
                updateType |= UpdateType.AllPlays;
                optionPicked = true;
            }

            var artists = new[] { "artists", "artist", "a" };
            if (Contains(extraOptions, artists))
            {
                updateType |= UpdateType.Artists;
                optionPicked = true;
            }

            var albums = new[] { "albums", "album", "ab" };
            if (Contains(extraOptions, albums))
            {
                updateType |= UpdateType.Albums;
                optionPicked = true;
            }

            var tracks = new[] { "tracks", "track", "tr" };
            if (Contains(extraOptions, tracks))
            {
                updateType |= UpdateType.Tracks;
                optionPicked = true;
            }
        }

        // var discogs = new[] { "discogs", "discog", "vinyl", "collection" };
        // if (Contains(extraOptions, discogs))
        // {
        //     updateType |= UpdateType.Discogs;
        //     optionPicked = true;
        // }

        return (updateType, optionPicked);
    }

    public async Task<UserSettingsModel> GetUser(
        string extraOptions,
        User user,
        CommandContext context,
        bool firstOptionIsLfmUsername = false)
    {
        return await GetUser(extraOptions, user, context.Guild, context.User, firstOptionIsLfmUsername);
    }

    public async Task<UserSettingsModel> GetUser(
        string extraOptions,
        User user,
        NetCord.Gateway.Guild discordGuild,
        NetCord.User discordUser,
        bool firstOptionIsLfmUsername = false,
        bool allowNonFmbot = false)
    {
        string discordUserName;
        if (discordGuild != null && discordGuild.Users.TryGetValue(user.DiscordUserId, out var discordGuildUser))
        {
            discordUserName = discordGuildUser.GetDisplayName();
        }
        else
        {
            discordUserName = discordUser.GetDisplayName();
        }

        var settingsModel = new UserSettingsModel
        {
            DifferentUser = false,
            TimeZone = user.TimeZone,
            UserNameLastFm = user.UserNameLastFM,
            SessionKeyLastFm = user.SessionKeyLastFm,
            DiscordUserId = discordUser.Id,
            DisplayName = discordUserName,
            UserId = user.UserId,
            UserType = user.UserType,
            RegisteredLastFm = user.RegisteredLastFm,
            NewSearchValue = extraOptions
        };

        if (extraOptions == null)
        {
            return settingsModel;
        }

        var options = extraOptions.Split(' ');

        if (firstOptionIsLfmUsername && !string.IsNullOrWhiteSpace(options.First()))
        {
            var otherUser = await GetDifferentUser(options.First());

            if (otherUser != null)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { options.First() }, true);

                settingsModel.DisplayName = otherUser.UserNameLastFM;
                settingsModel.TimeZone = otherUser.TimeZone ?? user.TimeZone;
                settingsModel.DifferentUser = true;
                settingsModel.DiscordUserId = otherUser.DiscordUserId;
                settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                settingsModel.SessionKeyLastFm = otherUser.SessionKeyLastFm;
                settingsModel.UserType = otherUser.UserType;
                settingsModel.UserId = otherUser.UserId;
                settingsModel.RegisteredLastFm = otherUser.RegisteredLastFm;

                return settingsModel;
            }

            if (allowNonFmbot)
            {
                if (options.First().Length is >= 3 and <= 15)
                {
                    var lfmUserName = options.First().ToLower();

                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, [lfmUserName], true);
                    settingsModel.UserNameLastFm = lfmUserName;
                    settingsModel.DisplayName = lfmUserName;
                    settingsModel.SessionKeyLastFm = null;
                    settingsModel.RegisteredLastFm = null;
                    settingsModel.UserId = 0;
                    settingsModel.DifferentUser = true;

                    return settingsModel;
                }
            }
        }

        foreach (var option in options)
        {
            var otherUser = await DiscordIdToUser(option);

            if (otherUser != null)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[]
                {
                    "<@&", "<", "@", "!", ">", "<@&",
                    otherUser.DiscordUserId.ToString(), $"<@!{otherUser.DiscordUserId}>", $"<@{otherUser.DiscordUserId}>",
                    $"<@&{otherUser.DiscordUserId}>",
                    otherUser.UserNameLastFM.ToLower()
                }, true);

                if (discordGuild != null && discordGuild.Users.TryGetValue(otherUser.DiscordUserId, out var discordGuildUser2))
                {
                    settingsModel.DisplayName = discordGuildUser2.GetDisplayName();
                }
                else
                {
                    settingsModel.DisplayName = otherUser.UserNameLastFM;
                }

                settingsModel.DifferentUser = true;
                settingsModel.DiscordUserId = otherUser.DiscordUserId;
                settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                settingsModel.TimeZone = otherUser.TimeZone ?? user.TimeZone;
                settingsModel.UserType = otherUser.UserType;
                settingsModel.UserId = otherUser.UserId;
                settingsModel.RegisteredLastFm = otherUser.RegisteredLastFm;
            }

            if (option.StartsWith("lfm:") && option.Length > 4)
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, ["lfm:"], true);

                var lfmUserName = option.ToLower().Replace("lfm:", "");

                var foundLfmUser = await GetDifferentUser(lfmUserName);

                if (foundLfmUser != null)
                {
                    settingsModel.NewSearchValue =
                        ContainsAndRemove(settingsModel.NewSearchValue, new[] { lfmUserName, $"lfm:{lfmUserName}" }, true);

                    settingsModel.DisplayName = foundLfmUser.UserNameLastFM;
                    settingsModel.TimeZone = foundLfmUser.TimeZone ?? user.TimeZone;
                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = foundLfmUser.DiscordUserId;
                    settingsModel.UserNameLastFm = foundLfmUser.UserNameLastFM;
                    settingsModel.SessionKeyLastFm = foundLfmUser.SessionKeyLastFm;
                    settingsModel.UserType = foundLfmUser.UserType;
                    settingsModel.UserId = foundLfmUser.UserId;
                    settingsModel.RegisteredLastFm = foundLfmUser.RegisteredLastFm;

                    return settingsModel;
                }

                if (allowNonFmbot)
                {
                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { lfmUserName }, true);
                    settingsModel.UserNameLastFm = lfmUserName;
                    settingsModel.DisplayName = lfmUserName;
                    settingsModel.SessionKeyLastFm = null;
                    settingsModel.RegisteredLastFm = null;
                    settingsModel.UserId = 0;
                    settingsModel.DifferentUser = true;

                    return settingsModel;
                }
            }
        }

        return settingsModel;
    }

    public async Task<UserSettingsModel> GetOriginalContextUser(
        ulong discordUserId, ulong requesterUserId, NetCord.Gateway.Guild discordGuild, NetCord.User contextDiscordUser)
    {
        NetCord.GuildUser guildUser = null;
        if (discordGuild != null)
        {
            discordGuild.Users.TryGetValue(discordUserId, out guildUser);
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var targetUser = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        var differentUser = discordUserId != requesterUserId;

        return new UserSettingsModel
        {
            DiscordUserId = targetUser.DiscordUserId,
            DifferentUser = differentUser,
            TimeZone = targetUser.TimeZone,
            UserId = targetUser.UserId,
            DisplayName = guildUser?.GetDisplayName() ??
                          (differentUser ? targetUser.UserNameLastFM : contextDiscordUser.GetDisplayName()),
            RegisteredLastFm = targetUser.RegisteredLastFm,
            SessionKeyLastFm = targetUser.SessionKeyLastFm,
            UserNameLastFm = targetUser.UserNameLastFM,
            UserType = targetUser.UserType
        };
    }

    public async Task<User> GetDifferentUser(string searchValue)
    {
        var otherUser = await DiscordIdToUser(searchValue);

        if (otherUser == null)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();

            searchValue = searchValue.ToLower().Replace("lfm:", "");
            return await db.Users
                .AsQueryable()
                .OrderByDescending(o => o.LastUsed != null)
                .ThenByDescending(o => o.LastUsed)
                .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == searchValue);
        }

        return otherUser;
    }

    private async Task<User> DiscordIdToUser(string value)
    {
        if (!value.Contains("<@") && value.Length is < 17 or > 19)
        {
            return null;
        }

        var id = value.Trim('@', '!', '<', '>', '&');

        if (!ulong.TryParse(id, out var discordUserId))
        {
            return null;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public static int GetAmount(
        string extraOptions,
        int amount = 8,
        int maxAmount = 20)
    {
        if (extraOptions == null)
        {
            return amount;
        }

        var options = extraOptions.Split(' ');
        foreach (var option in options)
        {
            if (int.TryParse(option, out var result))
            {
                if (result > 0 && result <= 100)
                {
                    if (result > maxAmount)
                    {
                        return maxAmount;
                    }

                    return result;
                }
            }
        }

        return amount;
    }

    public static int? GetYear(string extraOptions, bool cleanSetter = true, int minYear = 1900) =>
        Core.TimePeriodParser.GetYear(extraOptions, cleanSetter, minYear);

    public static long GetGoalAmount(
        string extraOptions,
        long currentPlaycount)
    {
        var goalAmount = 100;
        var ownGoalSet = false;

        if (extraOptions != null)
        {
            var options = extraOptions
                .Replace("(", "")
                .Replace(")", "")
                .Replace("*", "")
                .Replace("`", "")
                .Replace(",", "")
                .Replace(".", "")
                .Replace(" ", "")
                .Split(' ');

            foreach (var option in options)
            {
                if (option.ToLower().EndsWith("k"))
                {
                    if (int.TryParse(option.ToLower().Replace("k", ""), out var kResult))
                    {
                        kResult *= 1000;
                        if (kResult > currentPlaycount)
                        {
                            goalAmount = kResult;
                            ownGoalSet = true;
                            break;
                        }
                    }
                }
                else if (int.TryParse(option, out var result) && result > currentPlaycount)
                {
                    goalAmount = result;
                    ownGoalSet = true;
                }
            }
        }


        if (!ownGoalSet)
        {
            foreach (var breakPoint in Constants.PlayCountBreakPoints)
            {
                if (currentPlaycount < breakPoint)
                {
                    goalAmount = breakPoint;
                    break;
                }
            }
        }

        if (goalAmount > 10000000)
        {
            goalAmount = 10000000;
        }

        return goalAmount;
    }

    public static (int amount, bool isRandom) GetMilestoneAmount(
        string extraOptions,
        long currentPlaycount)
    {
        var goalAmount = 100;
        var ownGoalSet = false;
        var isRandom = false;

        if (extraOptions != null)
        {
            var options = extraOptions
                .Replace("(", "")
                .Replace(")", "")
                .Replace("*", "")
                .Replace("`", "")
                .Replace(",", "")
                .Replace(".", "")
                .Replace(" ", "")
                .Split(' ');

            foreach (var option in options)
            {
                if (option.ToLower().EndsWith("k"))
                {
                    if (int.TryParse(option.ToLower().Replace("k", ""), out var kResult))
                    {
                        kResult *= 1000;
                        if (kResult < currentPlaycount)
                        {
                            goalAmount = kResult;
                            ownGoalSet = true;
                            break;
                        }
                    }
                }
                else if (int.TryParse(option, out var result) && result < currentPlaycount)
                {
                    goalAmount = result;
                    ownGoalSet = true;
                    break;
                }

                if (option.ToLower().Contains("random") || option.ToLower().Contains("rnd"))
                {
                    goalAmount = RandomNumberGenerator.GetInt32(1, (int)currentPlaycount);
                    ownGoalSet = true;
                    isRandom = true;
                    break;
                }
            }
        }

        if (!ownGoalSet)
        {
            foreach (var breakPoint in Constants.PlayCountBreakPoints.OrderByDescending(o => o))
            {
                if (currentPlaycount > breakPoint)
                {
                    goalAmount = breakPoint;
                    break;
                }
            }
        }

        if (goalAmount < 1)
        {
            goalAmount = 1;
        }

        return (goalAmount, isRandom);
    }

    public static GuildRankingSettings SetGuildRankingSettings(GuildRankingSettings guildRankingSettings, string extraOptions)
    {
        var setGuildRankingSettings = guildRankingSettings;

        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return setGuildRankingSettings;
        }

        var playcounts = new[] { "p", "pc", "playcount", "plays", "scrobbles" };
        if (Contains(extraOptions, playcounts))
        {
            guildRankingSettings.NewSearchValue = ContainsAndRemove(guildRankingSettings.NewSearchValue, playcounts);
            setGuildRankingSettings.OrderType = OrderType.Playcount;
        }

        var listenerCounts = new[] { "l", "lc", "listenercount", "listeners" };
        if (Contains(extraOptions, listenerCounts))
        {
            guildRankingSettings.NewSearchValue = ContainsAndRemove(guildRankingSettings.NewSearchValue, listenerCounts);
            setGuildRankingSettings.OrderType = OrderType.Listeners;
        }

        var roleFilter = new[] { "rf", "rolefilter", "rolepicker", "roleselector" };
        if (Contains(extraOptions, roleFilter))
        {
            guildRankingSettings.NewSearchValue = ContainsAndRemove(guildRankingSettings.NewSearchValue, roleFilter);
            setGuildRankingSettings.DisplayRoleFilter = true;
        }

        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            guildRankingSettings.NewSearchValue = null;
        }

        return setGuildRankingSettings;
    }

    public static FeaturedView SetFeaturedTypeView(string extraOptions)
    {
        var featuredView = FeaturedView.User;

        var global = new[] { "g", "global", "gw" };
        if (Contains(extraOptions, global))
        {
            featuredView = FeaturedView.Global;
        }

        var friends = new[] { "friends", "f" };
        if (Contains(extraOptions, friends))
        {
            featuredView = FeaturedView.Friends;
        }

        var guild = new[] { "server", "guild", "members", "s" };
        if (Contains(extraOptions, guild))
        {
            featuredView = FeaturedView.Server;
        }

        var guildFeatured = new[] { "serverfeatured", "customfeatured", "sf" };
        if (Contains(extraOptions, guildFeatured))
        {
            featuredView = FeaturedView.GuildFeatured;
        }

        var guildFeaturedUser = new[] { "serverfeatureduser", "customfeatureduser", "sfu" };
        if (Contains(extraOptions, guildFeaturedUser))
        {
            featuredView = FeaturedView.GuildFeaturedUser;
        }

        return featuredView;
    }

    public static GuildRankingSettings TimeSettingsToGuildRankingSettings(GuildRankingSettings guildRankingSettings,
        TimeSettingsModel timeSettings)
    {
        guildRankingSettings.ChartTimePeriod = timeSettings.TimePeriod;
        guildRankingSettings.TimeDescription = timeSettings.Description;
        guildRankingSettings.TimeSettings = timeSettings;
        guildRankingSettings.EndDateTime = timeSettings.EndDateTime;
        guildRankingSettings.BillboardEndDateTime = timeSettings.BillboardEndDateTime;
        guildRankingSettings.BillboardTimeDescription = timeSettings.BillboardTimeDescription;
        guildRankingSettings.AmountOfDays = timeSettings.PlayDays.GetValueOrDefault();
        guildRankingSettings.AmountOfDaysWithBillboard = timeSettings.PlayDaysWithBillboard.GetValueOrDefault();
        guildRankingSettings.StartDateTime =
            timeSettings.StartDateTime.GetValueOrDefault(DateTime.UtcNow.AddDays(-guildRankingSettings.AmountOfDays));
        guildRankingSettings.BillboardStartDateTime =
            timeSettings.BillboardStartDateTime.GetValueOrDefault(DateTime.UtcNow.AddDays(-guildRankingSettings.AmountOfDaysWithBillboard));
        guildRankingSettings.NewSearchValue = timeSettings.NewSearchValue;

        return guildRankingSettings;
    }

    public static CrownViewType SetCrownViewSettings(string extraOptions)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return CrownViewType.Playcount;
        }

        if (extraOptions.Contains("p") || extraOptions.Contains("pc") || extraOptions.Contains("playcount") ||
            extraOptions.Contains("plays"))
        {
            return CrownViewType.Playcount;
        }

        if (extraOptions.Contains("r") || extraOptions.Contains("rc") || extraOptions.Contains("recent") || extraOptions.Contains("new") ||
            extraOptions.Contains("latest"))
        {
            return CrownViewType.Recent;
        }

        if (extraOptions.Contains("s") || extraOptions.Contains("stolen") || extraOptions.Contains("yoinked") ||
            extraOptions.Contains("yeeted"))
        {
            return CrownViewType.Stolen;
        }

        return CrownViewType.Playcount;
    }

    public static bool Contains(string extraOptions, string[] values) =>
        Core.TimePeriodParser.Contains(extraOptions, values);

    public static string ContainsAndRemove(string extraOptions, string[] values, bool alwaysReturnValue = false) =>
        Core.TimePeriodParser.ContainsAndRemove(extraOptions, values, alwaysReturnValue);

    public static (FmEmbedType embedType, string newSearchValue) GetEmbedType(string extraOptions,
        FmEmbedType defaultEmbedType = FmEmbedType.EmbedMini)
    {
        if (string.IsNullOrWhiteSpace(extraOptions))
        {
            return (defaultEmbedType, extraOptions);
        }

        var newSearchValue = extraOptions;

        var embedMini = new[] { "embed", "embedmini", "mini" };
        var embedFull = new[] { "embedfull", "full" };
        var embedTiny = new[] { "embedtiny", "tiny" };
        var textFull = new[] { "textfull", "txtfull" };
        var textMini = new[] { "text", "textmini", "txtmini" };
        var textOneLine = new[] { "textoneline", "oneline", "1line" };

        if (Contains(extraOptions, embedTiny))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embedTiny);
            return (FmEmbedType.EmbedTiny, newSearchValue);
        }

        if (Contains(extraOptions, embedFull))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embedFull);
            return (FmEmbedType.EmbedFull, newSearchValue);
        }

        if (Contains(extraOptions, embedMini))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, embedMini);
            return (FmEmbedType.EmbedMini, newSearchValue);
        }

        if (Contains(extraOptions, textFull))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, textFull);
            return (FmEmbedType.TextFull, newSearchValue);
        }

        if (Contains(extraOptions, textOneLine))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, textOneLine);
            return (FmEmbedType.TextOneLine, newSearchValue);
        }

        if (Contains(extraOptions, textMini))
        {
            newSearchValue = ContainsAndRemove(newSearchValue, textMini);
            return (FmEmbedType.TextMini, newSearchValue);
        }

        return (defaultEmbedType, extraOptions);
    }
}
