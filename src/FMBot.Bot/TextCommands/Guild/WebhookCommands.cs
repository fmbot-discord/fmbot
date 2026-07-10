using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
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
    UserService userService,
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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var permissions = await GuildService.GetChannelPermissionsAsync(this.Context);
        if (!permissions.HasFlag(Permissions.ManageWebhooks))
        {
            var currentUser = client.GetCurrentUser();
            this._embed.WithTitle("Missing permission");
            this._embed.WithDescription("In order to create the featured webhook, I need permission to add webhooks in this channel.\n\n" +
                                        $"You can add this permission by going to `Server Settings` > `Roles` > `{currentUser?.Username}` and enabling the `Manage Webhooks` permission, or by allowing it for this channel specifically.");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var guild = await guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            this._embed.WithTitle("Webhook already configured");
            this._embed.WithDescription("This server already has a featured webhook.\n\n" +
                                        "You can change the channel in the webhook settings (`Server Settings` > `Integrations` > `Webhooks`).\n\n" +
                                        "If you recently deleted the webhook and want to make a new one, please run `.testwebhook` once to remove the deleted webhook from our database.");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var createdWebhook = await webhookService.CreateWebhook(this.Context, guild.GuildId);

        await webhookService.TestWebhook(createdWebhook, "If you see this message the webhook has been successfully added!", this.Context.Client.Rest);

        this._embed.WithTitle("Featured webhook added");
        var description = new StringBuilder();
        description.AppendLine("You will now automatically receive the .fmbot featured message in this channel every hour.");
        description.AppendLine();
        description.AppendLine("To disable this, simply delete the webhook in your server's integration settings (`Server Settings` > `Integrations` > `Webhooks`).");
        this._embed.WithColor(DiscordConstants.SuccessColorGreen);

        var missingReactionPermissions = GetMissingReactionPermissionsText(permissions, guild.EmoteReactions);
        if (missingReactionPermissions != null)
        {
            description.AppendLine();
            description.AppendLine(missingReactionPermissions);
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        this._embed.WithDescription(description.ToString());
        await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });

        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
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
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var guild = await guildService.GetGuildWithWebhooks(this.Context.Guild.Id);
        var botType = this.Context.GetBotType();

        if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
        {
            var webhook = guild.Webhooks.First(f => f.BotType == botType);
            var successful = await webhookService.TestWebhook(webhook, "If you see this message, then the webhook works!\n\n" +
                                                                             "You will automatically receive the .fmbot featured message every hour.", this.Context.Client.Rest);

            if (!successful)
            {
                this._embed.WithTitle("Webhook removed");
                this._embed.WithDescription("The previously registered webhook has been removed from our database.\n\n" +
                                            "You can now add a new webhook for .fmbot with `.addwebhook`.");
                this._embed.WithColor(DiscordConstants.SuccessColorGreen);
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            }
            else
            {
                var permissions = await GuildService.GetChannelPermissionsAsync(this.Context);
                var missingReactionPermissions = GetMissingReactionPermissionsText(permissions, guild.EmoteReactions);
                if (missingReactionPermissions != null)
                {
                    this._embed.WithDescription(missingReactionPermissions);
                    this._embed.WithColor(DiscordConstants.WarningColorOrange);
                    await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
                }
            }

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            this._embed.WithDescription("You don't have any webhooks added yet.\n\n" +
                                        "Add a webhook for .fmbot with `.addwebhook`");
            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
    }

    private static string GetMissingReactionPermissionsText(Permissions permissions, string[] emoteReactions)
    {
        if (emoteReactions == null || !emoteReactions.Any())
        {
            return null;
        }

        var missing = new List<string>();
        if (!permissions.HasFlag(Permissions.AddReactions))
        {
            missing.Add("`Add Reactions`");
        }
        if (!permissions.HasFlag(Permissions.ReadMessageHistory))
        {
            missing.Add("`Read Message History`");
        }

        if (!missing.Any())
        {
            return null;
        }

        return $"⚠️ This server has emote reactions configured, but I'm missing the {string.Join(" and ", missing)} permission{(missing.Count > 1 ? "s" : "")} in this channel.\n\n" +
               "Featured messages will still be posted, but I won't be able to add the server reactions to them.";
    }
}
