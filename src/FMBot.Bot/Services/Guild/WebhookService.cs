using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services.Guild
{
    public class WebhookService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly string _avatarImagePath;

        public WebhookService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;

            this._avatarImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default-avatar.png");

            if (!File.Exists(this._avatarImagePath))
            {
                Log.Information("Downloading avatar...");
                var wc = new System.Net.WebClient();
                wc.DownloadFile("https://fmbot.xyz/img/bot/avatar.png", this._avatarImagePath);
            }
        }

        public async Task<Webhook> CreateWebhook(ICommandContext context, int guildId)
        {
            await using var fs = File.OpenRead(this._avatarImagePath);

            var socketWebChannel = context.Channel as SocketTextChannel;

            if (socketWebChannel == null)
            {
                return null;
            }

            var botType = context.GetBotType();

            var botTypeName = botType == BotType.Production ? "" : botType == BotType.Develop ? " develop" : " local";
            var newWebhook = await socketWebChannel.CreateWebhookAsync($".fmbot{botTypeName} featured", fs,
                new RequestOptions { AuditLogReason = "Created webhook for .fmbot featured feed." });

            await using var db = this._contextFactory.CreateDbContext();
            var webhook = new Webhook
            {
                GuildId = guildId,
                BotType = botType,
                Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                DiscordWebhookId = newWebhook.Id,
                Token = newWebhook.Token
            };

            await db.Webhooks.AddAsync(webhook);
            await db.SaveChangesAsync();

            Log.Information("Created webhook for guild {guildId}", guildId);

            return webhook;
        }

        public async Task<bool> TestWebhook(Webhook webhook, string text)
        {
            try
            {
                var webhookClient = new DiscordWebhookClient(webhook.DiscordWebhookId, webhook.Token);

                await webhookClient.SendMessageAsync(text);

                return true;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Could not find"))
                {
                    await using var db = this._contextFactory.CreateDbContext();

                    db.Webhooks.Remove(webhook);
                    await db.SaveChangesAsync();

                    Log.Information("Removed webhook from database for guild {guildId}", webhook.GuildId);
                }
                else
                {
                    Log.Error(e, "Unknown error while testing webhook for {guildId}", webhook.GuildId);
                }

                return false;
            }
        }

        public async Task SendFeaturedWebhooks(BotType botType, string trackString, int featuredUserId, string imageUrl)
        {
            var embed = new EmbedBuilder();
            embed.WithThumbnailUrl(imageUrl);
            embed.AddField("Featured:", trackString);

            await using var db = this._contextFactory.CreateDbContext();
            var webhooks = await db.Webhooks
                .AsQueryable()
                .Where(w => w.BotType == botType)
                .ToListAsync();

            foreach (var webhook in webhooks)
            {
                await SendWebhookEmbed(webhook, embed, featuredUserId);
            }
        }

        private async Task<bool> SendWebhookEmbed(Webhook webhook, EmbedBuilder embed, int featuredUserId)
        {
            try
            {
                var webhookClient = new DiscordWebhookClient(webhook.DiscordWebhookId, webhook.Token);

                await using var db = this._contextFactory.CreateDbContext();
                var guild = await db.Guilds
                    .AsQueryable()
                    .Include(i => i.GuildUsers)
                    .FirstOrDefaultAsync(f => f.GuildId == webhook.GuildId);

                if (guild?.GuildUsers != null && guild.GuildUsers.Any())
                {
                    var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == featuredUserId);

                    if (guildUser != null)
                    {
                        embed.WithFooter($"🥳 Congratulations! This user is in your server under the name {guildUser.UserName}.");
                    }
                }

                await webhookClient.SendMessageAsync(embeds: new[] { embed.Build() });

                return true;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("Could not find"))
                {
                    await using var db = this._contextFactory.CreateDbContext();

                    db.Webhooks.Remove(webhook);
                    await db.SaveChangesAsync();

                    Log.Information("Removed webhook from database for guild {guildId}", webhook.GuildId);
                }
                else
                {
                    Log.Error(e, "Unknown error while testing webhook for {guildId}", webhook.GuildId);
                }

                return false;
            }
        }
    }
}
