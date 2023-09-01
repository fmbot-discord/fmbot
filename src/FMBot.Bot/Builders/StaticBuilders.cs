using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;


    public StaticBuilders(SupporterService supporterService, UserService userService, IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._supporterService = supporterService;
        this._userService = userService;
        this._contextFactory = contextFactory;
    }

    public static ResponseModel OutOfSync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithTitle("Using Spotify and tracking is out of sync?");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();
        embedDescription.AppendLine(".fmbot uses your Last.fm account for knowing what you listen to. ");
        embedDescription.AppendLine($"Unfortunately, Last.fm and Spotify sometimes have issues keeping up to date with your current song, which can cause `{context.Prefix}fm` and other commands to lag behind the song you're currently listening to.");
        embedDescription.AppendLine();
        embedDescription.Append("First, **.fmbot is not affiliated with Last.fm**. Your music is tracked by Last.fm, and not by .fmbot. ");
        embedDescription.AppendLine("This means that this is a Last.fm issue and **not an .fmbot issue**. __We can't fix it for you__, but we can give you some tips that worked for others.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("Some things you can try that usually work:");
        embedDescription.AppendLine("- Restarting your Spotify application");
        embedDescription.AppendLine("- Disconnecting and **reconnecting Spotify in [your Last.fm settings](https://www.last.fm/settings/applications)**");
        embedDescription.AppendLine();
        embedDescription.AppendLine("If the two options above don't work, check out **[the complete guide for this issue on the Last.fm support forums](https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184)**.");

        response.Embed.WithDescription(embedDescription.ToString());

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:", "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues, so the steps listed above might not work. " +
                                          ".fmbot is not affiliated with Last.fm.");
        }

        response.Components = new ComponentBuilder()
            .WithButton("Last.fm settings", style: ButtonStyle.Link, url: "https://www.last.fm/settings/applications")
            .WithButton("Full guide", style: ButtonStyle.Link, url: "https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184")
            .WithButton("Your profile", style: ButtonStyle.Link, url: $"{LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}");

        return response;
    }

    public async Task<ResponseModel> DonateAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var embedDescription = new StringBuilder();

        var existingSupporter = await this._supporterService.GetSupporter(context.DiscordUser.Id);

        if (context.ContextUser.UserType == UserType.Supporter || (existingSupporter != null && existingSupporter.Expired != true))
        {
            response.Embed.WithTitle("Thank you for being a supporter!");
            embedDescription.AppendLine("See a list of all your perks below:");
        }
        else
        {
            response.Embed.WithTitle("Support .fmbot and get supporter");
        }

        response.Embed.AddField("📈 Get more stats",
            "- Your lifetime Last.fm data will be stored for extra stats\n" +
            "- See first listen dates, have an expanded profile and advanced recaps\n" +
            "- Ability to store your complete Discogs collection");

        response.Embed.AddField("<:history:1131511469096312914> Import your Spotify history",
            "- Use your full Spotify listening history together with your Last.fm data\n" +
            "- Get the most accurate playcounts and listening time");

        response.Embed.AddField("🔥 Expanded judge command",
            "- GPT-4 powered compliments and roasts\n" +
            "- Increased usage limits and ability to use the command on others");

        response.Embed.AddField("<:discoveries:1145740579284713512> Go back in time",
            "- View when you discovered artists with the exclusive `discoveries` command\n" +
            "- See discovery dates in the `artist`, `album` and `track` commands");

        response.Embed.AddField("⚙️ Customize your commands",
            "- Expand your `.fm` footer with extra stats\n" +
            "- Add more friends for expanded friend commands\n"+
            "- Set your own personal automatic emote reactions");

        response.Embed.AddField("⭐ Flex your support",
            $"- Get a badge after your name to show your support\n" +
            "- Exclusive role and channel in the [.fmbot server](https://discord.gg/fmbot) with sneak peeks\n" +
            $"- Higher chance to get featured on Supporter Sundays (next up in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())})");

        if (existingSupporter != null)
        {
            var existingSupporterDescription = new StringBuilder();

            var created = DateTime.SpecifyKind(existingSupporter.Created, DateTimeKind.Utc);
            var createdValue = ((DateTimeOffset)created).ToUnixTimeSeconds();
            existingSupporterDescription.AppendLine($"Activation date: <t:{createdValue}:D>");

            if (existingSupporter.LastPayment.HasValue)
            {
                var lastPayment = DateTime.SpecifyKind(existingSupporter.LastPayment.Value, DateTimeKind.Utc);
                var lastPaymentValue = ((DateTimeOffset)lastPayment).ToUnixTimeSeconds();

                if (existingSupporter.SubscriptionType != SubscriptionType.Discord)
                {
                    existingSupporterDescription.AppendLine($"Last payment: <t:{lastPaymentValue}:D>");
                }
                else
                {
                    existingSupporterDescription.AppendLine($"Expiry date: <t:{lastPaymentValue}:D>");
                }
            }

            if (existingSupporter.SubscriptionType.HasValue)
            {
                existingSupporterDescription.AppendLine($"Subscription type: `{Enum.GetName(existingSupporter.SubscriptionType.Value)}`");
            }

            existingSupporterDescription.AppendLine($"Name: **{StringExtensions.Sanitize(existingSupporter.Name)}**");

            response.Embed.AddField("Your details", existingSupporterDescription.ToString());
        }

        response.Embed.WithDescription(embedDescription.ToString());

        var buttons = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);

        response.Components = buttons;

        return response;
    }

    public async Task<ResponseModel> SupportersAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var supporters = await this._supporterService.GetAllVisibleSupporters();

        var supporterLists = supporters.ChunkBy(10);

        var description = new StringBuilder();
        description.AppendLine("Thank you to all our supporters that help keep .fmbot running. If you would like to be on this list too, please check out our [OpenCollective](https://opencollective.com/fmbot/contribute). \n" +
                               $"To get a complete list of all supporter advantages, run `{context.Prefix}getsupporter`.");
        description.AppendLine();

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();
            supporterString.Append(description.ToString());

            foreach (var supporter in supporterList)
            {
                var type = supporter.SupporterType switch
                {
                    SupporterType.Guild => " (server)",
                    SupporterType.User => "",
                    SupporterType.Company => " (business)",
                    _ => ""
                };

                supporterString.AppendLine($"- **{supporter.Name}** {type}");
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithTitle(".fmbot supporters overview"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    public async Task<ResponseModel> OpenCollectiveSupportersAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var existingSupporters = await this._supporterService.GetAllSupporters();

        var supporters = await this._supporterService.GetOpenCollectiveSupporters();

        var supporterLists = supporters.Users.OrderByDescending(o => o.FirstPayment).Chunk(10);

        var description = new StringBuilder();

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();
            supporterString.Append(description.ToString());

            foreach (var supporter in supporterList)
            {
                supporterString.AppendLine($"**{supporter.Name}** - `{supporter.Id}` - `{supporter.SubscriptionType}`");

                var lastPayment = DateTime.SpecifyKind(supporter.LastPayment, DateTimeKind.Utc);
                var lastPaymentValue = ((DateTimeOffset)lastPayment).ToUnixTimeSeconds();

                var firstPayment = DateTime.SpecifyKind(supporter.FirstPayment, DateTimeKind.Utc);
                var firstPaymentValue = ((DateTimeOffset)firstPayment).ToUnixTimeSeconds();

                if (firstPaymentValue == lastPaymentValue && supporter.SubscriptionType == SubscriptionType.Lifetime)
                {
                    supporterString.AppendLine($"Purchase date: <t:{firstPaymentValue}:D>");
                }
                else
                {
                    supporterString.AppendLine($"First payment: <t:{firstPaymentValue}:D> - Last payment: <t:{lastPaymentValue}:D>");
                }

                var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == supporter.Id);
                if (existingSupporter != null)
                {
                    supporterString.Append($"✅ Connected");

                    if (existingSupporter.Expired == true)
                    {
                        supporterString.Append($" *(Expired)*");
                    }

                    supporterString.Append($" - {existingSupporter.DiscordUserId} / <@{existingSupporter.DiscordUserId}>");
                    supporterString.AppendLine();
                }

                supporterString.AppendLine();
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithUrl("https://opencollective.com/fmbot/transactions")
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter($"OC: {supporters.Users.Count} - db: {existingSupporters.Count}\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Monthly && c.LastPayment >= DateTime.Now.AddDays(-35))} active monthly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Monthly)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Yearly && c.LastPayment >= DateTime.Now.AddDays(-370))} active yearly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Yearly)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Lifetime)} lifetime")
                .WithTitle(".fmbot opencollective supporters overview"));
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No pages, most likely an error while fetching supporters"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }

    public async Task<ResponseModel> DiscordSupportersAsync(
        ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        await using var db = await this._contextFactory.CreateDbContextAsync();

        var existingSupporters = await db.Supporters
            .Where(w => w.SubscriptionType == SubscriptionType.Discord &&
                        w.DiscordUserId.HasValue)
            .ToListAsync();

        var userIds = existingSupporters.Select(s => s.DiscordUserId.Value).ToList();
        var users = await db.Users
            .AsQueryable()
            .Where(w => userIds.Contains(w.DiscordUserId))
            .ToListAsync();

        var supporterLists = existingSupporters.OrderByDescending(o => o.Created).Chunk(10);

        var footer = new StringBuilder();

        footer.Append(
            $"Total: {existingSupporters.Count()}");
        footer.Append(
            $" - Active {existingSupporters.Count(c => c.Expired != true)}");
        footer.AppendLine();
        footer.Append(
            $"Average new per day: {Math.Round(existingSupporters.Where(w => w.Created >= DateTime.UtcNow.AddDays(-60)).GroupBy(g => g.Created.Date).Average(c => c.Count()), 1)}");
        footer.AppendLine();
        footer.Append(
            $"New yesterday: {existingSupporters.Count(c => c.Created.Date == DateTime.UtcNow.AddDays(-1).Date)}");
        footer.Append(
            $" - New today: {existingSupporters.Count(c => c.Created.Date == DateTime.UtcNow.Date)}");
        footer.AppendLine();
        footer.Append(
            $"New last month: {existingSupporters.Count(c => c.Created.Month == DateTime.UtcNow.AddMonths(-1).Month &&
                                                             c.Created.Year == DateTime.UtcNow.AddMonths(-1).Year)}");
        footer.Append(
            $" - New this month: {existingSupporters.Count(c => c.Created.Month == DateTime.UtcNow.Month &&
                                                             c.Created.Year == DateTime.UtcNow.Year)}");

        var pages = new List<PageBuilder>();
        foreach (var supporterList in supporterLists)
        {
            var supporterString = new StringBuilder();

            foreach (var supporter in supporterList)
            {
                supporterString.Append($"**{supporter.DiscordUserId}** - <@{supporter.DiscordUserId}>");

                var user = users.FirstOrDefault(f => f.DiscordUserId == supporter.DiscordUserId.Value);
                if (user != null)
                {
                    supporterString.Append($" - [{user.UserNameLastFM}]({Constants.LastFMUserUrl}{user.UserNameLastFM})");
                }
                else
                {
                    supporterString.Append($" - No .fmbot user :(");
                }

                supporterString.AppendLine();

                var startsAtValue = ((DateTimeOffset)supporter.Created).ToUnixTimeSeconds();

                var endsAt = DateTime.SpecifyKind(supporter.LastPayment.Value, DateTimeKind.Utc);
                var endsAtValue = ((DateTimeOffset)endsAt).ToUnixTimeSeconds();

                supporterString.AppendLine($"Started <t:{startsAtValue}:f> - Ends on <t:{endsAtValue}:D>");

                supporterString.AppendLine();
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithColor(DiscordConstants.InformationColorBlue)
                .WithAuthor(response.EmbedAuthor)
                .WithFooter(footer.ToString())
                .WithTitle(".fmbot Discord supporters overview"));
        }

        if (!pages.Any())
        {
            pages.Add(new PageBuilder()
                .WithDescription("No pages, most likely an error while fetching supporters"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }
}
