using System;
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
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Builders;

public class UserBuilder
{
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly TimerService _timer;
    private readonly FeaturedService _featuredService;
    private readonly BotSettings _botSettings;


    public UserBuilder(UserService userService,
        GuildService guildService,
        IPrefixService prefixService,
        TimerService timer,
        IOptions<BotSettings> botSettings,
        FeaturedService featuredService)
    {
        this._userService = userService;
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._timer = timer;
        this._featuredService = featuredService;
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

        response.Embed.WithFooter($"View your featured history with '{context.Prefix}featuredlog'");

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues");
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
                    $"{userSettings.DiscordUserName.FilterOutMentions()}{userSettings.UserType.UserTypeToIcon()}'s featured history");

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
                footer.AppendLine($"Every first Sunday of the month is Supporter Sunday. Check '{context.Prefix}donate' for info.");
            }

            response.Embed.WithFooter(footer.ToString());
        }

        response.Embed.WithDescription(description.ToString());

        return response;
    }
}
