using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using Serilog;
using Shared.Domain.Enums;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class PremiumSettingBuilder(
    GuildService guildService,
    SupporterService supporterService,
    ShortcutService shortcutService,
    CensorService censorService,
    CommandService<CommandContext> commands,
    HttpClient httpClient)
{
    public async Task<ResponseModel> PremiumServerOverview(ContextModel context, string userLocale,
        string source = "premiumserver")
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        response.Embed.WithTitle("✨ Premium server");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var description = new StringBuilder();
        var components = new ActionRowProperties();

        var subscription = await supporterService.GetPremiumGuildSubscription(context.DiscordGuild.Id);

        if (subscription != null)
        {
            description.AppendLine("✅ **This server has Premium.**");
            description.AppendLine();

            if (subscription.PurchaserDiscordUserId.HasValue)
            {
                description.AppendLine($"Purchased by <@{subscription.PurchaserDiscordUserId.Value}>.");
            }

            if (subscription.DateEnding.HasValue)
            {
                description.AppendLine(
                    $"Active until <t:{((DateTimeOffset)subscription.DateEnding.Value).ToUnixTimeSeconds()}:D>.");
            }

            description.AppendLine();
            AppendPerkListWithCommands(description);

            if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                if (subscription.PurchaserDiscordUserId == context.DiscordUser.Id)
                {
                    components.WithButton("Change plan",
                        $"{InteractionConstants.PremiumServer.ManageSubscription}:update",
                        style: ButtonStyle.Secondary);
                    components.WithButton("Cancel subscription",
                        $"{InteractionConstants.PremiumServer.ManageSubscription}:cancel",
                        style: ButtonStyle.Secondary);
                    response.Components = components;
                }
                else
                {
                    description.AppendLine();
                    description.AppendLine("-# Only the purchaser can manage this subscription.");
                }
            }
            else if (subscription.DiscordEntitlementId.HasValue)
            {
                description.AppendLine();
                description.AppendLine("-# Purchased through Discord. Manage it in your Discord server settings.");
            }

            var purchaserSubscriptions =
                await supporterService.GetPremiumGuildSubscriptionsForPurchaser(context.DiscordUser.Id);
            if (purchaserSubscriptions.Any(a => a.DiscordGuildId != context.DiscordGuild.Id))
            {
                components.WithButton("My Premium servers",
                    InteractionConstants.PremiumServer.MyServers,
                    style: ButtonStyle.Secondary);
                response.Components = components;
            }
        }
        else if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            description.AppendLine("✅ **This server has Premium access.**");
            description.AppendLine("-# Access was granted to this server, so there is no subscription to manage.");
            description.AppendLine();
            AppendPerkListWithCommands(description);
        }
        else
        {
            description.AppendLine("Put your server's music scene on autopilot and unlock perks for everyone:");
            description.AppendLine();
            AppendPerkPitchList(description);
            description.AppendLine();
            description.AppendLine("Anyone can purchase Premium for this server. Configuring features requires server management permissions.");

            var stripeSupporter = await supporterService.GetStripeSupporter(context.DiscordUser.Id);
            var pricing = await supporterService.GetPricing(userLocale, stripeSupporter?.Currency,
                StripeSupporterType.PremiumServer);

            if (pricing != null)
            {
                response.Embed.AddField($"Monthly - {pricing.MonthlyPriceString}",
                    $"-# {pricing.MonthlySubText}", true);
                response.Embed.AddField($"Yearly - {pricing.YearlyPriceString}",
                    $"-# {pricing.YearlySubText}", true);

                components.WithButton("Get monthly",
                    $"{InteractionConstants.PremiumServer.GetPurchaseLink}:monthly:{source}");
                components.WithButton("Get yearly",
                    $"{InteractionConstants.PremiumServer.GetPurchaseLink}:yearly:{source}");
                response.Components = components;
            }
            else
            {
                description.AppendLine();
                description.AppendLine("Premium server is not available for purchase yet. Stay tuned!");
            }

            var existingSubscriptions =
                await supporterService.GetPremiumGuildSubscriptionsForPurchaser(context.DiscordUser.Id);
            if (existingSubscriptions.Count > 0)
            {
                description.AppendLine();
                description.AppendLine(
                    $"-# You currently have Premium Server subscriptions for {existingSubscriptions.Count} other {(existingSubscriptions.Count == 1 ? "server" : "servers")}.");
                components.WithButton("My Premium servers",
                    InteractionConstants.PremiumServer.MyServers,
                    style: ButtonStyle.Secondary);
                response.Components = components;
            }
        }

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithFooter("Premium server unlocks perks for this whole server\n" +
                                  "Looking for personal perks? Check out /getsupporter");

        return response;
    }

    public async Task<ResponseModel> MyPremiumServers(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var subscriptions =
            await supporterService.GetPremiumGuildSubscriptionsForPurchaser(context.DiscordUser.Id);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## ✨ My Premium servers");
        container.WithSeparator();

        if (subscriptions.Count == 0)
        {
            container.WithTextDisplay("You don't have any active Premium server subscriptions.");
            return response;
        }

        foreach (var subscription in subscriptions)
        {
            var guild = await guildService.GetGuildAsync(subscription.DiscordGuildId);

            var details = new StringBuilder();
            details.AppendLine(guild?.Name != null
                ? $"**{StringExtensions.Sanitize(guild.Name)}**"
                : $"**Unknown server** · `{subscription.DiscordGuildId}`");
            details.Append(
                $"-# Started <t:{((DateTimeOffset)subscription.DateStarted).ToUnixTimeSeconds()}:D>");
            if (subscription.DateEnding.HasValue)
            {
                details.Append(
                    $" · Active until <t:{((DateTimeOffset)subscription.DateEnding.Value).ToUnixTimeSeconds()}:D>");
            }

            if (!string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            {
                container.AddComponent(new ComponentSectionProperties(
                    new ButtonProperties(
                        $"{InteractionConstants.PremiumServer.MyServersManage}:{subscription.Id}",
                        "Manage", ButtonStyle.Secondary))
                {
                    Components = [new TextDisplayProperties(details.ToString())]
                });
            }
            else
            {
                if (subscription.DiscordEntitlementId.HasValue)
                {
                    details.AppendLine();
                    details.Append("-# Purchased through Discord. Manage it in that server's Discord settings.");
                }

                container.WithTextDisplay(details.ToString());
            }
        }

        container.WithSeparator();

        if (subscriptions.Any(a => !string.IsNullOrWhiteSpace(a.StripeCustomerId)))
        {
            container.WithActionRow(new ActionRowProperties()
                .WithButton("Open billing portal", InteractionConstants.PremiumServer.MyServersBillingPortal,
                    style: ButtonStyle.Secondary));
        }

        container.WithTextDisplay("-# Leaving a server does not cancel its subscription.");

        return response;
    }

    public async Task<ResponseModel> ManageMyPremiumServer(ContextModel context, int subscriptionId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var subscription = await supporterService.GetPremiumGuildSubscriptionById(subscriptionId);

        if (subscription == null ||
            subscription.PurchaserDiscordUserId != context.DiscordUser.Id ||
            string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
        {
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription("This subscription doesn't exist or can't be managed here.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var guild = await guildService.GetGuildAsync(subscription.DiscordGuildId);

        response.Embed.WithTitle("✨ Premium server");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var description = new StringBuilder();
        description.AppendLine(guild?.Name != null
            ? $"Subscription for **{StringExtensions.Sanitize(guild.Name)}**."
            : $"Subscription for server `{subscription.DiscordGuildId}`.");
        description.AppendLine(
            $"Started <t:{((DateTimeOffset)subscription.DateStarted).ToUnixTimeSeconds()}:D>.");
        if (subscription.DateEnding.HasValue)
        {
            description.AppendLine(
                $"Active until <t:{((DateTimeOffset)subscription.DateEnding.Value).ToUnixTimeSeconds()}:D>.");
        }

        response.Embed.WithDescription(description.ToString());

        var components = new ActionRowProperties();
        components.WithButton("Change plan",
            $"{InteractionConstants.PremiumServer.MyServersManageFlow}:{subscription.Id}:update",
            style: ButtonStyle.Secondary);
        components.WithButton("Cancel subscription",
            $"{InteractionConstants.PremiumServer.MyServersManageFlow}:{subscription.Id}:cancel",
            style: ButtonStyle.Secondary);
        response.Components = components;

        return response;
    }

    private static void AppendPerkPitchList(StringBuilder description)
    {
        description.AppendLine("👑 **Automatic crownseeder** — seed crowns daily, weekly or monthly");
        description.AppendLine("📊 **Scheduled server recaps** — your server's weekly or monthly top charts, posted automatically");
        description.AppendLine("🤖 **Custom bot branding** and no sponsored messages");
        description.AppendLine("🎮 **100 daily Jumble and Pixel games** for every member");
        description.AppendLine("📜 **Lyrics unlocked** for every member");
        description.AppendLine("⌨️ **Server-wide shortcuts**");
        description.AppendLine("⚙️ **Role filters** and server activity threshold");
    }

    private static void AppendPerkListWithCommands(StringBuilder description)
    {
        description.AppendLine("👑 **Automatic crownseeder** — crownseeder setting in `/configuration`");
        description.AppendLine("📊 **Scheduled server recaps** — `.serverrecap`");
        description.AppendLine("🤖 **Custom bot branding** — `.botbranding`");
        description.AppendLine("🎮 **100 daily Jumble and Pixel games** for every member");
        description.AppendLine("📜 **Lyrics unlocked** for every member");
        description.AppendLine("⌨️ **Server-wide shortcuts** — `.servershortcuts`");
        description.AppendLine("⚙️ **Role filters** — `.allowedroles`, `.blockedroles`, `.serveractivitythreshold`");
    }

    private static string BuildPremiumFooter(NetCord.User lastModifier, string firstLine = "-# ✨ Premium server")
    {
        return lastModifier != null
            ? $"{firstLine}\n-# Last modified by {lastModifier.Username}"
            : firstLine;
    }

    public async Task<ResponseModel> GetPremiumServerSettings(ContextModel context,
        List<SettingsTab> availableTabs = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var showTabRow = availableTabs is { Count: > 1 };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay($"## ✨ Premium server — {guild.Name}");
        container.WithSeparator();

        if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            var shortcuts = await shortcutService.GetGuildShortcuts(guild);

            var crownSeeder = guild.AutomaticCrownSeeder.HasValue
                ? $"Crowns are automatically seeded {guild.AutomaticCrownSeeder.Value.ToString().ToLower()}."
                : "Not scheduled yet.";
            container.WithTextDisplay($"**👑 Automatic crownseeder**\n{crownSeeder}\n-# Crownseeder setting in `/configuration`");

            var recap = guild.RecapSchedule.HasValue && guild.RecapChannelId.HasValue
                ? $"{(guild.RecapSchedule == ServerRecapSchedule.Weekly ? "Weekly" : "Monthly")} recaps are posted in <#{guild.RecapChannelId.Value}>."
                : "Not set up yet.";
            container.WithTextDisplay($"**📊 Scheduled server recaps**\n{recap}\n-# `.serverrecap`");

            var featuredMode = guild.FeaturedMode ?? GuildFeaturedMode.GlobalFeatured;
            container.WithTextDisplay($"**🤖 Custom bot branding**\nCurrent mode: {GetFeaturedModeName(featuredMode)}.\n-# `.botbranding`");

            container.WithTextDisplay("**🎮 Games and 📜 lyrics**\nEvery member gets 100 daily Jumble and Pixel games and access to `.lyrics`.");

            container.WithTextDisplay($"**⌨️ Server-wide shortcuts**\n{shortcuts.Count}/10 shortcut slots used.\n-# `.servershortcuts`");

            var filtering = new StringBuilder();
            filtering.AppendLine($"**{guild.AllowedRoles?.Length ?? 0}** allowed, **{guild.BlockedRoles?.Length ?? 0}** blocked and **{guild.BotManagementRoles?.Length ?? 0}** bot management roles set.");
            filtering.AppendLine(guild.UserActivityThresholdDays.HasValue
                ? $"Server activity threshold set to **{guild.UserActivityThresholdDays.Value}** days."
                : "No server activity threshold set.");
            container.WithTextDisplay($"**⚙️ Role filters and activity threshold**\n{filtering}" +
                                      "-# `.allowedroles` · `.blockedroles` · `.botmanagementroles` · `.serveractivitythreshold`");

            container.WithSeparator();
            container.WithActionRow(new ActionRowProperties()
                .WithButton("Manage Premium server", $"{InteractionConstants.PremiumServer.GetOverview}:settings",
                    style: ButtonStyle.Secondary));
        }
        else
        {
            var pitch = new StringBuilder();
            pitch.AppendLine("Put your server's music scene on autopilot and unlock perks for everyone:");
            pitch.AppendLine();
            AppendPerkPitchList(pitch);
            container.WithTextDisplay(pitch.ToString());

            container.WithSeparator();
            container.WithActionRow(new ActionRowProperties()
                .WithButton("Get Premium server", $"{InteractionConstants.PremiumServer.GetOverview}:settings",
                    style: ButtonStyle.Primary));
        }

        if (showTabRow)
        {
            container.WithActionRow(GuildSettingBuilder.BuildSettingsTabRow(availableTabs, SettingsTab.Premium,
                context.DiscordUser.Id));
        }

        return response;
    }

    public async Task<ResponseModel> AllowedRoles(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);

        var allowedRoles = new RoleMenuProperties(InteractionConstants.SetAllowedRoleMenu)
            .WithPlaceholder("Pick allowed roles")
            .WithMinValues(0)
            .WithMaxValues(25);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Set server allowed roles");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine("Select the roles that you want to be visible in .fmbot.");
        description.AppendLine("This affects WhoKnows, but also all server-wide charts and other commands.");
        description.AppendLine();

        if (guild.AllowedRoles != null && guild.AllowedRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.AllowedRoles)
            {
                var role = await context.DiscordGuild.GetRoleAsync(roleId);
                if (role != null)
                {
                    description.AppendLine($"- <@&{roleId}>");
                }
            }
        }
        else
        {
            description.AppendLine($"Picked roles: None");
        }

        container.WithTextDisplay(description.ToString());
        container.AddComponent(allowedRoles);

        var footerFirstLine = guild.GuildFlags.HasValue && guild.GuildFlags.Value.HasFlag(GuildFlags.LegacyWhoKnowsWhitelist)
            ? "-# ✨ Grandfathered allowed roles access"
            : "-# ✨ Premium server";

        container.WithSeparator();
        container.WithTextDisplay(BuildPremiumFooter(lastModifier, footerFirstLine));

        return response;
    }

    public async Task<ResponseModel> BlockedRoles(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);

        var blockedRoles = new RoleMenuProperties(InteractionConstants.SetBlockedRoleMenu)
            .WithPlaceholder("Pick blocked roles")
            .WithMinValues(0)
            .WithMaxValues(25);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Set server blocked roles");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine("Select the roles that you want to be blocked in .fmbot.");
        description.AppendLine("This affects WhoKnows, but also all server-wide charts and other commands.");
        description.AppendLine();

        if (guild.BlockedRoles != null && guild.BlockedRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.BlockedRoles)
            {
                var role = await context.DiscordGuild.GetRoleAsync(roleId);
                if (role != null)
                {
                    description.AppendLine($"- <@&{roleId}>");
                }
            }
        }
        else
        {
            description.AppendLine($"Picked roles: None");
        }

        container.WithTextDisplay(description.ToString());
        container.AddComponent(blockedRoles);

        container.WithSeparator();
        container.WithTextDisplay(BuildPremiumFooter(lastModifier));

        return response;
    }

    public async Task<ResponseModel> BotManagementRoles(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);

        var botManagementRoles = new RoleMenuProperties(InteractionConstants.SetBotManagementRoleMenu)
            .WithPlaceholder("Pick bot management roles")
            .WithMinValues(0)
            .WithMaxValues(25);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Set bot management roles");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine("Select the roles that are allowed to change .fmbot settings on this server.");
        description.AppendLine();
        description.AppendLine("Users with these roles will be able to:");
        description.AppendLine("- Change all bot settings (except for this one)");
        description.AppendLine("- Block and unblock users");
        description.AppendLine("- Run crownseeder");
        description.AppendLine("- Manage crowns");
        description.AppendLine();

        if (guild.BotManagementRoles != null && guild.BotManagementRoles.Any())
        {
            description.AppendLine($"**Picked roles:**");
            foreach (var roleId in guild.BotManagementRoles)
            {
                var role = await context.DiscordGuild.GetRoleAsync(roleId);
                if (role != null)
                {
                    description.AppendLine($"- <@&{roleId}>");
                }
            }
        }
        else
        {
            description.AppendLine($"Picked roles: None");
        }

        container.WithTextDisplay(description.ToString());
        container.AddComponent(botManagementRoles);

        container.WithSeparator();
        container.WithTextDisplay(BuildPremiumFooter(lastModifier));

        return response;
    }

    public async Task<ResponseModel> SetGuildActivityThreshold(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Set server activity threshold");
        container.WithSeparator();

        var description = new StringBuilder();

        description.AppendLine($"Setting a WhoKnows activity threshold will filter out people who have not talked in your server for a certain amount of days. " +
                               $"A user counts as active as soon as they talk in a channel in which .fmbot has access.");
        description.AppendLine();
        description.AppendLine("This filtering applies to all server-wide commands. " +
                           "The bot only starts tracking user activity after Premium Server has been activated. Any messages before that time are not included.");
        description.AppendLine();

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);

        var components = new ActionRowProperties();

        if (!guild.UserActivityThresholdDays.HasValue)
        {
            description.AppendLine("There is currently no server activity threshold enabled.");
            description.AppendLine("To enable, click the button below and enter the amount of days.");
            components.WithButton("Set server activity threshold", InteractionConstants.SetGuildActivityThreshold, style: ButtonStyle.Secondary);
        }
        else
        {
            var guildMembers = await guildService.GetGuildUsers(context.DiscordGuild.Id);

            description.AppendLine($"✅ Enabled.");
            description.AppendLine($"Anyone who hasn't talked in the last **{guild.UserActivityThresholdDays.Value}** days is currently filtered out.");

            var filterDate = DateTime.UtcNow.AddDays(-guild.UserActivityThresholdDays.Value);
            var activeUserCount = guildMembers.Count(w => w.Value.LastMessage != null && w.Value.LastMessage >= filterDate);

            description.AppendLine($"The bot has seen **{activeUserCount}** {StringExtensions.GetUsersString(activeUserCount)} talk in this time period.");

            components.WithButton("Remove server activity threshold", $"{InteractionConstants.RemoveGuildActivityThreshold}", style: ButtonStyle.Secondary);
        }

        container.WithTextDisplay(description.ToString());
        container.WithActionRow(components);

        container.WithSeparator();
        container.WithTextDisplay(BuildPremiumFooter(lastModifier));

        return response;
    }

    public async Task<ResponseModel> BotBranding(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var featuredMode = guild.FeaturedMode ?? GuildFeaturedMode.GlobalFeatured;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Custom bot branding");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine("Give .fmbot a custom look in this server.");
        description.AppendLine();
        description.AppendLine("**Branding modes:**");
        description.AppendLine("- **Global featured** — The default look, with the avatar following the global hourly featured");
        description.AppendLine("- **Custom bot & global featured** — Your own custom avatar");
        description.AppendLine("- **Custom bot & custom featured** — The avatar follows an hourly featured based on the members of this server");
        description.AppendLine();
        description.AppendLine($"**Current mode:** {GetFeaturedModeName(featuredMode)}");

        if (featuredMode == GuildFeaturedMode.CustomBotGlobalFeatured)
        {
            description.AppendLine(!string.IsNullOrWhiteSpace(guild.CustomLogo)
                ? $"**Custom avatar:** [image]({guild.CustomLogo})"
                : "**Custom avatar:** None yet. Set one by running `.botbranding` with an image attached.");
        }

        if (featuredMode == GuildFeaturedMode.CustomBotCustomFeatured)
        {
            description.AppendLine(
                "-# Custom featured posts to the same channel as your featured notifications. Set those up with `.addwebhook`.");
        }

        description.AppendLine();
        description.AppendLine("-# Please note that custom avatars may be viewed by .fmbot staff to prevent abuse.");

        var modeMenu = new StringMenuProperties(InteractionConstants.BotBranding.SetFeaturedMode)
            .WithPlaceholder("Set branding mode")
            .WithMinValues(1)
            .WithMaxValues(1);

        modeMenu.AddOption("Global featured", Enum.GetName(GuildFeaturedMode.GlobalFeatured),
            description: "Default bot branding and global featured",
            isDefault: featuredMode == GuildFeaturedMode.GlobalFeatured);
        modeMenu.AddOption("Custom bot & global featured", Enum.GetName(GuildFeaturedMode.CustomBotGlobalFeatured),
            description: "Your custom avatar and description",
            isDefault: featuredMode == GuildFeaturedMode.CustomBotGlobalFeatured);
        modeMenu.AddOption("Custom bot & custom featured", Enum.GetName(GuildFeaturedMode.CustomBotCustomFeatured),
            description: "Featured rotation based on this server's members",
            isDefault: featuredMode == GuildFeaturedMode.CustomBotCustomFeatured);

        var components = new ActionRowProperties();
        components.WithButton("Remove custom avatar", InteractionConstants.BotBranding.RemoveAvatar,
            style: ButtonStyle.Secondary,
            disabled: featuredMode != GuildFeaturedMode.CustomBotGlobalFeatured ||
                      string.IsNullOrWhiteSpace(guild.CustomLogo));

        container.WithTextDisplay(description.ToString());
        container.AddComponent(modeMenu);
        container.WithActionRow(components);

        container.WithSeparator();
        container.WithTextDisplay(BuildPremiumFooter(lastModifier));

        return response;
    }

    private static string GetFeaturedModeName(GuildFeaturedMode featuredMode)
    {
        return featuredMode switch
        {
            GuildFeaturedMode.CustomBotGlobalFeatured => "Custom bot & global featured",
            GuildFeaturedMode.CustomBotCustomFeatured => "Custom bot & custom featured",
            _ => "Global featured"
        };
    }

    public async Task<string> SetGuildBotAvatar(RestClient rest, NetCord.Gateway.Guild discordGuild,
        string attachmentUrl, long attachmentSize)
    {
        if (attachmentSize > 8 * 1024 * 1024)
        {
            return "❌ The attached image is too large. Please use an image under 8MB.";
        }

        byte[] imageBytes;
        try
        {
            using var imageResponse = await httpClient.GetAsync(attachmentUrl);
            if (!imageResponse.IsSuccessStatusCode)
            {
                return "❌ Could not download the attached image. Please try again.";
            }

            imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
        }
        catch (HttpRequestException)
        {
            return "❌ Could not download the attached image. Please try again.";
        }

        var imageFormat = GetImageFormat(imageBytes);
        if (imageFormat == null)
        {
            return "❌ Unsupported image format. Please attach a png, jpg, webp or gif image.";
        }

        try
        {
            await rest.ModifyCurrentGuildUserAsync(discordGuild.Id,
                o => o.Avatar = new ImageProperties(imageFormat.Value, imageBytes));
        }
        catch (RestException e)
        {
            return $"❌ Discord did not accept this image. Please try a different image.\n-# {e.Message}";
        }

        await guildService.SetCustomLogoAsync(discordGuild, attachmentUrl);

        return null;
    }

    public static ImageFormat? GetImageFormat(byte[] bytes)
    {
        if (bytes.Length < 12)
        {
            return null;
        }

        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ImageFormat.Png;
        }

        if (bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ImageFormat.Jpeg;
        }

        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38)
        {
            return ImageFormat.Gif;
        }

        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return ImageFormat.WebP;
        }

        return null;
    }

    public static ResponseModel GuildShortcutsPremiumRequired(ContextModel context)
    {
        if (context.DiscordGuild != null && PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            return null;
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var promoText = new StringBuilder();
        promoText.AppendLine(
            "Get Premium server to create shortcuts that work for everyone in this server.");
        promoText.AppendLine();
        promoText.AppendLine("Some examples of what you can use as input and output:");
        promoText.AppendLine("- `today` > `chart today 2x2`");
        promoText.AppendLine("- `gamble` > `milestone random`");
        promoText.AppendLine("- `gm` > `fm oneline`");

        response.Embed.WithDescription(promoText.ToString());

        response.Components = new ActionRowProperties()
            .WithButton("Premium server", style: ButtonStyle.Primary,
                customId: $"{InteractionConstants.PremiumServer.GetOverview}:guild-shortcuts");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.CommandResponse = CommandResponse.PremiumServerRequired;

        return response;
    }

    public async Task<ResponseModel> ListGuildShortcutsAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var shortcuts = await shortcutService.GetGuildShortcuts(guild);

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);

        response.ComponentsContainer.AddComponent(
            new TextDisplayProperties($"## {EmojiProperties.Custom(DiscordConstants.Shortcut).ToDiscordString("shortcut")} Server command shortcuts"));

        if (shortcuts.Count == 0)
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            var emptyState = new StringBuilder();
            emptyState.AppendLine("This server doesn't have any command shortcuts yet.");
            emptyState.AppendLine("Server shortcuts work for every member of this server.");
            emptyState.AppendLine();
            emptyState.AppendLine("Some examples of what you can use as input and output:");
            emptyState.AppendLine("- `today` > `chart today 2x2`");
            emptyState.AppendLine("- `gamble` > `milestone random`");
            emptyState.AppendLine("- `gm` > `fm oneline`");
            response.ComponentsContainer.AddComponent(new TextDisplayProperties(emptyState.ToString()));
        }
        else
        {
            response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
            foreach (var shortcut in shortcuts)
            {
                response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
                    new ButtonProperties($"{InteractionConstants.GuildShortcuts.Manage}:{shortcut.Id}",
                        EmojiProperties.Standard("📝"), ButtonStyle.Secondary))
                {
                    Components =
                    [
                        new TextDisplayProperties(
                            $"**Input:** `{StringExtensions.Sanitize(shortcut.Input)}`\n**Output:** `{StringExtensions.Sanitize(shortcut.Output)}`")
                    ]
                });
            }
        }

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        response.ComponentsContainer.AddComponent(new ComponentSectionProperties(
            new ButtonProperties($"{InteractionConstants.GuildShortcuts.Create}",
                "Create", ButtonStyle.Primary))
        {
            Components =
            [
                new TextDisplayProperties(
                    $"-# ✨ Premium server perk - {shortcuts.Count}/10 shortcut slots used\n" +
                    $"-# Any change takes a minute to apply")
            ]
        });

        return response;
    }

    public async Task<ResponseModel> ManageGuildShortcutAsync(ContextModel context, int shortcutId,
        ulong overviewMessageId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var shortcut = await shortcutService.GetGuildShortcut(shortcutId);

        if (shortcut == null || shortcut.GuildId != guild.GuildId)
        {
            response.ResponseType = ResponseType.Embed;
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.Embed.WithDescription("This shortcut doesn't exist.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        response.ComponentsContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
        var description = new StringBuilder();
        description.AppendLine($"**Input:** `{shortcut.Input}`");
        description.AppendLine($"**Output:** `{shortcut.Output}`");
        response.ComponentsContainer.AddComponent(new TextDisplayProperties(description.ToString()));

        response.ComponentsContainer.AddComponent(new ComponentSeparatorProperties());
        var actionRow = new ActionRowProperties();
        actionRow.Add(new ButtonProperties(
            $"{InteractionConstants.GuildShortcuts.Modify}:{shortcut.Id}:{overviewMessageId}",
            "Modify", ButtonStyle.Secondary));
        actionRow.Add(new ButtonProperties(
            $"{InteractionConstants.GuildShortcuts.Delete}:{shortcut.Id}:{overviewMessageId}",
            "Delete", ButtonStyle.Danger));
        response.ComponentsContainer.AddComponent(actionRow);

        return response;
    }

    public async Task<GuildShortcut> GetGuildShortcut(int shortcutId)
    {
        return await shortcutService.GetGuildShortcut(shortcutId);
    }

    public async Task<ResponseModel> CreateGuildShortcutAsync(ContextModel context, string input, string output)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
            var shortcuts = await shortcutService.GetGuildShortcuts(guild);
            var validatedInput = await this.ValidateGuildShortcut(response, context, shortcuts, input, output);
            if (!validatedInput.validated)
            {
                return validatedInput.response;
            }

            await shortcutService.AddOrUpdateGuildShortcut(guild, 0, input, output);
            return null;
        }
        catch (Exception)
        {
            response.Embed.WithDescription($"❌ Error while trying to create server shortcut");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    public async Task<ResponseModel> ModifyGuildShortcutAsync(ContextModel context, int shortcutId, string input,
        string output)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
            var shortcuts = await shortcutService.GetGuildShortcuts(guild);
            var shortcut = shortcuts.FirstOrDefault(s => s.Id == shortcutId);
            if (shortcut == null)
            {
                response.Embed.WithDescription("❌ Shortcut not found.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            var validatedInput =
                await this.ValidateGuildShortcut(response, context, shortcuts, input, output, shortcutId);
            if (!validatedInput.validated)
            {
                return validatedInput.response;
            }

            await shortcutService.AddOrUpdateGuildShortcut(guild, shortcutId, input, output);
            return null;
        }
        catch (Exception ex)
        {
            response.Embed.WithDescription($"❌ Failed to modify server shortcut: {ex.Message}");
            response.Embed.WithColor(DiscordConstants.LastFmColorRed);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    public async Task<ResponseModel> DeleteGuildShortcutAsync(ContextModel context, int shortcutId)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        try
        {
            var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
            var shortcut = await shortcutService.GetGuildShortcut(shortcutId);

            if (shortcut == null || shortcut.GuildId != guild.GuildId)
            {
                response.Embed.WithDescription("❌ Shortcut not found.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
                response.CommandResponse = CommandResponse.NotFound;
                return response;
            }

            await shortcutService.RemoveGuildShortcut(guild, shortcut.Input);
            return null;
        }
        catch (Exception ex)
        {
            response.Embed.WithDescription($"❌ Failed to delete server shortcut: {ex.Message}");
            response.Embed.WithColor(DiscordConstants.LastFmColorRed);
            response.CommandResponse = CommandResponse.Error;
        }

        return response;
    }

    private async Task<(bool validated, ResponseModel response)> ValidateGuildShortcut(ResponseModel response,
        ContextModel context,
        List<GuildShortcut> existingShortcuts,
        string input,
        string output,
        int currentShortcutId = 0)
    {
        if (existingShortcuts.Count >= 10 && currentShortcutId == 0)
        {
            response.Embed.WithDescription($"❌ You can't create more than 10 server shortcuts");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Cooldown;
            return (false, response);
        }

        if (existingShortcuts.Any(a => a.Id != currentShortcutId &&
                                       string.Equals(a.Input, input, StringComparison.OrdinalIgnoreCase)))
        {
            response.Embed.WithDescription($"❌ This server already has a shortcut with this input");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            response.Embed.WithDescription($"❌ You can't use this input for a shortcut");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        var inputCommands = commands.Search(input);
        if (inputCommands.IsSuccess &&
            (inputCommands.Command?.Aliases[0].Equals("shortcuts", StringComparison.OrdinalIgnoreCase) == true ||
             inputCommands.Command?.Aliases[0].Equals("servershortcuts", StringComparison.OrdinalIgnoreCase) == true))
        {
            response.Embed.WithDescription($"❌ You can't use this input for a shortcut\n\n" +
                                           $"`{input}`");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        var outputCommands = commands.Search(output);
        if (!outputCommands.IsSuccess || outputCommands.Command == null)
        {
            if (output.Contains('.'))
            {
                response.Embed.WithDescription(
                    $"❌ No commands found for your output. Make sure you don't include the `.` prefix.\n\n" +
                    $"`{output}`");
            }
            else
            {
                response.Embed.WithDescription($"❌ No commands found for your output\n\n" +
                                               $"`{output}`");
            }

            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.WrongInput;
            return (false, response);
        }

        var badInput = await censorService.ContainsBadWords(input);
        if (badInput)
        {
            Log.Information(
                "Guild {discordGuildId} - user {discordUserId} attempted offensive server shortcut input - {input}",
                context.DiscordGuild.Id, context.DiscordUser.Id, input);
            response.Embed.WithDescription($"❌ Your input contains offensive words.\n\n" +
                                           $"Please note that attempts to circumvent this filter or setting your input to other offensive content may result in action being taken on your account.");
            response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            response.CommandResponse = CommandResponse.Censored;
            return (false, response);
        }

        return (true, response);
    }
}
