using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.Builders;

public class GuildSettingBuilder
{
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly BotSettings _botSettings;
    private readonly AdminService _adminService;

    public GuildSettingBuilder(GuildService guildService, IOptions<BotSettings> botSettings, AdminService adminService, IPrefixService prefixService)
    {
        this._guildService = guildService;
        this._adminService = adminService;
        this._prefixService = prefixService;
        this._botSettings = botSettings.Value;
    }

    public async Task<ResponseModel> GetGuildSettings(ContextModel context, GuildPermissions guildPermissions)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);
        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        response.Embed.WithTitle($".fmbot settings - {guild.Name}");
        response.Embed.WithFooter($"{guild.DiscordGuildId}");

        var settings = new StringBuilder();

        settings.Append("Text command prefix: ");
        if (guild.Prefix != null)
        {
            settings.Append($"`{guild.Prefix}`");
        }
        else
        {
            settings.Append($"`{this._botSettings.Bot.Prefix}` (default)");
        }
        settings.AppendLine();

        var whoKnowsSettings = new StringBuilder();

        whoKnowsSettings.AppendLine(
            $"**{guildUsers?.Count(c => c.BlockedFromWhoKnows) ?? 0}** users blocked from WhoKnows and server charts.");

        if (guild.ActivityThresholdDays.HasValue)
        {
            whoKnowsSettings.Append($"Users must have used .fmbot in the last **{guild.ActivityThresholdDays}** days to be visible.");
        }
        else
        {
            whoKnowsSettings.AppendLine("There is no activity requirement set for being visible.");
        }

        response.Embed.AddField("WhoKnows settings", whoKnowsSettings.ToString());

        var crownSettings = new StringBuilder();
        if (guild.CrownsDisabled == true)
        {
            crownSettings.Append("Crown functionality has been disabled on this server.");

        }
        else
        {
            crownSettings.AppendLine(
                "Users earn crowns whenever they're the #1 user for an artist. ");

            crownSettings.AppendLine(
                $"**{guildUsers?.Count(c => c.BlockedFromCrowns) ?? 0}** users are blocked from earning crowns.");

            crownSettings.AppendLine();

            crownSettings.Append($"The minimum playcount for a crown is set to **{guild.CrownsMinimumPlaycountThreshold ?? Constants.DefaultPlaysForCrown}** or higher");

            if (guild.CrownsMinimumPlaycountThreshold == null)
            {
                crownSettings.Append(
                    " (default)");
            }

            crownSettings.Append(". ");

            if (guild.CrownsActivityThresholdDays.HasValue)
            {
                crownSettings.Append($"Users must have used .fmbot in the last **{guild.CrownsActivityThresholdDays}** days to earn crowns.");
            }
            else
            {
                crownSettings.Append("There is no activity requirement set for earning crowns.");
            }
        }

        response.Embed.AddField("Crown settings", crownSettings.ToString());

        var emoteReactions = new StringBuilder();
        if (guild.EmoteReactions == null || !guild.EmoteReactions.Any())
        {
            emoteReactions.AppendLine("No automatic reactions enabled for `fm` and `featured`.");
        }
        else
        {
            emoteReactions.Append("Automatic `fm` and `featured` reactions:");
            foreach (var reaction in guild.EmoteReactions)
            {
                emoteReactions.Append($"{reaction} ");
            }
        }
        response.Embed.AddField("Emote reactions", emoteReactions.ToString());

        if (guild.DisabledCommands != null && guild.DisabledCommands.Any())
        {
            var disabledCommands = new StringBuilder();
            disabledCommands.Append($"Disabled commands: ");
            foreach (var disabledCommand in guild.DisabledCommands)
            {
                disabledCommands.Append($"`{disabledCommand}` ");
            }

            response.Embed.AddField("Server-wide disabled commands", disabledCommands.ToString());
        }
        response.Embed.WithDescription(settings.ToString());

        var serverPermission = new StringBuilder();
        if (!guildPermissions.SendMessages)
        {
            serverPermission.AppendLine("❌ Send messages");
        }
        if (!guildPermissions.AttachFiles)
        {
            serverPermission.AppendLine("❌ Attach files");
        }
        if (!guildPermissions.EmbedLinks)
        {
            serverPermission.AppendLine("❌ Embed links");
        }
        if (!guildPermissions.AddReactions)
        {
            serverPermission.AppendLine("❌ Add reactions");
        }
        if (!guildPermissions.UseExternalEmojis)
        {
            serverPermission.AppendLine("❌ Use external emojis");
        }

        if (serverPermission.Length > 0)
        {
            response.Embed.AddField("Missing server-wide permissions", serverPermission.ToString());
        }

        var guildSettings = new SelectMenuBuilder()
            .WithPlaceholder("Select server setting you want to change")
            .WithCustomId(InteractionConstants.GuildSetting)
            .WithMaxValues(1);

        foreach (var setting in ((GuildSetting[])Enum.GetValues(typeof(GuildSetting))))
        {
            var name = setting.GetAttribute<OptionAttribute>().Name;
            var description = setting.GetAttribute<OptionAttribute>().Description;
            var value = Enum.GetName(setting);

            guildSettings.AddOption(new SelectMenuOptionBuilder(name, $"gs-view-{value}", description));
        }

        response.Components = new ComponentBuilder()
            .WithSelectMenu(guildSettings);

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;

    }

    public async Task<ResponseModel> SetPrefix(IInteractionContext context, string newPrefix)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var description = new StringBuilder();

        if (newPrefix != this._botSettings.Bot.Prefix)
        {
            description.AppendLine("Prefix has successfully been changed.");
            await this._guildService.SetGuildPrefixAsync(context.Guild, newPrefix);
            this._prefixService.StorePrefix(newPrefix, context.Guild.Id);
        }
        else
        {
            description.AppendLine("Prefix has been set to default.");
            await this._guildService.SetGuildPrefixAsync(context.Guild, null);
            this._prefixService.RemovePrefix(context.Guild.Id);
        }

        description.AppendLine();
        description.AppendLine($"New prefix: `{newPrefix}`");
        description.AppendLine();
        description.AppendLine("Examples:");
        description.AppendLine($"`{newPrefix}fm`");
        description.AppendLine($"`{newPrefix}whoknows`");
        description.AppendLine();
        description.AppendLine("The bot will no longer respond to any text commands without this prefix. " +
                               "Consider letting other users in your server know.");

        response.Embed.WithTitle("Set text command prefix");
        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public async Task<bool> UserIsAllowed(IInteractionContext context)
    {
        if (context.Guild == null)
        {
            return false;
        }

        var guildUser = (IGuildUser)context.User;

        if (guildUser.GuildPermissions.BanMembers ||
            guildUser.GuildPermissions.Administrator)
        {
            return true;
        }

        if (await this._adminService.HasCommandAccessAsync(context.User, UserType.Admin))
        {
            return true;
        }

        //var fmbotManagerRole = context.Guild.Roles
        //    .FirstOrDefault(f => f.Name?.ToLower() == ".fmbot manager");
        //if (fmbotManagerRole != null &&
        //    guildUser.RoleIds.Any(a => a == fmbotManagerRole.Id))
        //{
        //    return true;
        //}

        return false;
    }

    public async Task<bool> UserNotAllowedResponse(IInteractionContext context)
    {
        var response = new StringBuilder();
        response.AppendLine("You are not authorized to change this .fmbot setting.");
        response.AppendLine();
        response.AppendLine("To change .fmbot settings, you must have the `Ban Members` permission or be an administrator.");
        //response.AppendLine("- A role with the name `.fmbot manager`");

        await context.Interaction.RespondAsync(response.ToString(), ephemeral: true);

        return false;
    }

    public async Task<ResponseModel> BlockedUsersAsync(
        ContextModel context,
        bool crownBlockedOnly = false,
        string searchValue = null)

    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator,
        };

        var guildUsers = await this._guildService.GetGuildUsers(context.DiscordGuild.Id);

        var footer = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(searchValue))
        {
            footer.AppendLine($"Showing results with '{Format.Sanitize(searchValue)}'");
        }

        footer.AppendLine($"Block type — Discord ID — Name — Last.fm");

        if (crownBlockedOnly)
        {
            response.Embed.WithTitle($"Crownblocked users in {context.DiscordGuild.Name}");
            footer.AppendLine($"To add: {context.Prefix}crownblock mention/user id/Last.fm username");
            footer.AppendLine($"To remove: {context.Prefix}unblock mention/user id/Last.fm username");
        }
        else
        {
            response.Embed.WithTitle($"Blocked users in {context.DiscordGuild.Name}");
            footer.AppendLine($"To add: {context.Prefix}block mention/user id/Last.fm username");
            footer.AppendLine($"To remove: {context.Prefix}unblock mention/user id/Last.fm username");
        }

        var pages = new List<PageBuilder>();
        var pageCounter = 1;

        if (!string.IsNullOrWhiteSpace(searchValue))
        {
            searchValue = searchValue.ToLower();

            guildUsers = guildUsers
                .Where(w => w.UserName.ToLower().Contains(searchValue) ||
                            w.DiscordUserId.ToString().Contains(searchValue) ||
                            w.UserNameLastFM.ToLower().Contains(searchValue))
                .ToList();
        }

        if (guildUsers != null &&
            guildUsers.Any(a => a.BlockedFromWhoKnows && (!crownBlockedOnly || a.BlockedFromCrowns)))
        {
            guildUsers = guildUsers
                .Where(w => w.BlockedFromCrowns && (crownBlockedOnly || w.BlockedFromWhoKnows))
                .ToList();

            var userPages = guildUsers.Chunk(15);

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
                        $"`{blockedUser.DiscordUserId}` — **{Format.Sanitize(blockedUser.UserName)}** — [`{blockedUser.UserNameLastFM}`]({Constants.LastFMUserUrl}{blockedUser.UserNameLastFM}) ");
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

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        return response;
    }

    public async Task<ResponseModel> ActivityThreshold(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        response.Embed.WithTitle("Set server activity threshold");

        var description = new StringBuilder();
        description.AppendLine("Select a forced mode for the `fm` command for everyone in this server.");
        description.AppendLine("This will override whatever mode a user has set themselves.");
        description.AppendLine();
        description.AppendLine("To disable, simply de-select the mode you have selected.");
        description.AppendLine();

        if (guild.FmEmbedType.HasValue)
        {
            description.AppendLine($"Current mode: **{guild.FmEmbedType}**.");
        }
        else
        {
            description.AppendLine($"Current mode: None");
        }

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder().WithButton();

        return response;
    }

    public async Task<ResponseModel> GuildMode(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var fmType = new SelectMenuBuilder()
            .WithPlaceholder("Select default server embed type")
            .WithCustomId(InteractionConstants.FmGuildSettingType)
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var name in Enum.GetNames(typeof(FmEmbedType)).OrderBy(o => o))
        {
            var selected = name == guild.FmEmbedType.ToString();
            fmType.AddOption(new SelectMenuOptionBuilder(name, name, isDefault: selected));
        }

        response.Embed.WithTitle("Set server 'fm' mode");

        var description = new StringBuilder();
        description.AppendLine("Select a forced mode for the `fm` command for everyone in this server.");
        description.AppendLine("This will override whatever mode a user has set themselves.");
        description.AppendLine();
        description.AppendLine("To disable, simply de-select the mode you have selected.");
        description.AppendLine();

        if (guild.FmEmbedType.HasValue)
        {
            description.AppendLine($"Current mode: **{guild.FmEmbedType}**.");
        }
        else
        {
            description.AppendLine($"Current mode: None");
        }

        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder().WithSelectMenu(fmType);

        return response;
    }

    public static ResponseModel GuildReactionsAsync(ContextModel context, string prfx)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        description.Append(
            $"Use the `{prfx}serverreactions` command for automatic emoji reacts for `fm` and `featured`. ");
        description.AppendLine("To disable, use without any emojis.");
        description.AppendLine();
        description.AppendLine("Make sure that you have a space between each emoji.");
        description.AppendLine();
        description.AppendLine("Examples:");
        description.AppendLine($"`{prfx}serverreactions :PagChomp: :PensiveBlob:`");
        description.AppendLine($"`{prfx}serverreactions 😀 😯 🥵`");

        response.Embed.WithDescription(description.ToString());

        return response;
    }

    public async Task<ResponseModel> ToggleChannelCommand(ContextModel context, ulong selectedChannelId, ulong? selectedCategoryId = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var selectedChannel = await context.DiscordGuild.GetChannelAsync(selectedChannelId);

        response.Embed.WithTitle($"Toggle channel commands - #{selectedChannel.Name}");
        response.Embed.WithFooter("Use the up and down selector to browse through channels");

        var channelDescription = new StringBuilder();

        var categories = await context.DiscordGuild.GetCategoriesAsync();

        ulong previousChannelId = 0;
        ulong previousCategoryId = 0;
        ulong nextChannelId = 0;
        ulong nextCategoryId = 0;

        var channel = await this._guildService.GetChannel(selectedChannel.Id);
        var botDisabled = channel?.BotDisabled == true;

        for (var i = 0; i < categories.Count; i++)
        {
            var previousCategory = categories.OrderBy(o => o.Position).ElementAtOrDefault(i - 1);
            var currentCategory = categories.OrderBy(o => o.Position).ElementAt(i);
            var nextCategory = categories.OrderBy(o => o.Position).ElementAtOrDefault(i + 1);

            if (currentCategory is not SocketCategoryChannel currentSocketCategory)
            {
                break;
            }

            var categoryChannels = currentSocketCategory.GetCategoryChannelPositions();

            if (!selectedCategoryId.HasValue && categoryChannels.Any(a => a.Key.Id == selectedChannelId))
            {
                selectedCategoryId = currentCategory.Id;
            }

            if (selectedCategoryId == currentCategory.Id)
            {
                channelDescription.AppendLine($"***`{currentCategory.Name.ToUpper()}`*** - {categoryChannels.Count}");

                for (var j = 0; j < categoryChannels.Count; j++)
                {
                    var previousChannel = categoryChannels.Keys.ElementAtOrDefault(j - 1);
                    var currentChannel = categoryChannels.Keys.ElementAt(j);
                    var nextChannel = categoryChannels.Keys.ElementAtOrDefault(j + 1);

                    if (currentChannel.Id == selectedChannelId)
                    {
                        if (previousChannel != null)
                        {
                            channelDescription.AppendLine($"{DiscordConstants.OneToFiveUp} **<#{previousChannel.Id}>**");
                        }

                        channelDescription.AppendLine($"{DiscordConstants.SamePosition} **<#{currentChannel.Id}>** **<**");

                        if (nextChannel != null)
                        {
                            channelDescription.AppendLine($"{DiscordConstants.OneToFiveDown} **<#{nextChannel.Id}>**");
                        }

                        if (previousChannel != null)
                        {
                            previousChannelId = previousChannel.Id;
                            previousCategoryId = currentCategory.Id;
                        }
                        else if (previousCategory is SocketCategoryChannel previousSocketCategory)
                        {
                            var previousCategoryChannels = previousSocketCategory.GetCategoryChannelPositions();
                            if (previousCategoryChannels.Keys.Any())
                            {
                                previousCategoryId = previousSocketCategory.Id;
                                previousChannelId = previousCategoryChannels.Keys.Last().Id;
                            }
                        }

                        if (nextChannel != null)
                        {
                            nextChannelId = nextChannel.Id;
                            nextCategoryId = currentCategory.Id;
                        }
                        else if (nextCategory is SocketCategoryChannel nextSocketCategory)
                        {
                            var nextCategoryChannels = nextSocketCategory.GetCategoryChannelPositions();
                            if (nextCategoryChannels.Keys.Any())
                            {
                                nextCategoryId = nextSocketCategory.Id;
                                nextChannelId = nextCategoryChannels.Keys.First().Id;
                            }
                        }
                    }

                }
            }

            channelDescription.AppendLine();
        }

        response.Embed.WithDescription(channelDescription.ToString());

        var currentlyDisabled = new StringBuilder();

        var currentDisabledCommands = channel?.DisabledCommands?.ToList();

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

        if (!botDisabled)
        {
            response.Embed.AddField("Disabled commands", currentlyDisabled.Length > 0 ? currentlyDisabled.ToString() : "✅ All commands enabled.");
        }
        else
        {
            response.Embed.AddField("Disabled commands", "🚫 The bot is fully disabled in this channel.");
        }

        var upDisabled = previousCategoryId == 0 || previousChannelId == 0;
        var downDisabled = nextCategoryId == 0 || nextChannelId == 0;

        var components = new ComponentBuilder()
            .WithButton(null, $"{InteractionConstants.ToggleCommandMove}-{previousChannelId}-{previousCategoryId}", style: ButtonStyle.Secondary, Emote.Parse(DiscordConstants.OneToFiveUp), disabled: upDisabled)
            .WithButton(null, $"{InteractionConstants.ToggleCommandMove}-{nextChannelId}-{nextCategoryId}", style: ButtonStyle.Secondary, Emote.Parse(DiscordConstants.OneToFiveDown), disabled: downDisabled, row: 1)
            .WithButton("Add", $"{InteractionConstants.ToggleCommandAdd}-{selectedChannel.Id}-{selectedCategoryId}", style: ButtonStyle.Secondary, disabled: botDisabled)
            .WithButton("Remove", $"{InteractionConstants.ToggleCommandRemove}-{selectedChannel.Id}-{selectedCategoryId}", style: ButtonStyle.Secondary, disabled: botDisabled || currentlyDisabled.Length == 0)
            .WithButton("Clear", $"{InteractionConstants.ToggleCommandClear}-{selectedChannel.Id}-{selectedCategoryId}", style: ButtonStyle.Secondary, disabled: botDisabled || currentlyDisabled.Length == 0);

        if (!botDisabled)
        {
            components
                .WithButton("Disable bot in channel", $"{InteractionConstants.ToggleCommandDisableAll}-{selectedChannel.Id}-{selectedCategoryId}", style: ButtonStyle.Secondary, row: 1);
        }
        else
        {
            components
                .WithButton("Enable bot in channel", $"{InteractionConstants.ToggleCommandEnableAll}-{selectedChannel.Id}-{selectedCategoryId}", style: ButtonStyle.Primary, row: 1);
        }

        response.Components = components;

        return response;
    }
}
