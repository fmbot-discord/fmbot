using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class UserBuilder
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly TimerService _timer;
    private readonly FeaturedService _featuredService;
    private readonly BotSettings _botSettings;
    private readonly LastFmRepository _lastFmRepository;
    private readonly PlayService _playService;
    private readonly TimeService _timeService;
    private readonly ArtistsService _artistsService;
    private readonly SupporterService _supporterService;

    public UserBuilder(UserService userService,
        GuildService guildService,
        IPrefixService prefixService,
        TimerService timer,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService,
        LastFmRepository lastFmRepository,
        PlayService playService,
        TimeService timeService,
        ArtistsService artistsService,
        SupporterService supporterService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._timer = timer;
        this._featuredService = featuredService;
        this._lastFmRepository = lastFmRepository;
        this._playService = playService;
        this._timeService = timeService;
        this._artistsService = artistsService;
        this._supporterService = supporterService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ResponseModel> FeaturedAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild?.Id);

        if (this._timer._currentFeatured == null)
        {
            response.ResponseType = ResponseType.Text;
            response.Text = ".fmbot is still starting up, please try again in a bit..";
            response.CommandResponse = CommandResponse.Cooldown;
            return response;
        }

        response.Embed.WithThumbnailUrl(this._timer._currentFeatured.ImageUrl);
        response.Embed.AddField("Featured:", this._timer._currentFeatured.Description);

        if (guild?.GuildUsers != null && guild.GuildUsers.Any() && this._timer._currentFeatured.UserId.HasValue && this._timer._currentFeatured.UserId.Value != 0)
        {
            var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == this._timer._currentFeatured.UserId);

            if (guildUser != null)
            {
                response.Text = "in-server";
                response.Embed.AddField("🥳 Congratulations!", $"This user is in your server under the name {guildUser.UserName}.");
            }
        }

        if (this._timer._currentFeatured.SupporterDay)
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                response.Embed.AddField("Get featured", $"*Also want a higher chance of getting featured on Supporter Sunday? " +
                                                            $"[Get .fmbot supporter here.]({Constants.GetSupporterLink})*");
            }
        }

        response.Embed.WithFooter($"View your featured history with '{context.Prefix}featuredlog'");

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
        }

        return response;
    }

    public async Task<ResponseModel> BotScrobblingAsync(ContextModel context, string option)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var newBotScrobblingDisabledSetting = await this._userService.ToggleBotScrobblingAsync(context.ContextUser, option);

        response.Embed.WithDescription("Bot scrobbling allows you to automatically scrobble music from Discord music bots to your Last.fm account. " +
                                    "For this to work properly you need to make sure .fmbot can see the voice channel and use a supported music bot.\n\n" +
                                    "Only tracks that already exist on Last.fm will be scrobbled. This feature works best with Spotify music.\n\n" +
                                    "Currently supported bots:\n" +
                                    "- Hydra (Only with Now Playing messages enabled in English)\n" +
                                    "- Cakey Bot (Only with Now Playing messages enabled in English)\n" +
                                    "- SoundCloud");

        if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && !string.IsNullOrWhiteSpace(context.ContextUser.SessionKeyLastFm))
        {
            response.Embed.AddField("Status", "✅ Enabled and ready.");
            response.Embed.WithFooter($"Use '{context.Prefix}botscrobbling off' to disable.");
        }
        else if ((newBotScrobblingDisabledSetting == null || newBotScrobblingDisabledSetting == false) && string.IsNullOrWhiteSpace(context.ContextUser.SessionKeyLastFm))
        {
            response.Embed.AddField("Status", $"⚠️ Bot scrobbling is enabled, but you need to login through `{context.Prefix}login` first.");
        }
        else
        {
            response.Embed.AddField("Status", $"❌ Disabled. Do '{context.Prefix}botscrobbling on' to enable.");
        }

        return response;
    }

    public async Task<ResponseModel> FeaturedLogAsync(ContextModel context, UserSettingsModel userSettings)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithTitle(
                    $"{userSettings.DiscordUserName}{userSettings.UserType.UserTypeToIcon()}'s featured history");

        var featuredHistory = await this._featuredService.GetFeaturedHistoryForUser(userSettings.UserId);

        var description = new StringBuilder();
        var odds = await this._featuredService.GetFeaturedOddsAsync();

        if (!featuredHistory.Any())
        {
            if (!userSettings.DifferentUser)
            {
                description.AppendLine("Sorry, you haven't been featured yet... <:404:882220605783560222>");
                description.AppendLine();
                description.AppendLine($"But don't give up hope just yet!");
                description.AppendLine($"Every hour there is a 1 in {odds} chance that you might be picked.");

                if (context.ContextUser.UserType == UserType.Supporter)
                {
                    description.AppendLine();
                    description.AppendLine($"Also, as a thank you for being a supporter you have a higher chance of becoming featured every first Sunday of the month on Supporter Sunday.");
                }
                else
                {
                    description.AppendLine($"Or become an [.fmbot supporter](https://opencollective.com/fmbot/contribute) and get a higher chance every Supporter Sunday.");
                }

                if (context.DiscordGuild?.Id != this._botSettings.Bot.BaseServerId)
                {
                    description.AppendLine();
                    description.AppendLine($"Want to be notified when you get featured?");
                    description.AppendLine($"Join [our server](https://discord.gg/6y3jJjtDqK) and you'll get a ping whenever it happens.");
                }
            }
            else
            {
                description.AppendLine("Hmm, they haven't been featured yet... <:404:882220605783560222>");
                description.AppendLine();
                description.AppendLine($"But don't let them give up hope just yet!");
                description.AppendLine($"Every hour there is a 1 in {odds} chance that they might be picked.");
            }
        }
        else
        {
            foreach (var featured in featuredHistory.Take(12))
            {
                var dateValue = ((DateTimeOffset)featured.DateTime).ToUnixTimeSeconds();
                description.AppendLine($"Mode: `{featured.FeaturedMode}`");
                description.AppendLine($"<t:{dateValue}:F> (<t:{dateValue}:R>)");
                if (featured.TrackName != null)
                {
                    description.AppendLine($"**{featured.TrackName}**");
                    description.AppendLine($"**{featured.ArtistName}** | *{featured.AlbumName}*");
                }
                else
                {
                    description.AppendLine($"**{featured.ArtistName}** - **{featured.AlbumName}**");
                }

                if (featured.SupporterDay)
                {
                    description.AppendLine($"⭐ On supporter Sunday");
                }

                description.AppendLine();
            }

            var self = userSettings.DifferentUser ? "They" : "You";
            var footer = new StringBuilder();

            footer.AppendLine(featuredHistory.Count == 1
                ? $"{self} have only been featured once. Every hour, that is a chance of 1 in {odds}!"
                : $"{self} have been featured {featuredHistory.Count} times");

            if (context.ContextUser.UserType == UserType.Supporter)
            {
                footer.AppendLine($"As a thank you for supporting, you have better odds every first Sunday of the month.");
            }
            else
            {
                footer.AppendLine($"Every first Sunday of the month is Supporter Sunday. Check '{context.Prefix}getsupporter' for info.");
            }

            response.Embed.WithFooter(footer.ToString());
        }

        response.Embed.WithDescription(description.ToString());

        return response;
    }


    public async Task<ResponseModel> StatsAsync(ContextModel context, UserSettingsModel userSettings, User user)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        string userTitle;
        if (userSettings.DifferentUser)
        {
            if (userSettings.DifferentUser && user.DiscordUserId == userSettings.DiscordUserId)
            {
                response.Embed.WithDescription("That user is not registered in .fmbot.");
                response.CommandResponse = CommandResponse.WrongInput;
                return response;
            }

            userTitle =
                $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser)}";
            user = await this._userService.GetFullUserAsync(userSettings.DiscordUserId);
        }
        else
        {
            userTitle = await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);
        }

        response.EmbedAuthor.WithName($"Stats for {userTitle}");
        response.EmbedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}");
        response.Embed.WithAuthor(response.EmbedAuthor);

        var userInfo = await this._lastFmRepository.GetLfmUserInfoAsync(userSettings.UserNameLastFm);

        var userAvatar = userInfo.Image?.FirstOrDefault(f => f.Size == "extralarge");
        if (!string.IsNullOrWhiteSpace(userAvatar?.Text))
        {
            response.Embed.WithThumbnailUrl(userAvatar.Text);
        }

        var description = new StringBuilder();
        if (user.UserType != UserType.User)
        {
            description.AppendLine($"{userSettings.UserType.UserTypeToIcon()} .fmbot {userSettings.UserType.ToString().ToLower()}");
        }

        if (userInfo.Type != "user" && userInfo.Type != "subscriber")
        {
            description.AppendLine($"Last.fm {userInfo.Type}");
        }

        if (description.Length > 0)
        {
            response.Embed.WithDescription(description.ToString());
        }

        var lastFmStats = new StringBuilder();
        lastFmStats.AppendLine($"Name: **{userInfo.Name}**");
        lastFmStats.AppendLine($"Username: **[{userSettings.UserNameLastFm}]({Constants.LastFMUserUrl}{userSettings.UserNameLastFm})**");
        if (userInfo.Subscriber != 0)
        {
            lastFmStats.AppendLine("Last.fm Pro subscriber");
        }

        lastFmStats.AppendLine($"Country: **{userInfo.Country}**");

        lastFmStats.AppendLine($"Registered: **<t:{userInfo.Registered.Text}:D>** (<t:{userInfo.Registered.Text}:R>)");

        response.Embed.AddField("Last.fm info", lastFmStats.ToString(), true);

        var age = DateTimeOffset.FromUnixTimeSeconds(userInfo.Registered.Text);
        var totalDays = (DateTime.UtcNow - age).TotalDays;
        var avgPerDay = userInfo.Playcount / totalDays;

        var playcounts = new StringBuilder();
        playcounts.AppendLine($"Scrobbles: **{userInfo.Playcount}**");
        playcounts.AppendLine($"Tracks: **{userInfo.TrackCount}**");
        playcounts.AppendLine($"Albums: **{userInfo.AlbumCount}**");
        playcounts.AppendLine($"Artists: **{userInfo.ArtistCount}**");
        response.Embed.AddField("Playcounts", playcounts.ToString(), true);

        var allPlays = await this._playService.GetAllUserPlays(userSettings.UserId);

        var stats = new StringBuilder();
        if (userSettings.UserType != UserType.User)
        {
            var hasImported = this._playService.UserHasImported(allPlays);
            if (hasImported)
            {
                stats.AppendLine("User has most likely imported plays from external source");
            }
        }

        stats.AppendLine($"Average of **{Math.Round(avgPerDay, 1)}** scrobbles per day");

        stats.AppendLine($"Average of **{Math.Round((double)userInfo.AlbumCount / userInfo.ArtistCount, 1)}** albums and **{Math.Round((double)userInfo.TrackCount / userInfo.ArtistCount, 1)}** tracks per artist");

        var topArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true);

        if (topArtists.Any())
        {
            var amount = topArtists.OrderByDescending(o => o.UserPlaycount).Take(10).Sum(s => s.UserPlaycount);
            stats.AppendLine($"Top **10** artists make up **{Math.Round((double)amount.GetValueOrDefault(0) / userInfo.Playcount * 100, 1)}%** of scrobbles");
        }

        var topDay = allPlays.GroupBy(g => g.TimePlayed.DayOfWeek).MaxBy(o => o.Count());
        if (topDay != null)
        {
            stats.AppendLine($"Most active day of the week is **{topDay.Key.ToString()}**");
        }

        if (stats.Length > 0)
        {
            response.Embed.AddField("Stats", stats.ToString());
        }

        var monthDescription = new StringBuilder();
        var monthGroups = allPlays
            .OrderByDescending(o => o.TimePlayed)
            .GroupBy(g => new { g.TimePlayed.Month, g.TimePlayed.Year });

        foreach (var month in monthGroups.Take(6))
        {
            if (!allPlays.Any(a => a.TimePlayed < DateTime.UtcNow.AddMonths(-month.Key.Month)))
            {
                break;
            }

            var time = await this._timeService.GetPlayTimeForPlays(month);
            monthDescription.AppendLine(
                $"**`{CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month.Key.Month)}`** " +
                $"- **{month.Count()}** plays " +
                $"- {StringExtensions.GetListeningTimeString(time, boldNumber: true)}");
        }
        if (monthDescription.Length > 0)
        {
            response.Embed.AddField("Months", monthDescription.ToString());
        }

        if (userSettings.UserType != UserType.User)
        {
            var yearDescription = new StringBuilder();
            var yearGroups = allPlays
                .OrderByDescending(o => o.TimePlayed)
                .GroupBy(g => g.TimePlayed.Year);

            var totalTime = await this._timeService.GetPlayTimeForPlays(allPlays);
            if (totalTime.TotalSeconds > 0)
            {
                yearDescription.AppendLine(
                    $"**` All`** " +
                    $"- **{allPlays.Count}** plays " +
                    $"- {StringExtensions.GetListeningTimeString(totalTime, boldNumber: true)}");
            }

            foreach (var year in yearGroups)
            {
                var time = await this._timeService.GetPlayTimeForPlays(year);
                yearDescription.AppendLine(
                    $"**`{year.Key}`** " +
                    $"- **{year.Count()}** plays " +
                    $"- {StringExtensions.GetListeningTimeString(time, boldNumber: true)}");
            }
            if (yearDescription.Length > 0)
            {
                response.Embed.AddField("Years", yearDescription.ToString());
            }
        }
        else
        {
            var randomHintNumber = new Random().Next(0, Constants.SupporterPromoChance);
            if (randomHintNumber == 1 && this._supporterService.ShowPromotionalMessage(context.ContextUser.UserType, context.DiscordGuild?.Id))
            {
                this._supporterService.SetGuildPromoCache(context.DiscordGuild?.Id);
                response.Embed.AddField("Years", $"*Want to see an overview of your scrobbles throughout the years? " +
                                                 $"[Get .fmbot supporter here.]({Constants.GetSupporterLink})*");
            }
        }

        var footer = new StringBuilder();
        if (user.Friends?.Count > 0)
        {
            footer.AppendLine($"Friends: {user.Friends?.Count}");
        }
        if (user.FriendedByUsers?.Count > 0)
        {
            footer.AppendLine($"Befriended by: {user.FriendedByUsers?.Count}");
        }
        if (footer.Length > 0)
        {
            response.Embed.WithFooter(footer.ToString());
        }

        return response;
    }
}
