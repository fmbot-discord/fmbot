using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.Commands;

namespace FMBot.Bot.TextCommands.Guild;

[ModuleName("Webhooks")]
[ServerStaffOnly]
public class WebhookCommands(
    WebhookService webhookService,
    GuildService guildService,
    IOptions<BotSettings> botSettings,
    GuildSettingBuilder guildSettingBuilder,
    IPrefixService prefixService,
    ShardedGatewayClient client)
    : BaseCommandModule(botSettings)
{
    [Command("addwebhook", "addfeaturedwebhook")]
    [Summary("Adds featured webhook to a channel. This will automatically post all .fmbot features to this channel.\n\n" +
             "To remove, simply delete the webhook from your server and .fmbot will automatically delete it next time it tries to post a feature.")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task AddFeaturedWebhookAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var permissions = await GuildService.GetGuildPermissionsAsync(this.Context);
        if (!permissions.HasFlag(Permissions.ManageWebhooks))
        {
            var currentUser = client.GetCurrentUser();
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = $"In order to create the featured webhook, I need permission to add webhooks.\n\nYou can add this permission by going to `Server Settings` > `Roles` > `{currentUser?.Username}` and enabling the `Manage Webhooks` permission." });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var guild = await guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "This server already has a webhook configured.\n\nYou can change the channel in the webhook settings (`Server settings` > `Integrations` > `Webhooks`)\n\nIf you recently deleted the webhook and want to make a new one, please run `.testwebhook` once to remove the deleted webhook from our database." });
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var createdWebhook = await webhookService.CreateWebhook(this.Context, guild.GuildId);

        await webhookService.TestWebhook(createdWebhook, "If you see this message the webhook has been successfully added!\n\n" +
                                                               "You will now automatically receive the .fmbot featured message every hour.\n\n" +
                                                               "To disable this, simply delete the webhook in your servers integration settings.");
        this.Context.LogCommandUsed();
    }

    [Command("testwebhook", "testfeatured", "testfeaturedwebhook")]
    [Summary("Test the .fmbot webhook in your channel")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task TestFeaturedWebhookAsync()
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var guild = await guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            var webhook = guild.Webhooks.First(f => f.BotType == botType);
            var successful = await webhookService.TestWebhook(webhook, "If you see this message, then the webhook works!\n\n" +
                                                                             "You will automatically receive the .fmbot featured message every hour.");

            if (!successful)
            {
                await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "The previously registered webhook has been removed from our database.\n\nYou can now add a new webhook for .fmbot with `.addwebhook`." });
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "You don't have any webhooks added yet.\n\nAdd a webhook for .fmbot with `.addwebhook`" });
            this.Context.LogCommandUsed();
        }
    }
}
