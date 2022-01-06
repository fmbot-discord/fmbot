using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Commands.Guild
{
    [Name("Webhooks")]
    [ServerStaffOnly]
    public class WebhookCommands : BaseCommandModule
    {
        private readonly AdminService _adminService;
        private readonly GuildService _guildService;
        private readonly WebhookService _webhookService;

        public WebhookCommands(
            AdminService adminService,
            WebhookService webhookService,
            GuildService guildService,
            IOptions<BotSettings> botSettings) : base(botSettings)
        {
            this._adminService = adminService;
            this._webhookService = webhookService;
            this._guildService = guildService;
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
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var socketCommandContext = (SocketCommandContext)this.Context;
            var user = await this.Context.Guild.GetUserAsync(socketCommandContext.Client.CurrentUser.Id);
            if (!user.GuildPermissions.ManageWebhooks)
            {
                await ReplyAsync(
                    "In order to create the featured webhook, I need permission to add webhooks.\n" +
                    $"You can add this permission by going to `Server Settings` > `Roles` > `{socketCommandContext.Client.CurrentUser.Username}` and enabling the `Manage Webhooks` permission.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id, enableCache: false);
            var botType = this.Context.GetBotType();

            if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
            {
                await ReplyAsync(
                    "This server already has a webhook configured.\n" +
                    "You can change the channel in the webhook settings (`Server settings` > `Integrations` > `Webhooks`)\n" +
                    "If you recently deleted the webhook and want to make a new one, please run `.fmtestwebhook` once to remove the deleted webhook from our database.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var createdWebhook = await this._webhookService.CreateWebhook(this.Context, guild.GuildId);

            await this._webhookService.TestWebhook(createdWebhook, "If you see this message the webhook has been successfully added!\n" +
                                                                   "You will now automatically receive the .fmbot featured message every hour.\n" +
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
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetFullGuildAsync(this.Context.Guild.Id, enableCache: false);
            var botType = this.Context.GetBotType();

            if (guild.Webhooks != null && guild.Webhooks.Any(a => a.BotType == botType))
            {
                var webhook = guild.Webhooks.First(f => f.BotType == botType);
                var successful = await this._webhookService.TestWebhook(webhook, "If you see this message, then the webhook works!\n" +
                                                                                "You will automatically receive the .fmbot featured message every hour.");

                if (!successful)
                {
                    await ReplyAsync("The previously registered webhook has been removed from our database.\n" +
                                     "You can now add a new webhook for .fmbot with `.fmaddwebhook`.");
                }
                
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("You don't have any webhooks added yet.\n" +
                                 "Add a webhook for .fmbot with `.fmaddwebhook`");
                this.Context.LogCommandUsed();
            }
        }
    }
}
