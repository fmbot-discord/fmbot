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
using FMBot.Domain.Models;

namespace FMBot.Bot.Builders;

public class StaticBuilders
{
    private readonly SupporterService _supporterService;

    public StaticBuilders(SupporterService supporterService)
    {
        this._supporterService = supporterService;
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

        embedDescription.Append(".fmbot is non-commercial, open-source and non-profit. It is maintained by volunteers.");
        embedDescription.AppendLine("You can help us fund hosting, development and other costs on our [OpenCollective](https://opencollective.com/fmbot).");
        embedDescription.AppendLine();
        embedDescription.AppendLine("We use OpenCollective so we can be transparent about our expenses. If you decide to donate, you can see exactly where your money goes.");
        embedDescription.AppendLine();
        embedDescription.AppendLine($"Use `{context.Prefix}supporters` to see everyone who has supported us so far!");
        embedDescription.AppendLine();
        embedDescription.AppendLine("**.fmbot supporter advantages include**:\n" +
                                    "- Extra statistics in some commands\n" +
                                    "- An emote behind their name (⭐)\n" +
                                    "- Higher chance of being featured on Supporter Sunday\n" +
                                    "- Their name shown in the list of supporters\n" +
                                    "- Exclusive role and channel on [our server](https://discord.gg/6y3jJjtDqK)\n" +
                                    "- A chance of sponsoring a chart\n" +
                                    "- Friend limit increased to 16 (up from 12)\n" +
                                    "- WhoKnows tracking increased to all your music (instead of top 4/5/6k artist/albums/tracks)");

        var existingSupporter = await this._supporterService.GetSupporter(context.DiscordUser.Id);
        if (existingSupporter != null)
        {
            var existingSupporterDescription = new StringBuilder();

            var created = DateTime.SpecifyKind(existingSupporter.Created, DateTimeKind.Utc);
            var createdValue = ((DateTimeOffset)created).ToUnixTimeSeconds();
            existingSupporterDescription.AppendLine($"Supporter added: <t:{createdValue}:D>");

            if (existingSupporter.LastPayment.HasValue)
            {
                var lastPayment = DateTime.SpecifyKind(existingSupporter.LastPayment.Value, DateTimeKind.Utc);
                var lastPaymentValue = ((DateTimeOffset)created).ToUnixTimeSeconds();
                existingSupporterDescription.AppendLine($"Last payment: <t:{lastPaymentValue}:D>");
            }

            if (existingSupporter.SubscriptionType.HasValue)
            {
                existingSupporterDescription.AppendLine($"Subscription type: {Enum.GetName(existingSupporter.SubscriptionType.Value)}");
            }

            existingSupporterDescription.AppendLine($"Name: **{Format.Sanitize(existingSupporter.Name)}** (from OpenCollective)");

            response.Embed.AddField("Thank you for being a supporter!", existingSupporterDescription.ToString());
        }

        response.Embed.WithDescription(embedDescription.ToString());

        response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterLink);

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

                supporterString.AppendLine($" - **{supporter.Name}** {type}");
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

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var existingSupporters = await this._supporterService.GetAllVisibleSupporters();

        var supporters = await this._supporterService.GetOpenCollectiveSupporters();

        var supporterLists = supporters.Users.OrderByDescending(o => o.CreatedAt).Chunk(10);

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

                var created = DateTime.SpecifyKind(supporter.CreatedAt, DateTimeKind.Utc);
                var createdValue = ((DateTimeOffset)lastPayment).ToUnixTimeSeconds();

                supporterString.AppendLine($"Created: <t:{createdValue}:D> - Last payment: <t:{lastPaymentValue}:D>");

                var existingSupporter = existingSupporters.FirstOrDefault(f => f.OpenCollectiveId == supporter.Id);
                if (existingSupporter != null)
                {
                    supporterString.AppendLine($"✅ Claimed - {existingSupporter.DiscordUserId} - <@{existingSupporter.DiscordUserId}>");
                }

                supporterString.AppendLine();
            }

            pages.Add(new PageBuilder()
                .WithDescription(supporterString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter($"OC: {supporters.Users.Count} - db: {existingSupporters.Count}\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Monthly && c.LastPayment >= DateTime.Now.AddDays(-35))} active monthly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Monthly)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Yearly && c.LastPayment >= DateTime.Now.AddDays(-370))} active yearly ({supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Yearly)} total)\n" +
                            $"{supporters.Users.Count(c => c.SubscriptionType == SubscriptionType.Lifetime)} lifetime")
                .WithTitle(".fmbot opencollective supporters overview"));
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);

        return response;
    }
}
