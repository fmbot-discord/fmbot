using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services;

namespace FMBot.Bot.Builders;

public class GuildSettingBuilder(
    GuildService guildService,
    IOptions<BotSettings> botSettings,
    AdminService adminService,
    ShardedGatewayClient client,
    ShortcutService shortcutService,
    AutopostService autopostService,
    SupporterService supporterService)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    public async Task<List<SettingsTab>> GetAvailableSettingsTabs(ContextModel context)
    {
        var tabs = new List<SettingsTab> { SettingsTab.User };

        if (context.DiscordGuild != null &&
            context.DiscordUser is PartialGuildUser &&
            await guildService.GetGuildAsync(context.DiscordGuild.Id) != null &&
            await UserIsAllowed(context))
        {
            tabs.Add(SettingsTab.Server);
        }

        return tabs;
    }

    public static ActionRowProperties BuildSettingsTabRow(List<SettingsTab> availableTabs, SettingsTab activeTab,
        ulong discordUserId)
    {
        var tabRow = new ActionRowProperties();
        foreach (var tab in availableTabs)
        {
            var isActive = tab == activeTab;
            tabRow.WithButton(
                tab.GetAttribute<OptionAttribute>().Name,
                customId: $"{InteractionConstants.Settings.Tab}:{(int)tab}:{discordUserId}",
                style: isActive ? ButtonStyle.Primary : ButtonStyle.Secondary,
                disabled: isActive);
        }

        return tabRow;
    }

    public static ActionRowProperties BuildBackRow(ulong discordUserId)
    {
        return new ActionRowProperties()
            .WithButton("← Back to server settings",
                customId: $"{InteractionConstants.Settings.ServerHome}:{discordUserId}",
                style: ButtonStyle.Secondary);
    }

    private static ButtonProperties BuildSectionButton(GuildSettingSection section, ulong discordUserId)
    {
        return new ButtonProperties(
            $"{InteractionConstants.Settings.ServerSection}:{(int)section}:{discordUserId}",
            section.GetAttribute<OptionAttribute>().Name,
            ButtonStyle.Secondary);
    }

    private static ButtonProperties BuildSettingButton(GuildSetting setting, ulong discordUserId, string label = null,
        bool disabled = false)
    {
        return new ButtonProperties(
            $"{InteractionConstants.Settings.ServerSettingOpen}:{(int)setting}:{discordUserId}",
            label ?? setting.GetAttribute<OptionAttribute>().Name,
            ButtonStyle.Secondary)
        {
            Disabled = disabled
        };
    }

    public static GuildSettingSection GetSectionForSetting(GuildSetting setting)
    {
        return setting.GetAttribute<SettingSectionAttribute>()?.Section ?? GuildSettingSection.General;
    }

    public async Task<ResponseModel> GetGuildSettings(ContextModel context, Permissions channelPermissions,
        List<SettingsTab> availableTabs = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var guildUsers = await guildService.GetGuildUsers(context.DiscordGuild.Id);
        var isPremium = PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
        var userId = context.DiscordUser.Id;

        var showTabRow = availableTabs is { Count: > 1 };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay($"## .fmbot server settings · {guild.Name}");
        container.WithSeparator();

        var general = new StringBuilder();
        general.AppendLine("**General**");
        general.Append($"Prefix `{guild.Prefix ?? this._botSettings.Bot.Prefix}`");
        general.Append(guild.Prefix == null ? " (default) · " : " · ");
        general.Append(guild.Language.HasValue
            ? $"Language {guild.Language.Value.GetAttribute<OptionAttribute>().Name}"
            : "Language English (default)");
        general.AppendLine();
        general.Append(guild.EmoteReactions is { Length: > 0 }
            ? $"{guild.EmoteReactions.Length} emote reactions"
            : "No emote reactions");
        if (guild.FmEmbedType.HasValue)
        {
            general.Append(
                $" · Forced `fm` type: {guild.FmEmbedType.Value.GetAttribute<OptionAttribute>().Name}");
        }

        container.AddComponent(new ComponentSectionProperties(
            BuildSectionButton(GuildSettingSection.General, userId))
        {
            Components = [new TextDisplayProperties(general.ToString())]
        });

        container.WithSeparator();

        var memberFilters = new StringBuilder();
        memberFilters.AppendLine("**Who appears in server charts**");
        if (isPremium)
        {
            memberFilters.AppendLine(
                $"{guild.AllowedRoles?.Length ?? 0} allowed roles · {guild.BlockedRoles?.Length ?? 0} blocked roles · " +
                $"{guild.BotManagementRoles?.Length ?? 0} bot management roles");
        }

        memberFilters.Append(
            $"{guildUsers?.Count(c => c.Value.BlockedFromWhoKnows) ?? 0} members blocked");
        memberFilters.Append(guild.ActivityThresholdDays.HasValue
            ? $" · Inactive on .fmbot: {guild.ActivityThresholdDays.Value} days"
            : " · Inactive on .fmbot: off");
        if (isPremium)
        {
            memberFilters.Append(guild.UserActivityThresholdDays.HasValue
                ? $" · Inactive in this server: {guild.UserActivityThresholdDays.Value} days"
                : " · Inactive in this server: off");
        }

        container.AddComponent(new ComponentSectionProperties(
            BuildSectionButton(GuildSettingSection.MemberFilters, userId))
        {
            Components = [new TextDisplayProperties(memberFilters.ToString())]
        });

        container.WithSeparator();

        var crowns = new StringBuilder();
        crowns.AppendLine("**Crowns**");
        if (guild.CrownsDisabled == true)
        {
            crowns.Append("Crown functionality is disabled on this server.");
        }
        else
        {
            crowns.Append(
                $"Enabled · minimum {guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown} plays");
            crowns.Append(guild.CrownsMinimumPlaycountThreshold == null ? " (default)" : "");
            crowns.AppendLine($" · {guildUsers?.Count(c => c.Value.BlockedFromCrowns) ?? 0} members crownblocked");
            crowns.Append(guild.CrownsActivityThresholdDays.HasValue
                ? $"Inactive on .fmbot: {guild.CrownsActivityThresholdDays.Value} days"
                : "Inactive on .fmbot: off");
            if (isPremium)
            {
                crowns.Append(guild.AutomaticCrownSeeder.HasValue
                    ? $" · Auto-seeding: {guild.AutomaticCrownSeeder.Value.ToString().ToLower()}"
                    : " · Auto-seeding: off");
            }
        }

        container.AddComponent(new ComponentSectionProperties(
            BuildSectionButton(GuildSettingSection.Crowns, userId))
        {
            Components = [new TextDisplayProperties(crowns.ToString())]
        });

        container.WithSeparator();

        var commands = new StringBuilder();
        commands.AppendLine("**Commands & channels**");
        commands.Append(guild.DisabledCommands is { Length: > 0 }
            ? $"{guild.DisabledCommands.Length} commands disabled server-wide"
            : "All commands enabled server-wide");
        if (isPremium)
        {
            var shortcuts = await shortcutService.GetGuildShortcuts(guild);
            commands.AppendLine();
            commands.Append($"{shortcuts.Count}/10 server shortcuts used");
        }

        container.AddComponent(new ComponentSectionProperties(
            BuildSectionButton(GuildSettingSection.Commands, userId))
        {
            Components = [new TextDisplayProperties(commands.ToString())]
        });

        if (isPremium)
        {
            container.WithSeparator();

            var autoposts = await autopostService.GetAutopostsForGuild(guild.GuildId);

            var automation = new StringBuilder();
            automation.AppendLine("**Automation & branding**");
            automation.Append(autoposts.Count > 0
                ? $"{autoposts.Count} {(autoposts.Count == 1 ? "autopost" : "autoposts")} configured"
                : "No autoposts configured");
            automation.Append(
                $" · Branding: {PremiumSettingBuilder.GetFeaturedModeName(guild.FeaturedMode ?? GuildFeaturedMode.GlobalFeatured)}");

            container.AddComponent(new ComponentSectionProperties(
                BuildSectionButton(GuildSettingSection.Automation, userId))
            {
                Components = [new TextDisplayProperties(automation.ToString())]
            });
        }

        var missingPermissions = new StringBuilder();
        if (!channelPermissions.HasFlag(Permissions.SendMessages))
        {
            missingPermissions.AppendLine("❌ Send messages");
        }

        if (!channelPermissions.HasFlag(Permissions.AttachFiles))
        {
            missingPermissions.AppendLine("❌ Attach files");
        }

        if (!channelPermissions.HasFlag(Permissions.EmbedLinks))
        {
            missingPermissions.AppendLine("❌ Embed links");
        }

        if (!channelPermissions.HasFlag(Permissions.AddReactions))
        {
            missingPermissions.AppendLine("❌ Add reactions");
        }

        if (!channelPermissions.HasFlag(Permissions.UseExternalEmojis))
        {
            missingPermissions.AppendLine("❌ Use external emojis");
        }

        if (missingPermissions.Length > 0)
        {
            missingPermissions.AppendLine();
            missingPermissions.AppendLine(
                "These are missing in this channel. We recommend granting them server-wide via `Server Settings` > `Roles` so all .fmbot commands work everywhere.");
            container.WithSeparator();
            container.WithTextDisplay($"**Missing permissions in this channel**\n{missingPermissions}");
        }

        container.WithSeparator();
        container.AddComponent(await BuildPremiumStatusSection(context, guild, isPremium));
        container.WithSeparator();

        var footer = new StringBuilder();
        footer.Append($"-# {guild.DiscordGuildId}");
        if (!showTabRow)
        {
            footer.Append($"\n-# Use '{context.Prefix}settings' for personal .fmbot settings");
        }

        container.WithTextDisplay(footer.ToString());

        if (showTabRow)
        {
            container.WithActionRow(BuildSettingsTabRow(availableTabs, SettingsTab.Server, context.DiscordUser.Id));
        }

        return response;
    }

    private async Task<ComponentSectionProperties> BuildPremiumStatusSection(ContextModel context,
        Persistence.Domain.Models.Guild guild, bool isPremium)
    {
        var premium = new StringBuilder();

        if (isPremium)
        {
            var subscription = await supporterService.GetPremiumGuildSubscription(context.DiscordGuild.Id);

            premium.AppendLine("✨ **Premium server**");
            premium.Append("Active");
            if (subscription?.DateEnding != null)
            {
                premium.Append(
                    $" until at least <t:{((DateTimeOffset)subscription.DateEnding.Value).ToUnixTimeSeconds()}:D>");
            }

            premium.Append(". ");
            premium.Append("All Premium server perks are unlocked for this server.");

            return new ComponentSectionProperties(new ButtonProperties(
                $"{InteractionConstants.PremiumServer.GetOverview}:settings", "Manage Premium server", ButtonStyle.Secondary))
            {
                Components = [new TextDisplayProperties(premium.ToString())]
            };
        }

        premium.AppendLine("✨ **Premium server**");
        premium.Append(
            "-# Unlock role filters, scheduled autoposts, custom bot branding, automatic crownseeding, server shortcuts and more");

        return new ComponentSectionProperties(new ButtonProperties(
            $"{InteractionConstants.PremiumServer.GetOverview}:settings", PremiumSettingBuilder.GetPremiumButtonLabel, ButtonStyle.Primary))
        {
            Components = [new TextDisplayProperties(premium.ToString())]
        };
    }

    public async Task<ResponseModel> ServerSettingsSection(ContextModel context, GuildSettingSection section,
        bool showForcedFmType = false)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var isPremium = PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);
        var userId = context.DiscordUser.Id;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        switch (section)
        {
            case GuildSettingSection.General:
                BuildGeneralSection(container, context, guild, showForcedFmType);
                break;
            case GuildSettingSection.MemberFilters:
                await BuildMemberFiltersSection(container, context, guild, userId, isPremium);
                break;
            case GuildSettingSection.Crowns:
                await BuildCrownsSection(container, context, guild, userId, isPremium);
                break;
            case GuildSettingSection.Commands:
                await BuildCommandsSection(container, guild, userId, isPremium);
                break;
            case GuildSettingSection.Automation:
                await BuildAutomationSection(container, guild, userId, isPremium);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(section), section, null);
        }

        container.WithSeparator();
        container.WithActionRow(BuildBackRow(userId));

        return response;
    }

    private void BuildGeneralSection(ComponentContainerProperties container, ContextModel context,
        Persistence.Domain.Models.Guild guild, bool showForcedFmType)
    {
        var textPrefix = guild.Prefix ?? this._botSettings.Bot.Prefix;

        container.WithTextDisplay($"## General · {guild.Name}");
        container.WithTextDisplay("How .fmbot talks and looks in this server.");
        container.WithSeparator();

        var prefix = new StringBuilder();
        var prefixIsCustom = guild.Prefix != null && guild.Prefix != this._botSettings.Bot.Prefix;
        var activePrefix = guild.Prefix ?? this._botSettings.Bot.Prefix;

        prefix.AppendLine("**Text command prefix**");
        prefix.Append("What members type before a text command. ");
        prefix.AppendLine(guild.Prefix != null
            ? $"Currently `{guild.Prefix}`."
            : $"Currently `{this._botSettings.Bot.Prefix}` (default).");
        prefix.AppendLine();
        prefix.Append($"Examples: `{activePrefix}fm` and `{activePrefix}whoknows`");

        if (prefixIsCustom)
        {
            prefix.AppendLine();
            prefix.Append(
                $"Most people know .fmbot with the `{this._botSettings.Bot.Prefix}` prefix, so consider informing your members.");
        }

        container.AddComponent(new ComponentSectionProperties(new ButtonProperties(
            prefixIsCustom ? InteractionConstants.RemovePrefix : InteractionConstants.SetPrefix,
            prefixIsCustom ? "Remove" : "Set", ButtonStyle.Secondary))
        {
            Components = [new TextDisplayProperties(prefix.ToString())]
        });

        container.WithSeparator();

        var language = new StringBuilder();
        language.AppendLine("**Server language**");
        language.Append("The language .fmbot replies in here. ");
        language.AppendLine(guild.Language.HasValue
            ? $"Currently **{guild.Language.Value.GetAttribute<OptionAttribute>().Name}**."
            : "Currently **English** (default).");
        language.Append(
            "-# Translations are in beta and might be incomplete. De-select to go back to the default.");

        container.WithTextDisplay(language.ToString());

        var languageMenu = new StringMenuProperties(InteractionConstants.GuildLanguageSetting)
            .WithPlaceholder("Server language")
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var option in Enum.GetValues<Language>())
        {
            languageMenu.AddOption(option.GetAttribute<OptionAttribute>().Name, Enum.GetName(option),
                description: option.GetAttribute<OptionAttribute>().Description,
                isDefault: guild.Language == option);
        }

        container.AddComponent(languageMenu);

        if (guild.FmEmbedType.HasValue || showForcedFmType)
        {
            container.WithSeparator();

            var embedType = new StringBuilder();
            embedType.AppendLine("**Forced `fm` type**");
            embedType.Append("Overrides the layout every member picked themselves. ");
            embedType.AppendLine(guild.FmEmbedType.HasValue
                ? $"Currently **{guild.FmEmbedType.Value.GetAttribute<OptionAttribute>().Name}**."
                : "Currently **none**. Every member uses their own `fmmode`.");
            embedType.Append(
                "-# Not recommended. Most members prefer their own `fmmode`, so leave this off unless you have a reason. " +
                $"De-select to disable, or use `{textPrefix}togglecommand` to set it for one channel instead.");

            container.WithTextDisplay(embedType.ToString());

            var fmTypeMenu = new StringMenuProperties(InteractionConstants.FmGuildSettingType)
                .WithPlaceholder("Forced server 'fm' mode")
                .WithMinValues(0)
                .WithMaxValues(1);

            foreach (var option in Enum.GetValues<FmEmbedType>()
                         .OrderBy(o => o.GetAttribute<OptionOrderAttribute>().Order))
            {
                fmTypeMenu.AddOption(option.GetAttribute<OptionAttribute>().Name, Enum.GetName(option),
                    description: option.GetAttribute<OptionAttribute>().Description,
                    isDefault: guild.FmEmbedType == option);
            }

            container.AddComponent(fmTypeMenu);
        }

        container.WithSeparator();

        var reactions = new StringBuilder();
        reactions.AppendLine("**Emote reactions**");
        reactions.Append("Reactions added automatically to `fm` and `featured`. ");
        if (guild.EmoteReactions is { Length: > 0 })
        {
            reactions.Append("Currently: ");
            foreach (var reaction in guild.EmoteReactions)
            {
                reactions.Append($"{reaction} ");
            }

            reactions.AppendLine();
        }
        else
        {
            reactions.AppendLine("Currently off.");
        }

        reactions.AppendLine();
        reactions.AppendLine(
            $"Set with `{textPrefix}serverreactions 😀 😯 🥵` (space between each emoji), or without emojis to disable.");

        container.WithTextDisplay(reactions.ToString());
    }

    private async Task BuildMemberFiltersSection(ComponentContainerProperties container, ContextModel context,
        Persistence.Domain.Models.Guild guild, ulong userId, bool isPremium)
    {
        var guildUsers = await guildService.GetGuildUsers(context.DiscordGuild.Id);
        var textPrefix = guild.Prefix ?? this._botSettings.Bot.Prefix;

        container.WithTextDisplay($"## Who appears in server charts · {guild.Name}");
        container.WithTextDisplay(
            "Controls which members show up in WhoKnows, server charts and every other server-wide command.");
        container.WithSeparator();

        var fmbotActivity = new StringBuilder();
        fmbotActivity.AppendLine("**Inactive on .fmbot**");
        fmbotActivity.Append("Hides members who haven't used .fmbot in a while. ");
        fmbotActivity.Append(guild.ActivityThresholdDays.HasValue
            ? $"Currently hiding anyone who hasn't used .fmbot in **{guild.ActivityThresholdDays.Value}** days.\n-# A member counts as active as soon as they use .fmbot anywhere."
            : "Currently off.");

        container.AddComponent(new ComponentSectionProperties(new ButtonProperties(
            guild.ActivityThresholdDays.HasValue
                ? InteractionConstants.RemoveFmbotActivityThreshold
                : InteractionConstants.SetFmbotActivityThreshold,
            guild.ActivityThresholdDays.HasValue ? "Remove" : "Set", ButtonStyle.Secondary))
        {
            Components = [new TextDisplayProperties(fmbotActivity.ToString())]
        });

        container.WithSeparator();

        var blockedMembers = new StringBuilder();
        blockedMembers.AppendLine("**Blocked members**");
        blockedMembers.Append("Members you manually hid from server-wide commands. ");
        blockedMembers.AppendLine(
            $"Currently **{guildUsers?.Count(c => c.Value.BlockedFromWhoKnows) ?? 0}** blocked.");
        blockedMembers.Append(
            $"-# Add with `{textPrefix}block`, remove with `{textPrefix}unblock`.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.WhoKnowsBlockedUsers, userId, "View"))
        {
            Components = [new TextDisplayProperties(blockedMembers.ToString())]
        });

        container.WithSeparator();

        PremiumSettingBuilder.AppendAllowedRoles(container, context, guild, isPremium);
        container.WithSeparator();

        PremiumSettingBuilder.AppendBlockedRoles(container, context, guild, isPremium);
        container.WithSeparator();

        PremiumSettingBuilder.AppendServerActivityThreshold(container, guild, guildUsers, isPremium);
        container.WithSeparator();

        PremiumSettingBuilder.AppendBotManagementRoles(container, context, guild, isPremium);

        if (!isPremium)
        {
            container.WithSeparator();
            container.AddComponent(PremiumSettingBuilder.BuildPremiumUpsell("memberfilters-settings",
                "Role filters and Discord activity filtering",
                "Filter server charts down to the roles you pick, hide members who've gone quiet, " +
                "and let trusted roles manage .fmbot"));
        }
    }

    private async Task BuildCrownsSection(ComponentContainerProperties container, ContextModel context,
        Persistence.Domain.Models.Guild guild, ulong userId, bool isPremium)
    {
        var guildUsers = await guildService.GetGuildUsers(context.DiscordGuild.Id);
        var textPrefix = guild.Prefix ?? this._botSettings.Bot.Prefix;
        var crownsDisabled = guild.CrownsDisabled == true;

        container.WithTextDisplay($"## Crowns · {guild.Name}");
        container.WithTextDisplay(
            "Members earn a crown whenever they're the #1 listener for an artist in this server.");
        container.WithSeparator();

        var functionality = new StringBuilder();
        functionality.AppendLine("**Crown functionality**");
        functionality.Append("Whether crowns can be earned in this server. ");
        functionality.Append(crownsDisabled
            ? "Currently **disabled**.\n-# Crown history is preserved, but not visible."
            : "Currently **enabled**.");

        container.AddComponent(new ComponentSectionProperties(new ButtonProperties(
            crownsDisabled ? InteractionConstants.ToggleCrowns.Enable : InteractionConstants.ToggleCrowns.Disable,
            crownsDisabled ? "Enable" : "Disable", ButtonStyle.Secondary))
        {
            Components = [new TextDisplayProperties(functionality.ToString())]
        });

        container.WithSeparator();

        var minPlaycount = new StringBuilder();
        minPlaycount.AppendLine("**Minimum playcount**");
        minPlaycount.Append("How many plays a crown needs. ");
        minPlaycount.Append(guild.CrownsMinimumPlaycountThreshold.HasValue
            ? $"Currently **{guild.CrownsMinimumPlaycountThreshold.Value}** plays or more."
            : $"Currently **{Constants.DefaultPlaysForCrown}** plays or more (default).");

        container.AddComponent(new ComponentSectionProperties(new ButtonProperties(
            guild.CrownsMinimumPlaycountThreshold.HasValue
                ? InteractionConstants.RemoveCrownMinPlaycount
                : InteractionConstants.SetCrownMinPlaycount,
            guild.CrownsMinimumPlaycountThreshold.HasValue ? "Remove" : "Set",
            ButtonStyle.Secondary)
        {
            Disabled = crownsDisabled
        })
        {
            Components = [new TextDisplayProperties(minPlaycount.ToString())]
        });

        container.WithSeparator();

        var crownActivity = new StringBuilder();
        crownActivity.AppendLine("**Crown inactivity filter**");
        crownActivity.Append("Blocks crowns for members who haven't used .fmbot in a while. ");
        crownActivity.Append(guild.CrownsActivityThresholdDays.HasValue
            ? $"Currently blocking anyone who hasn't used .fmbot in **{guild.CrownsActivityThresholdDays.Value}** days.\n-# A member counts as active as soon as they use .fmbot anywhere."
            : "Currently off.");

        container.AddComponent(new ComponentSectionProperties(new ButtonProperties(
            guild.CrownsActivityThresholdDays.HasValue
                ? InteractionConstants.RemoveCrownActivityThreshold
                : InteractionConstants.SetCrownActivityThreshold,
            guild.CrownsActivityThresholdDays.HasValue ? "Remove" : "Set", ButtonStyle.Secondary)
        {
            Disabled = crownsDisabled
        })
        {
            Components = [new TextDisplayProperties(crownActivity.ToString())]
        });

        container.WithSeparator();

        var crownBlocked = new StringBuilder();
        crownBlocked.AppendLine("**Crownblocked members**");
        crownBlocked.Append("Members who can't earn crowns. ");
        crownBlocked.AppendLine(
            $"Currently **{guildUsers?.Count(c => c.Value.BlockedFromCrowns) ?? 0}** crownblocked.");
        crownBlocked.Append($"-# Add with `{textPrefix}crownblock`, remove with `{textPrefix}unblock`.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.CrownBlockedUsers, userId, "View"))
        {
            Components = [new TextDisplayProperties(crownBlocked.ToString())]
        });

        container.WithSeparator();

        var seeder = new StringBuilder();
        seeder.AppendLine("**Crownseeder**");
        seeder.AppendLine("Generates or updates every crown in this server at once.");
        if (isPremium)
        {
            seeder.AppendLine(guild.AutomaticCrownSeeder.HasValue
                ? $"Automatic seeding currently runs **{guild.AutomaticCrownSeeder.Value.ToString().ToLower()}**."
                : "Automatic seeding is currently off.");
        }

        seeder.Append(
            "-# Members claim crowns themselves by running `whoknows`. Only server staff can seed them all at once.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.CrownSeeder, userId, "Open", crownsDisabled))
        {
            Components = [new TextDisplayProperties(seeder.ToString().TrimEnd())]
        });
    }

    private async Task BuildCommandsSection(ComponentContainerProperties container,
        Persistence.Domain.Models.Guild guild, ulong userId, bool isPremium)
    {
        container.WithTextDisplay($"## Commands & channels · {guild.Name}");
        container.WithTextDisplay("Where .fmbot commands work, and what members can run.");
        container.WithSeparator();

        var guildCommands = new StringBuilder();
        guildCommands.AppendLine("**Disabled server commands**");
        guildCommands.Append("Commands turned off everywhere in this server. ");
        if (guild.DisabledCommands is { Length: > 0 })
        {
            guildCommands.Append("Currently: ");
            foreach (var disabledCommand in guild.DisabledCommands.Take(32))
            {
                guildCommands.Append($"`{disabledCommand}` ");
            }

            if (guild.DisabledCommands.Length > 32)
            {
                guildCommands.Append($"and {guild.DisabledCommands.Length - 32} more");
            }
        }
        else
        {
            guildCommands.Append("Currently none. Every command is enabled.");
        }

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.DisabledGuildCommands, userId, "Manage"))
        {
            Components = [new TextDisplayProperties(guildCommands.ToString())]
        });

        container.WithSeparator();

        var channelCommands = new StringBuilder();
        channelCommands.AppendLine("**Channel commands**");
        channelCommands.AppendLine(
            "Turns .fmbot or single commands off per channel, and sets a per-channel `fm` type.");
        channelCommands.AppendLine("-# Opens for the channel you're in. Switch channels from there.");
        channelCommands.Append(
            $"-# Set an `fm` cooldown for a channel with `{guild.Prefix ?? this._botSettings.Bot.Prefix}cooldown`.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.DisabledCommands, userId, "Manage"))
        {
            Components = [new TextDisplayProperties(channelCommands.ToString())]
        });

        container.WithSeparator();

        if (isPremium)
        {
            var shortcuts = await shortcutService.GetGuildShortcuts(guild);

            var shortcutText = new StringBuilder();
            shortcutText.AppendLine("**Server shortcuts**");
            shortcutText.Append("Shared text command shortcuts for everyone here. ");
            shortcutText.Append($"Currently **{shortcuts.Count}**/10 slots used.");

            container.AddComponent(new ComponentSectionProperties(
                BuildSettingButton(GuildSetting.ServerShortcuts, userId, "Manage"))
            {
                Components = [new TextDisplayProperties(shortcutText.ToString())]
            });
        }
        else
        {
            container.AddComponent(PremiumSettingBuilder.BuildPremiumUpsell("commands-settings",
                "Server shortcuts",
                "Shared text command shortcuts that work for everyone in this server"));
        }
    }

    private async Task BuildAutomationSection(ComponentContainerProperties container,
        Persistence.Domain.Models.Guild guild, ulong userId, bool isPremium)
    {
        container.WithTextDisplay($"## Automation & branding · {guild.Name}");
        container.WithTextDisplay("What .fmbot posts on its own, and how it looks doing it.");
        container.WithSeparator();

        if (!isPremium)
        {
            container.AddComponent(PremiumSettingBuilder.BuildPremiumUpsell("automation-settings",
                "Autoposts and bot branding",
                "Scheduled recaps and top charts, a custom bot avatar, and a featured rotation built from your own members"));
            return;
        }

        var autoposts = await autopostService.GetAutopostsForGuild(guild.GuildId);

        var autopostText = new StringBuilder();
        autopostText.AppendLine("**Server autoposts**");
        autopostText.Append("Posts recaps and top charts on a schedule. ");
        autopostText.Append(autoposts.Count > 0
            ? $"Currently **{autoposts.Count}** {(autoposts.Count == 1 ? "autopost" : "autoposts")}."
            : "Currently none.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.ServerAutoposts, userId, "Manage"))
        {
            Components = [new TextDisplayProperties(autopostText.ToString())]
        });

        container.WithSeparator();

        var branding = new StringBuilder();
        branding.AppendLine("**Bot branding**");
        branding.Append("Gives .fmbot a custom look in this server. ");
        branding.AppendLine(
            $"Currently **{PremiumSettingBuilder.GetFeaturedModeName(guild.FeaturedMode ?? GuildFeaturedMode.GlobalFeatured)}**.");
        branding.Append(
            "-# Set a custom avatar, or rotate a featured built from your own members.");

        container.AddComponent(new ComponentSectionProperties(
            BuildSettingButton(GuildSetting.BotBranding, userId, "Manage"))
        {
            Components = [new TextDisplayProperties(branding.ToString())]
        });
    }


    public async Task<ResponseModel> CrownSeeder(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var crownsDisabled = guild.CrownsDisabled == true;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Crownseeder");
        container.WithSeparator();

        var description = new StringBuilder();

        description.AppendLine(
            $"Crowns can be earned when someone is the #1 listener for an artist and has {guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown} plays or more. ");
        description.AppendLine();
        description.AppendLine($"Users can run `whoknows` to claim crowns, but you can also use the crownseeder to generate or update all crowns at once. " +
                               $"Only server staff can do this, because some people prefer manual crown claiming.");
        description.AppendLine();
        description.AppendLine($"To add or update all crowns, press the button below.");

        var components = new ActionRowProperties();
        components.WithButton("Run crownseeder", $"{InteractionConstants.RunCrownseeder}", style: ButtonStyle.Secondary, disabled: crownsDisabled);

        var isPremium = PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id);

        StringMenuProperties scheduleMenu = null;
        if (isPremium)
        {
            description.AppendLine();
            description.AppendLine(guild.AutomaticCrownSeeder.HasValue
                ? $"Automatic crownseeder is enabled and runs **{guild.AutomaticCrownSeeder.Value.ToString().ToLower()}**."
                : "You can also let the crownseeder run automatically on a schedule.");

            scheduleMenu = BuildCrownSeederScheduleMenu(guild);
            scheduleMenu.WithDisabled(crownsDisabled);
        }

        if (crownsDisabled)
        {
            description.AppendLine();
            description.AppendLine("⚠️ Note: Crown functionality is disabled in this server.");
        }

        container.WithTextDisplay(description.ToString());
        if (scheduleMenu != null)
        {
            container.AddComponent(scheduleMenu);
        }

        container.WithActionRow(components);

        if (!isPremium)
        {
            container.WithSeparator();
            container.AddComponent(PremiumSettingBuilder.BuildPremiumUpsell("crownseeder-settings",
                "Automatic crownseeding",
                "Seed this server's crowns daily, weekly or monthly instead of running it by hand"));
        }

        return response;
    }

    public static ResponseModel CrownSeederRunning(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Crownseeder");
        container.WithSeparator();
        container.WithTextDisplay($"<a:loading:821676038102056991> Seeding crowns... ");
        container.WithSeparator();
        container.WithTextDisplay($"-# Crownseeder initiated by {context.DiscordUser.Username}");

        return response;
    }

    public async Task<ResponseModel> CrownSeederDone(ContextModel context, int amountSeeded)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var prefix = guild.Prefix ?? this._botSettings.Bot.Prefix;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Crownseeder");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine($"✅ Seeded **{amountSeeded}** crowns for your server.");
        description.AppendLine();
        description.AppendLine($"If you would like to remove crowns, use:");
        description.AppendLine($"- `{prefix}killallcrowns` (All crowns)");
        description.AppendLine($"- `{prefix}killallseededcrowns` (Only seeded crowns)");

        StringMenuProperties scheduleMenu = null;

        container.WithTextDisplay(description.ToString());

        if (PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            var automaticCrownseeder = new StringBuilder();
            if (guild.AutomaticCrownSeeder.HasValue)
            {
                automaticCrownseeder.AppendLine($"Automatic crownseeder is enabled and runs **{guild.AutomaticCrownSeeder.Value.ToString().ToLower()}**.");
            }
            else
            {
                automaticCrownseeder.AppendLine("You can also let the crownseeder run automatically on a schedule.");
                scheduleMenu = BuildCrownSeederScheduleMenu(guild);
            }

            container.WithSeparator();
            container.WithTextDisplay(automaticCrownseeder.ToString());
        }
        else
        {
            container.WithSeparator();
            container.AddComponent(PremiumSettingBuilder.BuildPremiumUpsell("crownseeder-run",
                "Automatic crownseeding",
                "Running this manually every time? Premium server can seed crowns daily, weekly or monthly"));
        }

        if (scheduleMenu != null)
        {
            container.AddComponent(scheduleMenu);
        }

        container.WithSeparator();
        container.WithTextDisplay($"-# Crownseeder initiated by {context.DiscordUser.Username}");

        return response;
    }

    private static StringMenuProperties BuildCrownSeederScheduleMenu(Persistence.Domain.Models.Guild guild)
    {
        var scheduleMenu = new StringMenuProperties(InteractionConstants.SetCrownSeederSchedule)
            .WithPlaceholder("Automatic crownseeder schedule")
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var schedule in Enum.GetValues<AutomaticCrownSeeder>())
        {
            scheduleMenu.AddOption(schedule.ToString(), Enum.GetName(schedule),
                description: $"Automatically seed crowns {schedule.ToString().ToLower()}",
                isDefault: guild.AutomaticCrownSeeder == schedule);
        }

        return scheduleMenu;
    }

    public async Task<bool> UserIsAllowed(ContextModel context, bool managersAllowed = true)
    {
        if (context.DiscordGuild == null)
        {
            return false;
        }

        // TODO: check if this works
        var guildUser = (PartialGuildUser)context.DiscordUser;
        var permissions = guildUser.GetPermissions(context.DiscordGuild);

        if (permissions.HasFlag(Permissions.BanUsers) ||
            permissions.HasFlag(Permissions.Administrator))
        {
            return true;
        }

        if (await adminService.HasCommandAccessAsync(context.DiscordUser, UserType.Admin))
        {
            return true;
        }

        if (managersAllowed && PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
            if (guild.BotManagementRoles != null &&
                guild.BotManagementRoles.Any() &&
                guildUser.RoleIds.Any(a => guild.BotManagementRoles.Contains(a)))
            {
                return true;
            }
        }

        return false;
    }

    public static string UserNotAllowedResponseText(bool managersAllowed = true)
    {
        var response = new StringBuilder();
        response.AppendLine("You are not authorized to change this .fmbot setting.");
        response.AppendLine();
        response.AppendLine("To change .fmbot settings, you need at least one of the following:");
        response.AppendLine("- `Administrator` permission");
        response.AppendLine("- `Ban Members` permission");
        if (managersAllowed)
        {
            response.AppendLine("- A role that is allowed to manage the bot");
        }

        return response.ToString();
    }

    public static async Task UserNotAllowedResponse(IInteractionContext context, bool managersAllowed = true)
    {
        await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(UserNotAllowedResponseText(managersAllowed))
            .WithFlags(MessageFlags.Ephemeral)));
    }

    public async Task<ResponseModel> BlockedUsersAsync(
        ContextModel context,
        bool includeCrownBlocked = false,
        string searchValue = null)

    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var prefix = guild.Prefix ?? this._botSettings.Bot.Prefix;
        var guildUsers = await guildService.GetGuildUsers(context.DiscordGuild.Id);

        var footer = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(searchValue))
        {
            footer.AppendLine($"Showing results with '{StringExtensions.Sanitize(searchValue)}'");
        }

        footer.AppendLine($"Block type — Discord ID — Name — Last.fm");

        if (includeCrownBlocked)
        {
            response.Embed.WithTitle($"Crownblocked users in {context.DiscordGuild.Name}");
            footer.AppendLine($"To add: {prefix}crownblock mention/user id/Last.fm username");
            footer.AppendLine($"To remove: {prefix}unblock mention/user id/Last.fm username");
        }
        else
        {
            response.Embed.WithTitle($"Blocked users in {context.DiscordGuild.Name}");
            footer.AppendLine($"To add: {prefix}block mention/user id/Last.fm username");
            footer.AppendLine($"To remove: {prefix}unblock mention/user id/Last.fm username");
        }

        var pages = new List<PageBuilder>();
        var pageCounter = 1;

        if (!string.IsNullOrWhiteSpace(searchValue))
        {
            searchValue = searchValue.ToLower();

            guildUsers = guildUsers
                .Where(w => w.Value.UserName.ToLower().Contains(searchValue) ||
                            w.Value.DiscordUserId.ToString().Contains(searchValue) ||
                            w.Value.UserNameLastFM.ToLower().Contains(searchValue))
                .ToDictionary(i => i.Key, i => i.Value);
        }

        if (guildUsers != null &&
            guildUsers.Any(a => includeCrownBlocked ? a.Value.BlockedFromCrowns || a.Value.BlockedFromWhoKnows : a.Value.BlockedFromWhoKnows))
        {
            guildUsers = guildUsers
                .Where(w => includeCrownBlocked ? w.Value.BlockedFromCrowns || w.Value.BlockedFromWhoKnows : w.Value.BlockedFromWhoKnows)
                .ToDictionary(i => i.Key, i => i.Value);

            var userPages = guildUsers.Select(s => s.Value).Chunk(15);

            foreach (var userPage in userPages)
            {
                var description = new StringBuilder();

                foreach (var blockedUser in userPage)
                {
                    if (blockedUser.BlockedFromCrowns && !blockedUser.BlockedFromWhoKnows)
                    {
                        description.Append("<:crownblocked:1075892343552618566> ");
                    }
                    else
                    {
                        description.Append("🚫 ");
                    }

                    description.AppendLine(
                        $"`{blockedUser.DiscordUserId}` — **{StringExtensions.Sanitize(blockedUser.UserName)}** — [`{blockedUser.UserNameLastFM}`]({LastfmUrlExtensions.GetUserUrl(blockedUser.UserNameLastFM)}) ");
                }

                pages.Add(new PageBuilder()
                    .WithDescription(description.ToString())
                    .WithColor(DiscordConstants.InformationColorBlue)
                    .WithAuthor(response.Embed.Title)
                    .WithFooter($"Page {pageCounter}/{userPages.Count()} - {guildUsers.Count} total\n" +
                                footer));
                pageCounter++;
            }
        }
        else
        {
            pages.Add(new PageBuilder()
                .WithDescription("No blocked users in this server or no results for your search.")
                .WithAuthor(response.Embed.Title)
                .WithFooter(footer.ToString()));
        }

        response.ComponentPaginator = StringService.BuildComponentPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> ToggleGuildCommand(ContextModel context, NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var currentlyDisabled = new StringBuilder();

        var currentDisabledCommands = guild?.DisabledCommands?.ToList();

        if (currentDisabledCommands != null)
        {
            var maxNewCommandsToDisplay = currentDisabledCommands.Count > 32 ? 32 : currentDisabledCommands.Count;
            for (var index = 0; index < maxNewCommandsToDisplay; index++)
            {
                var newDisabledCommand = currentDisabledCommands[index];
                currentlyDisabled.Append($"`{newDisabledCommand}` ");
            }

            if (currentDisabledCommands.Count > 32)
            {
                currentlyDisabled.Append($" and {currentDisabledCommands.Count - 32} other commands");
            }
        }

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay($"## Toggle server commands - {context.DiscordGuild.Name}");
        container.WithSeparator();

        container.WithTextDisplay(
            $"**Disabled commands**\n{(currentlyDisabled.Length > 0 ? currentlyDisabled.ToString() : "✅ All commands enabled.")}");

        container.WithTextDisplay(lastModifier != null
            ? $"-# Last modified by {lastModifier.Username}"
            : "-# Commands disabled here will be disabled throughout the whole server");

        container.WithSeparator();

        var buttons = new ActionRowProperties();
        buttons.AddComponents(new ButtonProperties(InteractionConstants.ToggleCommand.ToggleGuildCommandAdd, "Add",
            ButtonStyle.Secondary));
        buttons.AddComponents(new ButtonProperties(InteractionConstants.ToggleCommand.ToggleGuildCommandRemove, "Remove",
            ButtonStyle.Secondary)
        {
            Disabled = currentlyDisabled.Length == 0
        });
        buttons.AddComponents(new ButtonProperties(InteractionConstants.ToggleCommand.ToggleGuildCommandClear, "Clear",
            ButtonStyle.Secondary)
        {
            Disabled = currentlyDisabled.Length == 0
        });

        container.WithActionRow(buttons);

        return response;
    }

    public async Task<ResponseModel> ToggleChannelCommand(ContextModel context, ulong selectedChannelId, ulong? selectedCategoryId = null,
        NetCord.User lastModifier = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var selectedChannel = context.DiscordGuild.Channels.TryGetValue(selectedChannelId, out var ch) ? ch : null;

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay($"## Toggle channel commands - #{selectedChannel?.Name}");
        container.WithSeparator();

        var footer = new StringBuilder();

        var channel = await guildService.GetChannel(selectedChannel.Id);
        var botDisabled = channel?.BotDisabled == true;

        selectedCategoryId ??= (selectedChannel as TextGuildChannel)?.ParentId;

        var currentlyDisabled = new StringBuilder();

        var currentToggledCommands = channel?.DisabledCommands?.ToList();

        if (currentToggledCommands != null)
        {
            var maxNewCommandsToDisplay = currentToggledCommands.Count > 32 ? 32 : currentToggledCommands.Count;
            for (var index = 0; index < maxNewCommandsToDisplay; index++)
            {
                var newDisabledCommand = currentToggledCommands[index];
                currentlyDisabled.Append($"`{newDisabledCommand}` ");
            }

            if (currentToggledCommands.Count > 32)
            {
                currentlyDisabled.Append($" and {currentToggledCommands.Count - 32} other commands");
            }
        }

        var fmType = new StringMenuProperties($"{InteractionConstants.ToggleCommand.ToggleCommandChannelFmType}:{selectedChannel.Id}:{selectedCategoryId}")
            .WithPlaceholder("Forced channel 'fm' mode")
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var option in Enum.GetValues<FmEmbedType>().OrderBy(o => o.GetAttribute<OptionOrderAttribute>().Order))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var description = option.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(option);

            var active = option == channel?.FmEmbedType;

            fmType.AddOption(name, value, description: description, isDefault: active);
        }

        if (!botDisabled)
        {
            container.WithTextDisplay(
                $"**Disabled commands**\n{(currentlyDisabled.Length > 0 ? currentlyDisabled.ToString() : "✅ All commands enabled.")}");

            footer.AppendLine("-# All commands enabled except for those explicitly disabled");
        }
        else
        {
            container.WithTextDisplay(
                $"**Enabled commands**\n{(currentlyDisabled.Length > 0 ? currentlyDisabled.ToString() : "🚫 All commands disabled.")}");

            footer.AppendLine("-# All commands disabled except for those explicitly enabled");
        }

        if (channel is { FmEmbedType: not null })
        {
            var name = channel.FmEmbedType.GetAttribute<OptionAttribute>().Name;

            container.WithTextDisplay($"**Forced 'fm' mode**\n`{name}`");
        }

        if (channel?.RecommendedAlternativeChannelIds is { Length: > 0 })
        {
            container.WithTextDisplay(
                $"**Recommended alternative channels**\n{string.Join(", ", channel.RecommendedAlternativeChannelIds.Select(s => $"<#{s}>"))}");
        }

        var missingPermissions = GetMissingBotPermissionsInChannel(context.DiscordGuild, selectedChannel);
        if (missingPermissions.Length > 0)
        {
            container.WithTextDisplay($"**Missing permissions in this channel**\n{missingPermissions}");
        }

        if (lastModifier != null)
        {
            footer.AppendLine($"-# Last modified by {lastModifier.Username}");
        }

        container.WithTextDisplay(footer.ToString());

        container.WithSeparator();

        var firstRow = new ActionRowProperties();
        firstRow.AddComponents(new ButtonProperties(
            $"{InteractionConstants.ToggleCommand.ToggleCommandAdd}:{selectedChannel.Id}:{selectedCategoryId}", "Add",
            ButtonStyle.Secondary));
        firstRow.AddComponents(new ButtonProperties(
            $"{InteractionConstants.ToggleCommand.ToggleCommandRemove}:{selectedChannel.Id}:{selectedCategoryId}", "Remove",
            ButtonStyle.Secondary)
        {
            Disabled = currentlyDisabled.Length == 0
        });
        firstRow.AddComponents(new ButtonProperties(
            $"{InteractionConstants.ToggleCommand.ToggleCommandClear}:{selectedChannel.Id}:{selectedCategoryId}", "Clear",
            ButtonStyle.Secondary)
        {
            Disabled = currentlyDisabled.Length == 0
        });

        if (!botDisabled)
        {
            firstRow.AddComponents(new ButtonProperties(
                $"{InteractionConstants.ToggleCommand.ToggleCommandDisableAll}:{selectedChannel.Id}:{selectedCategoryId}",
                "Disable all commands", ButtonStyle.Secondary));
        }
        else
        {
            firstRow.AddComponents(new ButtonProperties(
                $"{InteractionConstants.ToggleCommand.ToggleCommandEnableAll}:{selectedChannel.Id}:{selectedCategoryId}",
                "Enable all commands", ButtonStyle.Secondary));
        }

        var channelPicker = new ChannelMenuProperties(InteractionConstants.ToggleCommand.ToggleCommandPickChannel)
            .WithPlaceholder("Pick a channel")
            .WithMinValues(1)
            .WithMaxValues(1)
            .WithChannelTypes([
                ChannelType.TextGuildChannel, ChannelType.AnnouncementGuildChannel,
                ChannelType.PublicGuildThread, ChannelType.PrivateGuildThread,
                ChannelType.AnnouncementGuildThread
            ]);
        channelPicker.DefaultValues = [selectedChannel.Id];

        container.AddComponent(channelPicker);

        container.WithActionRow(firstRow);

        var fmToggled = currentToggledCommands != null &&
                        currentToggledCommands.Any(a => string.Equals(a, "fm", StringComparison.OrdinalIgnoreCase));

        if (!botDisabled)
        {
            fmType.Disabled = fmToggled;
            container.AddComponent(fmType);
        }
        else if (fmToggled)
        {
            container.AddComponent(fmType);
        }

        var recommendedChannels = new ChannelMenuProperties(
                $"{InteractionConstants.ToggleCommand.ToggleCommandRecommendedChannel}:{selectedChannel.Id}:{selectedCategoryId}")
            .WithPlaceholder("Recommended alternative channels")
            .WithMinValues(0)
            .WithMaxValues(5)
            .WithChannelTypes([
                ChannelType.TextGuildChannel, ChannelType.AnnouncementGuildChannel,
                ChannelType.PublicGuildThread, ChannelType.PrivateGuildThread,
                ChannelType.AnnouncementGuildThread
            ]);

        if (channel?.RecommendedAlternativeChannelIds is { Length: > 0 })
        {
            recommendedChannels.DefaultValues = channel.RecommendedAlternativeChannelIds;
        }

        container.AddComponent(recommendedChannels);

        return response;
    }

    public async Task<ResponseModel> ToggleCrowns(ContextModel context, bool? disabled = null)
    {
        if (disabled.HasValue)
        {
            await guildService.ToggleCrownsAsync(context.DiscordGuild, disabled.Value);
        }

        return await ServerSettingsSection(context, GuildSettingSection.Crowns);
    }

    private StringBuilder GetMissingBotPermissionsInChannel(Guild guild, IGuildChannel channel)
    {
        var missing = new StringBuilder();
        if (guild == null || channel == null)
        {
            return missing;
        }

        var botUserId = client.GetCurrentUser()?.Id;
        if (!botUserId.HasValue || !guild.Users.TryGetValue(botUserId.Value, out var botUser))
        {
            return missing;
        }

        var guildPermissions = botUser.GetPermissions(guild);
        var perms = botUser.GetChannelPermissions(guildPermissions, channel);

        if (!perms.HasFlag(Permissions.ViewChannel))
        {
            missing.AppendLine("❌ View channel");
        }

        if (!perms.HasFlag(Permissions.SendMessages))
        {
            missing.AppendLine("❌ Send messages");
        }

        if (!perms.HasFlag(Permissions.EmbedLinks))
        {
            missing.AppendLine("❌ Embed links");
        }

        if (!perms.HasFlag(Permissions.AttachFiles))
        {
            missing.AppendLine("❌ Attach files");
        }

        if (!perms.HasFlag(Permissions.AddReactions))
        {
            missing.AppendLine("❌ Add reactions");
        }

        if (!perms.HasFlag(Permissions.UseExternalEmojis))
        {
            missing.AppendLine("❌ Use external emojis");
        }

        return missing;
    }
}
