using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork.Migrations;
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

        return response;

    }

    public async Task RespondToPrefixSetter(IInteractionContext context)
    {
        if (!await UserIsAllowed(context))
        {
            await UserNotAllowedResponse(context);
            return;
        }

        var mb = new ModalBuilder()
            .WithTitle("Set .fmbot text command prefix")
            .WithCustomId($"gs-set-{Enum.GetName(GuildSetting.TextPrefix)}")
            .AddTextInput("Enter new prefix", "prefix", placeholder: ".", minLength: 1, maxLength: 15, required: true);

        await context.Interaction.RespondWithModalAsync(mb.Build());
    }

    public async Task RespondWithPrefixSet(IInteractionContext context, string newPrefix)
    {
        if (!await UserIsAllowed(context))
        {
            await UserNotAllowedResponse(context);
            return;
        }

        var embed = new EmbedBuilder();
        var description = new StringBuilder();

        Guild guild;
        if (newPrefix != this._botSettings.Bot.Prefix)
        {
            description.AppendLine("Prefix for all text commands has successfully been changed.");
            guild = await this._guildService.SetGuildPrefixAsync(context.Guild, newPrefix);
            this._prefixService.StorePrefix(newPrefix, context.Guild.Id);
        }
        else
        {
            description.AppendLine("Prefix for all text commands has been set to default.");
            guild = await this._guildService.SetGuildPrefixAsync(context.Guild, null);
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

        embed.WithDescription(description.ToString());

        await context.Interaction.RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    private async Task<bool> UserIsAllowed(IInteractionContext context)
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

    private static async Task<bool> UserNotAllowedResponse(IInteractionContext context)
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
}
