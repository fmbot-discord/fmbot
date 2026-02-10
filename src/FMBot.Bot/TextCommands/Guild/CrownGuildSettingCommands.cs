using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands.Guild;

[ModuleName("Crown settings")]
[ServerStaffOnly]
public class CrownGuildSettingCommands(
    IPrefixService prefixService,
    GuildService guildService,
    AdminService adminService,
    SettingService settingService,
    CrownService crownService,
    UserService userService,
    IOptions<BotSettings> botSettings,
    InteractiveService interactivity,
    GuildSettingBuilder guildSettingBuilder)
    : BaseCommandModule(botSettings)
{
    private InteractiveService Interactivity { get; } = interactivity;


    [Command("crownthreshold", "setcrownthreshold", "setcwthreshold", "cwthreshold", "crowntreshold")]
    [Summary("Sets amount of plays before someone can earn a crown in your server")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SetCrownPlaycountThresholdAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("crownactivitythreshold", "setcrownactivitythreshold", "setcwactivitythreshold", "cwactivitythreshold", "crownactivitytreshold")]
    [Summary("Sets amount of days to filter out users from earning crowns for inactivity. " +
             "Inactivity is counted by the last date that someone has used .fmbot")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SetCrownActivityThresholdAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("crownblock", "crownblockuser", "crownban", "cwblock", "cwban", "crownbanuser", "crownbanmember")]
    [Summary("Block a user from gaining any crowns in your server")]
    [Options(Constants.UserMentionExample)]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task GuildBlockUserFromCrownsAsync([CommandParameter(Remainder = true)] string user = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        if (user == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                { Content = "Please mention a user, enter a Discord ID, or enter a Last.fm username to block from gaining crowns on your server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var userToBlock = await settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var guildUsers = await guildService.GetGuildUsers(this.Context.Guild.Id);

        if (!guildUsers.TryGetValue(userToBlock.UserId, out var guildUser))
        {
            var similarUsers = await adminService.GetUsersWithLfmUsernameAsync(userToBlock.UserNameLastFM);

            var userInThisServer = similarUsers.FirstOrDefault(f =>
                f.UserNameLastFM.ToLower() == userToBlock.UserNameLastFM.ToLower() && guildUsers.ContainsKey(f.UserId));

            if (userInThisServer == null)
            {
                await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                {
                    Content =
                        $"User not found. Are you sure they are in this server?\nTo refresh the cached memberlist on your server, use `{prfx}refreshmembers`."
                });
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
                return;
            }

            userToBlock = userInThisServer;
        }

        if (guildUser == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
            {
                Content = $"User not found. Are you sure they are in this server?\nTo refresh the cached memberlist on your server, use `{prfx}refreshmembers`."
            });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        if (guildUser.BlockedFromCrowns)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                { Content = "The user you're trying to block from gaining crowns has already been blocked on this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var userBlocked = await guildService.CrownBlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

        if (userBlocked)
        {
            this._embed.WithTitle("Added crownblocked user");
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");

            this._embed.WithFooter($"See all crownblocked users with {prfx}crownblockedusers");

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed] });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        else
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                { Content = "Something went wrong while attempting to crownblock user, please contact .fmbot staff." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Error }, userService);
        }
    }

    [Command("removeusercrowns", "deleteusercrowns", "deleteusercrown", "removeusercrown", "removeusercws", "deleteusercws", "usercrownsdelete",
        "usercrownsremove", "killusercrowns")]
    [Summary("Removes crowns from a user")]
    [Options(Constants.UserMentionExample)]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task RemoveUserCrownsAsync([CommandParameter(Remainder = true)] string user = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        if (user == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties
                { Content = "Please mention a user, enter a Discord ID, or enter a Last.fm username to remove their crowns from." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        var userToBlock = await settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "User not found. Are you sure they are registered in .fmbot?" });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotFound }, userService);
            return;
        }

        try
        {
            this._embed.WithTitle("Are you sure you want to delete all crowns for this user?");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");
            this._embed.WithFooter($"Expires in 30 seconds..");

            var builder = new ActionRowProperties()
                .WithButton("Confirm", "id");

            var msg = await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Embeds = [this._embed], Components = [builder] });

            var result = await this.Interactivity.NextInteractionAsync(x =>
                    x is MessageComponentInteraction c && c.Message.Id == msg.Id && x.User.Id == this.Context.User.Id,
                timeout: TimeSpan.FromSeconds(30));

            if (result.IsSuccess)
            {
                await result.Value.SendResponseAsync(InteractionCallback.DeferredModifyMessage);
                await crownService.RemoveAllCrownsFromDiscordUser(userToBlock.DiscordUserId, this.Context.Guild.Id);

                this._embed.WithTitle("Crowns have been removed for:");
                this._embed.Footer = null;
                await msg.ModifyAsync(x =>
                {
                    x.Embeds = [this._embed];
                    x.Components = [new ActionRowProperties()];
                    x.AllowedMentions = AllowedMentionsProperties.None;
                });
            }
            else
            {
                this._embed.WithTitle("Crown removal timed out");
                await msg.ModifyAsync(x =>
                {
                    x.Embeds = [this._embed];
                    x.Components = [new ActionRowProperties()];
                    x.AllowedMentions = AllowedMentionsProperties.None;
                });
            }

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("crownblockedusers", "crownblocked", "crownbanned", "crownbannedusers", "crownbannedmembers")]
    [Summary("View all users that are blocked from earning crowns in your server")]
    [GuildOnly]
    [RequiresIndex]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task CrownBlockedUsersAsync([CommandParameter(Remainder = true)] string searchValue = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var response = await guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, prfx), true, searchValue);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("togglecrowns", "togglecrown")]
    [Summary("Completely enables/disables crowns for your server.")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task ToggleCrownsAsync([CommandParameter(Remainder = true)] string unused = null)
    {
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        var response = await guildSettingBuilder.ToggleCrowns(new ContextModel(this.Context, prfx));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [Command("killcrown", "kcw", "kcrown", "killcw")]
    [Summary("Removes all crowns from a specific artist for your server.")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillCrownAsync([CommandParameter(Remainder = true)] string killCrownValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(killCrownValues) && killCrownValues.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killcrown");
            this._embed.WithDescription("Allows you to remove a crown and all crown history for a certain artist.");

            this._embed.AddField("Examples",
                $"`{prfx}killcrown deadmau5` \n" +
                $"`{prfx}killcrown the beatles`");

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Crown functionality has been disabled in this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, userService);
            return;
        }

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        if (string.IsNullOrWhiteSpace(killCrownValues))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please enter the artist you want to remove all crowns for." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        var artistCrowns = await crownService.GetCrownsForArtist(guild.GuildId, killCrownValues);

        if (!artistCrowns.Any())
        {
            this._embed.WithDescription($"No crowns found for the artist `{killCrownValues}`");
            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            return;
        }

        await crownService.RemoveCrowns(artistCrowns);

        this._embed.WithDescription($"All crowns for `{killCrownValues}` have been removed.");
        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("crownseeder", "crownseed", "seedcrowns")]
    [Summary("Automatically generates or updates all crowns for your server")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SeedCrownsAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        try
        {
            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await guildSettingBuilder.CrownSeeder(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("killallcrowns", "removeallcrowns")]
    [Summary("Removes all crowns from your server")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillAllCrownsAsync([CommandParameter(Remainder = true)] string confirmation = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killallcrowns");
            this._embed.WithDescription("Removes all crowns from your server.");

            this._embed.AddField("Examples",
                $"`{prfx}killallcrowns`\n" +
                $"`{prfx}killallcrowns confirm`");

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Crown functionality has been disabled in this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, userService);
            return;
        }

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var guildCrowns = await crownService.GetAllCrownsForGuild(guild.GuildId);
        if (guildCrowns == null || !guildCrowns.Any())
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "This server does not have any crowns." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.IndexRequired }, userService);
            return;
        }

        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
        {
            this._embed.WithDescription($"Are you sure you want to remove all {guildCrowns.Count} crowns from your server?\n\n" +
                                        $"Type `{prfx}killallcrowns confirm` to confirm.");
            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        await crownService.RemoveAllCrownsFromGuild(guild.GuildId);

        this._embed.WithDescription("Removed all crowns for your server.");
        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }

    [Command("killallseededcrowns", "removeallseededcrowns")]
    [Summary("Removes all crowns seeded by the `crownseeder` command. All other manually claimed crowns will remain in place.")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillAllSeededCrownsAsync([CommandParameter(Remainder = true)] string confirmation = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killallseededcrowns");
            this._embed.WithDescription("Removes all automatically seeded crowns from your server.");

            this._embed.AddField("Examples",
                $"`{prfx}killallseededcrowns`\n" +
                $"`{prfx}killallseededcrowns confirm`");

            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Help }, userService);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Crown functionality has been disabled in this server." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Disabled }, userService);
            return;
        }

        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = GuildSettingBuilder.UserNotAllowedResponseText() });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var guildCrowns = await crownService.GetAllCrownsForGuild(guild.GuildId);
        if (guildCrowns == null || !guildCrowns.Any())
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "This server does not have any crowns." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.IndexRequired }, userService);
            return;
        }

        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
        {
            this._embed.WithDescription(
                $"Are you sure you want to remove all {guildCrowns.Count(c => c.SeededCrown)} automatically seeded crowns from your server?\n\n" +
                $"Type `{prfx}killallseededcrowns confirm` to confirm.");
            await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        await crownService.RemoveAllSeededCrownsFromGuild(guild);

        this._embed.WithDescription("Removed all seeded crowns for your server.");
        await Context.Client.Rest.SendMessageAsync(Context.Message.ChannelId, new MessageProperties().AddEmbeds(this._embed));
        await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
    }
}
