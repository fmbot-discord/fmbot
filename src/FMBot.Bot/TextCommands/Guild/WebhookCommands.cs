using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands.Guild;

[Name("Webhooks")]
[ServerStaffOnly]
public class WebhookCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly WebhookService _webhookService;
    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly IPrefixService _prefixService;

    public WebhookCommands(
        WebhookService webhookService,
        GuildService guildService,
        IOptions<BotSettings> botSettings,
        GuildSettingBuilder guildSettingBuilder,
        IPrefixService prefixService) : base(botSettings)
    {
        this._webhookService = webhookService;
        this._guildService = guildService;
        this._guildSettingBuilder = guildSettingBuilder;
        this._prefixService = prefixService;
    }

    [Command("addwebhook", RunMode = RunMode.Async)]
    [Summary("Adds featured webhook to a channel. This will automatically post all .fmbot features to this channel.\n\n" +
             "To remove, simply delete the webhook from your server and .fmbot will automatically delete it next time it tries to post a feature.")]
    [Alias("addfeaturedwebhook")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task AddFeaturedWebhookAsync()
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var socketCommandContext = (SocketCommandContext)this.Context;
        var user = await this.Context.Guild.GetUserAsync(socketCommandContext.Client.CurrentUser.Id);
        if (!user.GuildPermissions.ManageWebhooks)
        {
            await ReplyAsync(
                "In order to create the featured webhook, I need permission to add webhooks.\n\n" +
                $"You can add this permission by going to `Server Settings` > `Roles` > `{socketCommandContext.Client.CurrentUser.Username}` and enabling the `Manage Webhooks` permission.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var guild = await this._guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            await ReplyAsync(
                "This server already has a webhook configured.\n\n" +
                "You can change the channel in the webhook settings (`Server settings` > `Integrations` > `Webhooks`)\n\n" +
                "If you recently deleted the webhook and want to make a new one, please run `.testwebhook` once to remove the deleted webhook from our database.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var type = socketCommandContext.Channel.GetChannelType();

        var createdWebhook = await this._webhookService.CreateWebhook(this.Context, guild.GuildId);

        await this._webhookService.TestWebhook(createdWebhook, "If you see this message the webhook has been successfully added!\n\n" +
                                                               "You will now automatically receive the .fmbot featured message every hour.\n\n" +
                                                               "To disable this, simply delete the webhook in your servers integration settings.");
        this.Context.LogCommandUsed();
    }

    [Command("testwebhook", RunMode = RunMode.Async)]
    [Summary("Test the .fmbot webhook in your channel")]
    [Alias("testfeatured", "testfeaturedwebhook")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task TestFeaturedWebhookAsync()
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        var guild = await this._guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            var webhook = guild.Webhooks.First(f => f.BotType == botType);
            var successful = await this._webhookService.TestWebhook(webhook, "If you see this message, then the webhook works!\n\n" +
                                                                             "You will automatically receive the .fmbot featured message every hour.");

            if (!successful)
            {
                await ReplyAsync("The previously registered webhook has been removed from our database.\n\n" +
                                 "You can now add a new webhook for .fmbot with `.addwebhook`.");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("You don't have any webhooks added yet.\n\n" +
                             "Add a webhook for .fmbot with `.addwebhook`");
            this.Context.LogCommandUsed();
        }
    }
}
