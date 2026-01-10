using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using Shared.Domain.Enums;
using User = FMBot.Persistence.Domain.Models.User;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly SupporterService _supporterService;
    private readonly UserService _userService;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;


    public StaticBuilders(SupporterService supporterService, UserService userService,
        IDbContextFactory<FMBotDbContext> contextFactory)
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
        embedDescription.AppendLine(
            $"Last.fm and Spotify sometimes have issues keeping up with your current song, which can cause `{context.Prefix}fm` and other commands to lag behind the song you're currently listening to.");
        embedDescription.AppendLine();
        embedDescription.Append(
            "**.fmbot is not affiliated with Last.fm**. Your music is tracked by Last.fm, and not by .fmbot. ");
        embedDescription.AppendLine(
            "This means that this is a Last.fm issue and **not an .fmbot issue**. __We can't fix it for you__, but we can give you tips that worked for others.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("Things you can try:");
        embedDescription.AppendLine("- Restarting your Spotify application");
        embedDescription.AppendLine(
            "- Disconnecting and **reconnecting Spotify in [your Last.fm settings](https://www.last.fm/settings/applications)**");
        embedDescription.AppendLine();
        embedDescription.AppendLine(
            "If the two options above don't work, check out **[the complete guide for this issue on the Last.fm support forums](https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184)**.");

        response.Embed.WithDescription(embedDescription.ToString());

        if (PublicProperties.IssuesAtLastFm)
        {
            response.Embed.AddField("Note:",
                "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues, so the steps listed above might not work. " +
                ".fmbot is not affiliated with Last.fm.");
        }

        response.Components = new ActionRowProperties()
            .WithButton("Last.fm settings", url: "https://www.last.fm/settings/applications")
            .WithButton("Full guide",
                url: "https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184")
            .WithButton("Your profile",
                url: $"{LastfmUrlExtensions.GetUserUrl(context.ContextUser.UserNameLastFM)}");

        return response;
    }

    public async Task<ResponseModel> SupporterButtons(
        ContextModel context,
        bool expandWithPerks,
        bool showExpandButton,
        bool publicResponse = false,
        string userLocale = null,
        string source = "unknown")
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.Components = new ActionRowProperties();

        if (expandWithPerks)
        {
            response.Embed.AddField("📊 More stats, more insights",
                "-# Unlock lifetime Last.fm data for deeper analysis. Track first listens, get expanded profiles, expanded commands, your full Discogs collection and more.");

            response.Embed.AddField("<:history:1131511469096312914> Import your history",
                "-# Import and access your full Spotify and Apple Music history together with your Last.fm data for the most accurate playcounts, listening time, and insights.");

            response.Embed.AddField("<:discoveries:1145740579284713512> Go back in time",
                "-# See exactly when you discovered and re-discovered artists, albums, and tracks in commands and through the exclusive Discoveries and Gaps commands.");

            response.Embed.AddField("🎮 Play unlimited games",
                "-# Remove the daily limit on Jumble and Pixel Jumble and play as much as you want.");

            response.Embed.AddField("⚙️ Customize your commands",
                "-# Expand your .fm footer, add more friends, set automatic emoji reactions, and personalize your experience.");

            response.Embed.AddField("🎤 View lyrics",
                "-# View your favorite `.lyrics` directly inside of .fmbot.");

            response.Embed.AddField("⭐ Exclusive supporter perks",
                $"-# Show your support with a badge, gain access to a private [Discord role and channel](https://discord.gg/fmbot), and a higher chance to be featured on Supporter Sunday (next up in {FeaturedService.GetDaysUntilNextSupporterSunday()} {StringExtensions.GetDaysString(FeaturedService.GetDaysUntilNextSupporterSunday())}).");
        }
        else
        {
            response.Embed.WithDescription(
                "⭐ Take your .fmbot experience to the next level with new features and benefits. " +
                "Import and use your history, access extra statistics, play unlimited games, support development and much more. " +
                "Please note that .fmbot is not affiliated with Last.fm.");
        }

        var existingSupporter = await this._supporterService.GetSupporter(context.ContextUser.DiscordUserId);
        var stripeSupporter = await this._supporterService.GetStripeSupporter(context.ContextUser.DiscordUserId);

        if (publicResponse)
        {
            response.Components.WithButton(
                SupporterService.IsSupporter(context.ContextUser.UserType)
                    ? "Manage your supporter"
                    : "Get .fmbot supporter",
                style: ButtonStyle.Secondary,
                customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:true:false:false:{source}");
        }
        else
        {
            if (SupporterService.IsSupporter(context.ContextUser.UserType) &&
                existingSupporter != null && existingSupporter.Expired != true)
            {
                if (stripeSupporter == null)
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "Manage your subscription with the button below.");

                    response.Components.WithButton("View current supporter status", style: ButtonStyle.Secondary,
                        customId: InteractionConstants.SupporterLinks.ManageOverview);
                }
                else if (stripeSupporter.Type == StripeSupporterType.GiftedSupporter)
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "You have been gifted supporter status! Since this was a gift, you cannot manage this subscription directly.");
                }
                else
                {
                    response.Embed.AddField("Thank you for being a supporter",
                        "Manage your subscription with the link below.");

                    var stripeManageLink = await this._supporterService.GetSupporterManageLink(stripeSupporter);

                    response.Components.WithButton("Manage subscription",
                        url: stripeManageLink);
                }
            }
            else
            {
                var pricing = await this._supporterService.GetPricing(userLocale, stripeSupporter?.Currency);
                response.Embed.AddField($"Monthly - {pricing.MonthlyPriceString}",
                    $"-# {pricing.MonthlySubText}", true);
                response.Embed.AddField($"Yearly - {pricing.YearlyPriceString}",
                    $"-# {pricing.YearlySubText}", true);

                response.Components = new ActionRowProperties()
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:monthly:{source}", "Get monthly",
                        ButtonStyle.Primary))
                    .AddComponents(new ButtonProperties(
                        $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:yearly:{source}", "Get yearly",
                        ButtonStyle.Primary));

                if (pricing.LifetimePriceId != null &&
                    pricing.LifetimePriceString != null &&
                    pricing.LifetimeSubText != null)
                {
                    response.Embed.AddField($"Lifetime - {pricing.LifetimePriceString}",
                        $"-# {pricing.LifetimeSubText}", true);

                    response.Components.AddComponents(new ButtonProperties(
                        $"{InteractionConstants.SupporterLinks.GetPurchaseLink}:lifetime:{source}", "Get lifetime",
                        ButtonStyle.Primary));
                }
            }
        }

        if (showExpandButton)
        {
            if (expandWithPerks)
            {
                response.Components.WithButton("Hide all perks", style: ButtonStyle.Secondary,
                    customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:false:false:true:{source}");
            }
            else
            {
                response.Components.WithButton("View all perks", style: ButtonStyle.Secondary,
                    customId: $"{InteractionConstants.SupporterLinks.GetPurchaseButtons}:false:true:true:{source}");
            }
        }

        return response;
    }

    public async Task<ResponseModel> BuildGiftSupporterResponse(ulong purchaserDiscordId, User recipient,
        string userLocale)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (recipient == null)
        {
            response.Text = "❌ This user has not used .fmbot before. They need to set up their account first.";
            response.CommandResponse = CommandResponse.UsernameNotSet;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        if (recipient.DiscordUserId == purchaserDiscordId)
        {
            response.Text = "❌ You cannot gift supporter to yourself. Use `/getsupporter` instead.";
            response.CommandResponse = CommandResponse.WrongInput;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        if (SupporterService.IsSupporter(recipient.UserType))
        {
            response.Text = "❌ The user you want to gift supporter already has access to the supporter perks.";
            response.CommandResponse = CommandResponse.Cooldown;
            response.ResponseType = ResponseType.Text;
            return response;
        }

        response.Embed
            .WithTitle("🎁 Gift .fmbot supporter")
            .WithDescription(
                $"You are gifting supporter to **{recipient.UserNameLastFM}** (<@{recipient.DiscordUserId}>)")
            .WithColor(DiscordConstants.Gold);

        var existingStripeSupporter = await this._supporterService.GetStripeSupporter(purchaserDiscordId);
        var pricing = await this._supporterService.GetPricing(userLocale, existingStripeSupporter?.Currency,
            StripeSupporterType.GiftedSupporter);

        response.Embed.AddField("Note",
            "- This is a gift purchase - no subscription will be created\n" +
            "- The recipient will receive all supporter benefits\n" +
            "- Your identity will not be revealed",
            false);

        var actionRow = new ActionRowProperties();

        if (!string.IsNullOrEmpty(pricing.QuarterlyPriceId))
        {
            response.Embed.AddField($"Quarter - {pricing.QuarterlyPriceString}",
                $"-# {pricing.QuarterlySubText}", true);
            actionRow.WithButton("Gift quarter", $"gift-supporter-purchase-quarterly-{recipient.DiscordUserId}");
        }

        if (!string.IsNullOrEmpty(pricing.YearlyPriceId))
        {
            response.Embed.AddField($"Yearly - {pricing.YearlyPriceString}",
                $"-# {pricing.YearlySubText}", true);
            actionRow.WithButton("Gift year", $"gift-supporter-purchase-yearly-{recipient.DiscordUserId}");
        }

        if (!string.IsNullOrEmpty(pricing.TwoYearPriceId))
        {
            response.Embed.AddField($"Two years - {pricing.TwoYearPriceString}",
                $"-# {pricing.TwoYearSubText}", true);
            actionRow.WithButton("Gift two years", $"gift-supporter-purchase-twoyear-{recipient.DiscordUserId}");
        }

        // if (!string.IsNullOrEmpty(pricing.LifetimePriceId))
        // {
        //     actionRow.WithButton("Lifetime", $"gift-supporter-purchase-lifetime-{recipient.DiscordUserId}", ButtonStyle.Success, EmojiProperties.Standard("⭐"));
        // }

        response.Components = actionRow;

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
        description.AppendLine(
            $"Thank you to all our supporters that help keep .fmbot running. To view all supporter perks and join this list, run `{context.Prefix}getsupporter`.");
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

    public async Task<ResponseModel> OpenCollectiveSupportersAsync(ContextModel context, bool expiredOnly)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var existingSupporters = await this._supporterService.GetAllSupporters();

        var supporters = await this._supporterService.GetOpenCollectiveSupporters();

        if (expiredOnly)
        {
            var ocIds = existingSupporters
                .Where(w => w.SubscriptionType == SubscriptionType.MonthlyOpenCollective && w.OpenCollectiveId != null)
                .OrderByDescending(o => o.LastPayment)
                .GroupBy(g => g.OpenCollectiveId)
                .ToDictionary(d => d.Key, d => d.First());
            supporters.Users = supporters.Users.Where(w => ocIds.ContainsKey(w.Id) &&
                                                           ocIds[w.Id].Expired != true &&
                                                           ocIds[w.Id].SubscriptionType ==
                                                           SubscriptionType.MonthlyOpenCollective &&
                                                           ocIds[w.Id].LastPayment <= DateTime.UtcNow.AddDays(-61))
                .ToList();
        }

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

                if (firstPaymentValue == lastPaymentValue &&
                    supporter.SubscriptionType == SubscriptionType.LifetimeOpenCollective)
                {
                    supporterString.AppendLine($"Purchase date: <t:{firstPaymentValue}:D>");
                }
                else
                {
                    supporterString.AppendLine(
                        $"First payment: <t:{firstPaymentValue}:D> - Last payment: <t:{lastPaymentValue}:D>");
                }

                var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == supporter.Id);
                if (existingSupporter != null)
                {
                    supporterString.Append($"✅ Connected");

                    if (existingSupporter.Expired == true)
                    {
                        supporterString.Append($" *(Expired)*");
                    }

                    supporterString.Append(
                        $" - {existingSupporter.DiscordUserId} / <@{existingSupporter.DiscordUserId}>");
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
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.MonthlyOpenCollective && c.LastPayment >= DateTime.Now.AddDays(-35))} active monthly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.MonthlyOpenCollective)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.YearlyOpenCollective && c.LastPayment >= DateTime.Now.AddDays(-370))} active yearly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.YearlyOpenCollective)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.LifetimeOpenCollective)} lifetime")
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
                    supporterString.Append(
                        $" - [{user.UserNameLastFM}]({Constants.LastFMUserUrl}{user.UserNameLastFM})");
                }
                else
                {
                    supporterString.Append($" - No .fmbot user :(");
                }

                supporterString.AppendLine();

                var startsAtValue = ((DateTimeOffset)supporter.Created).ToUnixTimeSeconds();

                if (supporter.LastPayment.HasValue)
                {
                    var endsAt = DateTime.SpecifyKind(supporter.LastPayment.Value, DateTimeKind.Utc);
                    var endsAtValue = ((DateTimeOffset)endsAt).ToUnixTimeSeconds();

                    supporterString.AppendLine($"Started <t:{startsAtValue}:f> - Ends on <t:{endsAtValue}:D>");
                }
                else
                {
                    supporterString.AppendLine($"Started <t:{startsAtValue}:f> - Ends on unknown>");
                }


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

    public async Task<ResponseModel> BuildHelpResponse(
        IReadOnlyList<ICommandInfo<CommandContext>> allCommands,
        string prefix,
        CommandCategory? selectedCategory,
        string selectedCommand,
        string userName,
        ulong userId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        ICommandInfo<CommandContext> foundCommand = null;

        if (!string.IsNullOrEmpty(selectedCommand))
        {
            foundCommand = HelpFindCommand(allCommands, selectedCommand);
            if (foundCommand != null && !selectedCategory.HasValue)
            {
                var categories = HelpGetAllAttributes(foundCommand)
                    .OfType<CommandCategoriesAttribute>()
                    .SelectMany(s => s.Categories)
                    .ToList();
                if (categories.Count > 0)
                {
                    selectedCategory = categories.First();
                }
            }
        }

        var categoryMenu = new StringMenuProperties(InteractionConstants.Help.CategoryMenu)
        {
            Placeholder = "Select a category"
        };

        foreach (var category in Enum.GetValues<CommandCategory>())
        {
            var description = StringExtensions.CommandCategoryToString(category);
            categoryMenu.Add(new StringMenuSelectOptionProperties(category.ToString(), category.ToString())
            {
                Description = description?.ToLower() != category.ToString().ToLower() ? description : null,
                Default = selectedCategory == category || (selectedCategory == null && category == CommandCategory.General)
            });
        }

        if (selectedCategory.HasValue && selectedCategory.Value != CommandCategory.General)
        {
            var categoryCommands = allCommands.Where(w =>
                HelpGetAllAttributes(w).OfType<CommandCategoriesAttribute>().Select(s => s.Categories)
                    .Any(a => a.Contains(selectedCategory.Value))).ToList();

            if (foundCommand != null)
            {
                var helpResponse = GenericEmbedService.HelpResponse(response.Embed, foundCommand, prefix, userName);
                var userSettings = await this._userService.GetUserAsync(userId);
                var isSupporter = userSettings != null && SupporterService.IsSupporter(userSettings.UserType);
                if (helpResponse.showPurchaseButtons && !isSupporter)
                {
                    response.Components = GenericEmbedService.PurchaseButtons(foundCommand);
                }
            }
            else
            {
                var commands = new StringBuilder();
                commands.AppendLine("**Commands:** \n");

                var usedCommands = new HashSet<string>();
                foreach (var command in categoryCommands)
                {
                    var cmdName = HelpGetCommandName(command);
                    if (usedCommands.Add(cmdName))
                    {
                        commands.AppendLine(HelpCommandInfoToHelpString(prefix, command));
                    }
                }

                if (selectedCategory == CommandCategory.Importing)
                {
                    commands.AppendLine();
                    commands.AppendLine("**Slash commands:**");
                    commands.AppendLine("**`/import spotify`** | *Starts your Spotify import*");
                    commands.AppendLine("**`/import applemusic`** | *Starts your Apple Music import*");
                    commands.AppendLine("**`/import manage`** | *Manage and configure your existing imports*");
                    commands.AppendLine("**`/import modify`** | *Modify your existing imports*");
                }

                response.Embed.WithTitle($"Overview of all {selectedCategory.Value} commands");
                response.Embed.WithDescription(commands.ToString());
            }

            var commandMenu = new StringMenuProperties(InteractionConstants.Help.CommandMenu)
            {
                Placeholder = "Select a command for details"
            };

            var addedCommands = new HashSet<string>();
            foreach (var command in categoryCommands)
            {
                var cmdName = HelpGetCommandName(command);
                if (addedCommands.Add(cmdName))
                {
                    commandMenu.Add(new StringMenuSelectOptionProperties(cmdName, cmdName)
                    {
                        Default = cmdName.Equals(selectedCommand, StringComparison.OrdinalIgnoreCase)
                    });
                }
                if (addedCommands.Count >= 25)
                {
                    break;
                }
            }

            response.StringMenus.Add(categoryMenu);
            if (addedCommands.Count > 0)
            {
                response.StringMenus.Add(commandMenu);
            }
        }
        else
        {
            await SetGeneralHelpEmbed(response, prefix, userId);
            response.StringMenus.Add(categoryMenu);
        }

        return response;
    }

    private async Task SetGeneralHelpEmbed(ResponseModel response, string prefix, ulong userId)
    {
        response.EmbedAuthor.WithName(".fmbot help & command overview");
        response.Embed.WithAuthor(response.EmbedAuthor);

        var description = new StringBuilder();
        var footer = new StringBuilder();

        description.AppendLine($"**Main command `{prefix}fm`**");
        description.AppendLine("*Displays last scrobbles, and looks different depending on the mode you've set.*");

        description.AppendLine();

        var contextUser = await this._userService.GetUserAsync(userId);
        if (contextUser == null)
        {
            description.AppendLine("**Connecting a Last.fm account**");
            description.AppendLine(
                $"To use .fmbot, you have to connect a Last.fm account. Last.fm is a website that tracks what music you listen to. Get started with `{prefix}login`.");
        }
        else
        {
            description.AppendLine("**Customizing .fmbot**");
            description.AppendLine($"- User settings: `{prefix}settings`");
            description.AppendLine($"- Server config: `{prefix}configuration`");

            footer.AppendLine($"Logged in to .fmbot with the Last.fm account '{contextUser.UserNameLastFM}'");
        }

        description.AppendLine();

        description.AppendLine("**Commands**");
        description.AppendLine("- View all commands on [our website](https://fm.bot/commands/)");
        description.AppendLine("- Or use the dropdown below this message to pick a category");

        description.AppendLine();
        description.AppendLine("**Links**");
        description.Append("[Website](https://fm.bot/) - ");
        description.Append($"[Get Supporter]({Constants.GetSupporterDiscordLink})");
        description.Append(" - [Support server](https://discord.gg/6y3jJjtDqK)");

        if (PublicProperties.IssuesAtLastFm)
        {
            var issues = "";
            if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
            {
                issues = "\n\n" +
                         "Note:\n" +
                         $"*\"{PublicProperties.IssuesReason}\"*";
            }

            response.Embed.AddField("Note:",
                "⚠️ [Last.fm](https://twitter.com/lastfmstatus) is currently experiencing issues.\n" +
                $".fmbot is not affiliated with Last.fm.{issues}");
        }

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithFooter(new EmbedFooterProperties { Text = footer.ToString() });
    }

    private static ICommandInfo<CommandContext> HelpFindCommand(IReadOnlyList<ICommandInfo<CommandContext>> commands, string search)
    {
        search = search.ToLower().Trim();
        return commands.FirstOrDefault(c =>
        {
            var attr = HelpGetAllAttributes(c).OfType<CommandAttribute>().FirstOrDefault();
            return attr != null && attr.Aliases.Any(a => a.Equals(search, StringComparison.OrdinalIgnoreCase));
        });
    }

    private static string HelpGetCommandName(ICommandInfo<CommandContext> commandInfo)
    {
        var nameAttr = HelpGetAllAttributes(commandInfo).OfType<CommandAttribute>().FirstOrDefault();
        if (nameAttr != null && nameAttr.Aliases.Length > 0)
        {
            return nameAttr.Aliases[0];
        }
        return commandInfo.ToString() ?? "unknown";
    }

    private static string HelpCommandInfoToHelpString(string prefix, ICommandInfo<CommandContext> commandInfo)
    {
        var nameAttr = HelpGetAllAttributes(commandInfo).OfType<CommandAttribute>().FirstOrDefault();
        var summaryAttr = HelpGetAllAttributes(commandInfo).OfType<SummaryAttribute>().FirstOrDefault();

        var name = nameAttr?.Aliases.FirstOrDefault() ?? "unknown";
        var summary = summaryAttr?.Summary ?? "";

        return $"**`{prefix}{name}`** | *{summary}*";
    }

    private static IEnumerable<Attribute> HelpGetAllAttributes(ICommandInfo<CommandContext> commandInfo)
    {
        return commandInfo.Attributes.Values.SelectMany(x => x);
    }
}
