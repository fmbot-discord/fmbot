using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
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

    public async Task<ResponseModel> GetGuildSettings(ContextModel context)
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


        var guildSettings = new SelectMenuBuilder()
            .WithPlaceholder("Select server setting you want to change")
            .WithCustomId(Constants.GuildSetting)
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

    public async Task RespondToPrefixSetter(IInteractionContext context)
    {
        if (!await UserIsAllowed(context))
        {
            await UserNotAllowedResponse(context);
            return;
        }

        await context.Interaction.RespondWithModalAsync<PrefixModal>(Constants.TextPrefixModal);
    }

    public class PrefixModal : IModal
    {
        public string Title => "Set .fmbot text command prefix";

        [InputLabel("Enter new prefix")]
        [ModalTextInput("new_prefix", placeholder: ".", minLength: 1, maxLength: 15)]
        public string NewPrefix { get; set; }
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

    public async Task<ResponseModel> GuildMode(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var guild = await this._guildService.GetGuildAsync(context.DiscordGuild.Id);

        var fmType = new SelectMenuBuilder()
            .WithPlaceholder("Select embed type")
            .WithCustomId(Constants.FmGuildSettingType)
            .WithMinValues(0)
            .WithMaxValues(1);

        foreach (var name in Enum.GetNames(typeof(FmEmbedType)).OrderBy(o => o))
        {
            fmType.AddOption(new SelectMenuOptionBuilder(name, name));
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

    public async Task<ResponseModel> SetGuildMode(ContextModel context, FmEmbedType? embedType)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        await this._guildService.ChangeGuildSettingAsync(context.DiscordGuild, embedType);

        if (embedType.HasValue)
        {
            response.Embed.WithDescription($"The default .fm mode for your server has been set to **{embedType}**.\n\n" +
                             $"All .fm commands in this server will use this mode regardless of user settings, so make sure to inform your users of this change.");
        }
        else
        {
            response.Embed.WithDescription(
                $"The default .fm mode has been disabled for this server. Users can now set their own mode using `fmmode`.");
        }

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }
}
