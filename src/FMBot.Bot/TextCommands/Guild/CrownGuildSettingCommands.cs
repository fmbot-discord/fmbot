using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
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

namespace FMBot.Bot.TextCommands.Guild;

[Name("Crown settings")]
[ServerStaffOnly]
public class CrownGuildSettingCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly CrownService _crownService;
    private readonly GuildService _guildService;
    private readonly SettingService _settingService;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IPrefixService _prefixService;
    private InteractiveService Interactivity { get; }


    public CrownGuildSettingCommands(IPrefixService prefixService,
        GuildService guildService,
        AdminService adminService,
        SettingService settingService,
        CrownService crownService,
        IOptions<BotSettings> botSettings,
        InteractiveService interactivity,
        GuildSettingBuilder guildSettingBuilder) : base(botSettings)
    {
        this._prefixService = prefixService;
        this._guildService = guildService;
        this._settingService = settingService;
        this._crownService = crownService;
        this.Interactivity = interactivity;
        this._guildSettingBuilder = guildSettingBuilder;
        this._adminService = adminService;
    }

    [Command("crownthreshold", RunMode = RunMode.Async)]
    [Summary("Sets amount of plays before someone can earn a crown in your server")]
    [Alias("setcrownthreshold", "setcwthreshold", "cwthreshold", "crowntreshold")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SetCrownPlaycountThresholdAsync([Remainder] string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.SetCrownMinPlaycount(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crownactivitythreshold", RunMode = RunMode.Async)]
    [Summary("Sets amount of days to filter out users from earning crowns for inactivity. " +
             "Inactivity is counted by the last date that someone has used .fmbot")]
    [Alias("setcrownactivitythreshold", "setcwactivitythreshold", "cwactivitythreshold", "crownactivitytreshold")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SetCrownActivityThresholdAsync([Remainder] string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.SetCrownActivityThreshold(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crownblock", RunMode = RunMode.Async)]
    [Summary("Block a user from gaining any crowns in your server")]
    [Options(Constants.UserMentionExample)]
    [Alias("crownblockuser", "crownban", "cwblock", "cwban", "crownbanuser", "crownbanmember")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task GuildBlockUserFromCrownsAsync([Remainder] string user = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        if (user == null)
        {
            await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to block from gaining crowns on your server.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var userToBlock = await this._settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild.Id);

        if (!guildUsers.TryGetValue(userToBlock.UserId, out var guildUser))
        {
            var similarUsers = await this._adminService.GetUsersWithLfmUsernameAsync(userToBlock.UserNameLastFM);

            var userInThisServer = similarUsers.FirstOrDefault(f =>
                f.UserNameLastFM.ToLower() == userToBlock.UserNameLastFM.ToLower() && guildUsers.ContainsKey(f.UserId));

            if (userInThisServer == null)
            {
                await ReplyAsync("User not found. Are you sure they are in this server?\n" +
                                 $"To refresh the cached memberlist on your server, use `{prfx}refreshmembers`.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            userToBlock = userInThisServer;
        }

        if (guildUser == null)
        {
            await ReplyAsync("User not found. Are you sure they are in this server?\n" +
                             $"To refresh the cached memberlist on your server, use `{prfx}refreshmembers`.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        if (guildUser.BlockedFromCrowns)
        {
            await ReplyAsync("The user you're trying to block from gaining crowns has already been blocked on this server.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var userBlocked = await this._guildService.CrownBlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

        if (userBlocked)
        {
            this._embed.WithTitle("Added crownblocked user");
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");

            this._embed.WithFooter($"See all crownblocked users with {prfx}crownblockedusers");

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Something went wrong while attempting to crownblock user, please contact .fmbot staff.");
            this.Context.LogCommandUsed(CommandResponse.Error);
        }
    }

    [Command("removeusercrowns", RunMode = RunMode.Async)]
    [Summary("Removes crowns from a user")]
    [Options(Constants.UserMentionExample)]
    [Alias("deleteusercrowns", "deleteusercrown", "removeusercrown", "removeusercws", "deleteusercws", "usercrownsdelete", "usercrownsremove", "killusercrowns")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task RemoveUserCrownsAsync([Remainder] string user = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        if (user == null)
        {
            await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to remove their crowns from.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        var userToBlock = await this._settingService.GetDifferentUser(user);

        if (userToBlock == null)
        {
            await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        try
        {
            this._embed.WithTitle("Are you sure you want to delete all crowns for this user?");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                        $"Last.fm username: `{userToBlock.UserNameLastFM}`");
            this._embed.WithFooter($"Expires in 30 seconds..");

            var builder = new ComponentBuilder()
                .WithButton("Confirm", "id");

            var msg = await ReplyAsync("", false, this._embed.Build(), components: builder.Build());

            var result = await this.Interactivity.NextInteractionAsync(x =>
                    x is SocketMessageComponent c && c.Message.Id == msg.Id && x.User.Id == this.Context.User.Id,
                    timeout: TimeSpan.FromSeconds(30));

            if (result.IsSuccess)
            {
                await result.Value.DeferAsync();
                await this._crownService.RemoveAllCrownsFromDiscordUser(userToBlock.DiscordUserId, this.Context.Guild.Id);

                this._embed.WithTitle("Crowns have been removed for:");
                this._embed.Footer = null;
                await msg.ModifyAsync(x =>
                {
                    x.Embed = this._embed.Build();
                    x.Components = new ComponentBuilder().Build(); // No components
                    x.AllowedMentions = AllowedMentions.None;
                });
            }
            else
            {
                this._embed.WithTitle("Crown removal timed out");
                await msg.ModifyAsync(x =>
                {
                    x.Embed = this._embed.Build();
                    x.Components = new ComponentBuilder().Build(); // No components
                    x.AllowedMentions = AllowedMentions.None;
                });
            }

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await ReplyAsync("Something went wrong while attempting to remove crowns for user, please contact .fmbot staff.");
        }
    }

    [Command("crownblockedusers", RunMode = RunMode.Async)]
    [Summary("View all users that are blocked from earning crowns in your server")]
    [Alias("crownblocked", "crownbanned", "crownbannedusers", "crownbannedmembers")]
    [GuildOnly]
    [RequiresIndex]
    [SupportsPagination]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task CrownBlockedUsersAsync([Remainder] string searchValue = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, prfx), true, searchValue);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("togglecrowns", RunMode = RunMode.Async)]
    [Summary("Completely enables/disables crowns for your server.")]
    [Alias("togglecrown")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task ToggleCrownsAsync([Remainder] string confirmation = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        _ = this.Context.Channel.TriggerTypingAsync();
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (guild.CrownsDisabled != true && (confirmation == null || confirmation.ToLower() != "confirm"))
        {
            await ReplyAsync($"Disabling crowns will remove all existing crowns and crown history for this server.\n" +
                             $"Type `{prfx}togglecrowns confirm` to confirm.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var crownsDisabled = await this._guildService.ToggleCrownsAsync(this.Context.Guild);

        if (crownsDisabled == true)
        {
            await this._crownService.RemoveAllCrownsFromGuild(guild.GuildId);
            await ReplyAsync("All crowns have been removed and crowns have been disabled for this server.");
        }
        else
        {
            await ReplyAsync($"Crowns have been enabled for this server.");
        }

        this.Context.LogCommandUsed();
    }

    [Command("killcrown", RunMode = RunMode.Async)]
    [Summary("Removes all crowns from a specific artist for your server.")]
    [Alias("kcw", "kcrown", "killcw", "kill crown", "crown kill")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillCrownAsync([Remainder] string killCrownValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(killCrownValues) && killCrownValues.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killcrown");
            this._embed.WithDescription("Allows you to remove a crown and all crown history for a certain artist.");

            this._embed.AddField("Examples",
                $"`{prfx}killcrown deadmau5` \n" +
                $"`{prfx}killcrown the beatles`");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await ReplyAsync("Crown functionality has been disabled in this server.");
            this.Context.LogCommandUsed(CommandResponse.Disabled);
            return;
        }

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        if (string.IsNullOrWhiteSpace(killCrownValues))
        {
            await ReplyAsync("Please enter the artist you want to remove all crowns for.");
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var artistCrowns = await this._crownService.GetCrownsForArtist(guild.GuildId, killCrownValues);

        if (!artistCrowns.Any())
        {
            this._embed.WithDescription($"No crowns found for the artist `{killCrownValues}`");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            return;
        }

        await this._crownService.RemoveCrowns(artistCrowns);

        this._embed.WithDescription($"All crowns for `{killCrownValues}` have been removed.");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("crownseeder", RunMode = RunMode.Async)]
    [Summary("Automatically generates or updates all crowns for your server")]
    [Alias("crownseed", "seedcrowns")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task SeedCrownsAsync([Remainder] string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var response = await this._guildSettingBuilder.CrownSeeder(new ContextModel(this.Context, prfx));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("killallcrowns", RunMode = RunMode.Async)]
    [Summary("Removes all crowns from your server")]
    [Alias("removeallcrowns")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillAllCrownsAsync([Remainder] string confirmation = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killallcrowns");
            this._embed.WithDescription("Removes all crowns from your server.");

            this._embed.AddField("Examples",
                $"`{prfx}killallcrowns`\n" +
                $"`{prfx}killallcrowns confirm`");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await ReplyAsync("Crown functionality has been disabled in this server.");
            this.Context.LogCommandUsed(CommandResponse.Disabled);
            return;
        }

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);
        if (guildCrowns == null || !guildCrowns.Any())
        {
            await ReplyAsync("This server does not have any crowns.");
            this.Context.LogCommandUsed(CommandResponse.IndexRequired);
            return;
        }

        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
        {
            this._embed.WithDescription($"Are you sure you want to remove all {guildCrowns.Count} crowns from your server?\n\n" +
                                        $"Type `{prfx}killallcrowns confirm` to confirm.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await this._crownService.RemoveAllCrownsFromGuild(guild.GuildId);

        this._embed.WithDescription("Removed all crowns for your server.");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }

    [Command("killallseededcrowns", RunMode = RunMode.Async)]
    [Summary("Removes all crowns seeded by the `crownseeder` command. All other manually claimed crowns will remain in place.")]
    [Alias("removeallseededcrowns")]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns, CommandCategory.ServerSettings)]
    public async Task KillAllSeededCrownsAsync([Remainder] string confirmation = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        if (!string.IsNullOrWhiteSpace(confirmation) && confirmation.ToLower() == "help")
        {
            this._embed.WithTitle($"{prfx}killallseededcrowns");
            this._embed.WithDescription("Removes all automatically seeded crowns from your server.");

            this._embed.AddField("Examples",
                $"`{prfx}killallseededcrowns`\n" +
                $"`{prfx}killallseededcrowns confirm`");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Help);
            return;
        }

        if (guild.CrownsDisabled == true)
        {
            await ReplyAsync("Crown functionality has been disabled in this server.");
            this.Context.LogCommandUsed(CommandResponse.Disabled);
            return;
        }

        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context, prfx)))
        {
            await ReplyAsync(GuildSettingBuilder.UserNotAllowedResponseText());
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

        var guildCrowns = await this._crownService.GetAllCrownsForGuild(guild.GuildId);
        if (guildCrowns == null || !guildCrowns.Any())
        {
            await ReplyAsync("This server does not have any crowns.");
            this.Context.LogCommandUsed(CommandResponse.IndexRequired);
            return;
        }

        if (string.IsNullOrWhiteSpace(confirmation) || confirmation.ToLower() != "confirm")
        {
            this._embed.WithDescription($"Are you sure you want to remove all {guildCrowns.Count(c => c.SeededCrown)} automatically seeded crowns from your server?\n\n" +
                                        $"Type `{prfx}killallseededcrowns confirm` to confirm.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        await this._crownService.RemoveAllSeededCrownsFromGuild(guild);

        this._embed.WithDescription("Removed all seeded crowns for your server.");
        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
        this.Context.LogCommandUsed();
    }
}
