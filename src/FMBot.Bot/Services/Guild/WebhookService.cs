using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Shared.Domain.Enums;
using SkiaSharp;

namespace FMBot.Bot.Services.Guild;

public class WebhookService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly string _avatarImagePath;
    private readonly BotSettings _botSettings;
    private readonly GuildService _guildService;
    private readonly OpenAiService _openAiService;
    private readonly HttpClient _httpClient;

    public WebhookService(IDbContextFactory<FMBotDbContext> contextFactory, IOptions<BotSettings> botSettings, GuildService guildService, OpenAiService openAiService, HttpClient httpClient)
    {
        this._contextFactory = contextFactory;
        this._guildService = guildService;
        this._openAiService = openAiService;
        this._httpClient = httpClient;
        this._botSettings = botSettings.Value;

        this._avatarImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "default-avatar.png");

        if (!File.Exists(this._avatarImagePath))
        {
            Log.Information("Downloading avatar...");
            var wc = new System.Net.WebClient();
            wc.DownloadFile("https://fm.bot/img/bot/avatar.png", this._avatarImagePath);
        }
    }

    public async Task<Webhook> CreateWebhook(ICommandContext context, int guildId)
    {
        await using var fs = File.OpenRead(this._avatarImagePath);

        var botType = context.GetBotType();
        var botTypeName = botType == BotType.Production ? "" : botType == BotType.Beta ? " develop" : " local";

        var webhook = new Webhook
        {
            GuildId = guildId,
            BotType = botType,
            Created = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
        };

        if (context.Channel.GetChannelType() != ChannelType.PublicThread)
        {
            var socketTextChannel = context.Channel as SocketTextChannel;

            var newWebhook = await socketTextChannel.CreateWebhookAsync($".fmbot{botTypeName} featured", fs,
                new RequestOptions { AuditLogReason = "Created webhook for .fmbot featured feed." });

            webhook.DiscordWebhookId = newWebhook.Id;
            webhook.Token = newWebhook.Token;
        }
        else
        {
            var socketThreadChannel = context.Channel as SocketThreadChannel;

            var parentChannel = socketThreadChannel.ParentChannel as SocketTextChannel;

            var newWebhook = await parentChannel.CreateWebhookAsync($".fmbot{botTypeName} featured", fs,
            new RequestOptions { AuditLogReason = "Created webhook for .fmbot featured feed." });

            webhook.DiscordWebhookId = newWebhook.Id;
            webhook.Token = newWebhook.Token;
            webhook.DiscordThreadId = context.Channel.Id;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.Webhooks.AddAsync(webhook);
        await db.SaveChangesAsync();

        Log.Information("Created webhook for guild {guildId}", guildId);

        return webhook;
    }

    public async Task<bool> TestWebhook(Webhook webhook, string text)
    {
        var webhookClient = new DiscordWebhookClient(webhook.DiscordWebhookId, webhook.Token);

        try
        {
            await webhookClient.SendMessageAsync(text, threadId: webhook.DiscordThreadId);

            return true;
        }
        catch (Exception e)
        {
            if (e.Message.Contains("Could not find"))
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

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
        finally
        {
            webhookClient.Dispose();
        }
    }

    public async Task SendFeaturedWebhooks(FeaturedLog featured)
    {
        var embed = new EmbedBuilder();
        embed.WithThumbnailUrl(featured.ImageUrl);
        embed.AddField("Featured:", featured.Description);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var webhooks = await db.Webhooks
            .AsQueryable()
            .ToListAsync();

        var tasks = new List<Task>();
        foreach (var webhook in webhooks)
        {
            tasks.Add(SendWebhookEmbed(webhook, embed, featured.UserId));
        }

        Log.Information("SendFeaturedWebhooks: Sending {webhookCount} featured webhooks", webhooks.Count);
        await Task.WhenAll(tasks);
    }

    public async Task SendFeaturedPreview(FeaturedLog featured, string webhook)
    {
        var embed = new EmbedBuilder();
        embed.WithImageUrl(featured.ImageUrl);
        embed.AddField("Featured:", featured.Description);

        var dateValue = ((DateTimeOffset)featured.DateTime).ToUnixTimeSeconds();
        embed.AddField("Time", $"<t:{dateValue}:F>");
        embed.AddField("Resetting", $"`.resetfeatured {featured.FeaturedLogId}`");

        embed.WithFooter(featured.ImageUrl);

        if (featured.UserId.HasValue)
        {
            await using var db = await this._contextFactory.CreateDbContextAsync();
            var user = await db.Users.FindAsync(featured.UserId.Value);

            Log.Information("Featured: Checking is username is offensive");

            var possiblyOffensive = await this._openAiService.CheckIfUsernameOffensive(user?.UserNameLastFM);

            if (possiblyOffensive)
            {
                embed.AddField("⚠️ Warning",
                    "Possibly offensive username detected");
                embed.WithColor(DiscordConstants.WarningColorOrange);
            }
        }

        var webhookClient = new DiscordWebhookClient(webhook);
        await webhookClient.SendMessageAsync(embeds: new[] { embed.Build() });
        webhookClient.Dispose();
    }

    public async Task PostFeatured(FeaturedLog featuredLog, DiscordShardedClient client)
    {
        var builder = new EmbedBuilder();
        if (featuredLog.FullSizeImage == null)
        {
            builder.WithThumbnailUrl(featuredLog.ImageUrl);
        }
        else
        {
            builder.WithImageUrl(featuredLog.FullSizeImage);
        }

        builder.AddField("Featured:", featuredLog.Description);

        if (this._botSettings.Bot.BaseServerId != 0 && this._botSettings.Bot.FeaturedChannelId != 0)
        {
            var guild = client.GetGuild(this._botSettings.Bot.BaseServerId);
            var channel = guild?.GetTextChannel(this._botSettings.Bot.FeaturedChannelId);

            if (channel != null)
            {
                if (featuredLog.UserId.HasValue)
                {
                    await using var db = await this._contextFactory.CreateDbContextAsync();
                    var dbGuild = await db.Guilds
                        .AsQueryable()
                        .Include(i => i.GuildUsers)
                        .ThenInclude(i => i.User)
                        .FirstOrDefaultAsync(f => f.DiscordGuildId == this._botSettings.Bot.BaseServerId);

                    if (dbGuild?.GuildUsers != null && dbGuild.GuildUsers.Any())
                    {
                        var guildUser = dbGuild.GuildUsers.FirstOrDefault(f => f.UserId == featuredLog.UserId);

                        if (guildUser != null)
                        {
                            var localFeaturedMsg = await channel.SendMessageAsync($"🥳 Congratulations <@{guildUser.User.DiscordUserId}>! You've just been picked as the featured user for the next hour.",
                                false,
                                builder.Build());

                            if (localFeaturedMsg != null)
                            {
                                await this._guildService.AddGuildReactionsAsync(localFeaturedMsg, guild, true);
                            }

                            return;
                        }
                    }
                }

                var message = await channel.SendMessageAsync("", false, builder.Build());

                if (message != null)
                {
                    if (featuredLog.Reactions != null && featuredLog.Reactions.Any())
                    {
                        await GuildService.AddReactionsAsync(message, featuredLog.Reactions);
                    }
                    else
                    {
                        await this._guildService.AddGuildReactionsAsync(message, guild);
                    }
                }
            }
        }
        else
        {
            Log.Warning("Featured channel not set, not sending featured message");
        }
    }

    private async Task SendWebhookEmbed(Webhook webhook, EmbedBuilder embed, int? featuredUserId)
    {
        var webhookClient = new DiscordWebhookClient(webhook.DiscordWebhookId, webhook.Token);

        try
        {
            if (featuredUserId.HasValue)
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();
                var guild = await db.Guilds
                    .AsQueryable()
                    .Include(i => i.GuildUsers)
                    .FirstOrDefaultAsync(f => f.GuildId == webhook.GuildId);

                if (guild?.GuildUsers != null && guild.GuildUsers.Any())
                {
                    var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == featuredUserId);

                    if (guildUser != null)
                    {
                        embed.WithFooter(
                            $"🥳 Congratulations! This user is in your server under the name {guildUser.UserName}.");
                    }
                }
            }

            await webhookClient.SendMessageAsync(embeds: new[] { embed.Build() }, threadId: webhook.DiscordThreadId);
        }
        catch (Exception e)
        {
            if (e.Message.Contains("Could not find"))
            {
                await using var db = await this._contextFactory.CreateDbContextAsync();

                db.Webhooks.Remove(webhook);
                await db.SaveChangesAsync();

                Log.Information("Removed webhook from database for guild {guildId}", webhook.GuildId);
            }
            else
            {
                Log.Error(e, "Unknown error while testing webhook for {guildId}", webhook.GuildId);
            }
        }
        finally
        {
            webhookClient.Dispose();
        }
    }

    public async Task ChangeToNewAvatar(DiscordShardedClient client, string imageUrl)
    {
        Log.Information($"ChangeToNewAvatar: Updating avatar to {imageUrl}");

        if (imageUrl.Contains("lastfm.freetls.fastly.net"))
        {
            imageUrl = imageUrl.Replace(".jpg", ".webp").Replace(".png", ".webp");
        }

        try
        {
            using (var response = await this._httpClient.GetAsync(imageUrl))
            {
                response.EnsureSuccessStatusCode();
                Log.Information("ChangeToNewAvatar: Got new avatar in stream");

                var contentType = response.Content.Headers.ContentType?.MediaType;
                Log.Information($"ChangeToNewAvatar: Content-Type: {contentType}");

                var imageData = await response.Content.ReadAsByteArrayAsync();

                if (contentType?.ToLower() == "image/webp")
                {
                    imageData = ConvertWebPToPng(imageData);
                    Log.Information("ChangeToNewAvatar: Converted WebP to PNG");
                }

                using (var imageStream = new MemoryStream(imageData))
                {
                    await client.CurrentUser.ModifyAsync(u => u.Avatar = new Discord.Image(imageStream));
                    Log.Information("ChangeToNewAvatar: Avatar successfully changed");
                }
            }

            await Task.Delay(3000);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "ChangeToNewAvatar: Error while attempting to change avatar: {ErrorMessage}", exception.Message);
        }
    }

    private static byte[] ConvertWebPToPng(byte[] webpData)
    {
        using var inputStream = new MemoryStream(webpData);
        using var outputStream = new MemoryStream();
        using (var codec = SKCodec.Create(inputStream))
        using (var surface = SKSurface.Create(new SKImageInfo(codec.Info.Width, codec.Info.Height)))
        {
            var canvas = surface.Canvas;
            using (var image = SKImage.FromEncodedData(webpData))
            {
                canvas.DrawImage(image, 0, 0);
            }

            using (var image = surface.Snapshot())
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                data.SaveTo(outputStream);
            }
        }
        return outputStream.ToArray();
    }
}
