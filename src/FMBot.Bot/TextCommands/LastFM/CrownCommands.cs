using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands.LastFM;

[ModuleName("Crowns")]
public class CrownCommands(
    GuildService guildService,
    IPrefixService prefixService,
    UserService userService,
    SettingService settingService,
    InteractiveService interactivity,
    IOptions<BotSettings> botSettings,
    CrownBuilders crownBuilders,
    GuildBuilders guildBuilders)
    : BaseCommandModule(botSettings)
{

    private InteractiveService Interactivity { get; } = interactivity;

    [Command("crowns", "cws", "topcrowns", "topcws", "tcws")]
    [Summary("Shows you your crowns for this server.")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task UserCrownsAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(extraOptions, contextUser, this.Context);

        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        var crownViewType = SettingService.SetCrownViewSettings(userSettings.NewSearchValue);

        try
        {
            var response = await crownBuilders.CrownOverviewAsync(new ContextModel(this.Context, prfx, contextUser), guild, userSettings, crownViewType);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crown", "cw")]
    [Summary("Shows crown history for the artist you're currently listening to or searching for")]
    [UsernameSetRequired]
    [GuildOnly]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownAsync([CommandParameter(Remainder = true)] string artistValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await crownBuilders.CrownAsync(new ContextModel(this.Context, prfx, contextUser), guild, artistValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("crownleaderboard", "cwlb", "crownlb", "cwleaderboard")]
    [Summary("Shows users with the most crowns in your server")]
    [UsernameSetRequired]
    [GuildOnly]
    [SupportsPagination]
    [RequiresIndex]
    [CommandCategories(CommandCategory.Crowns)]
    public async Task CrownLeaderboardAsync()
    {
        try
        {
            _ = this.Context.Channel?.TriggerTypingStateAsync()!;

            var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await guildService.GetGuildForWhoKnows(this.Context.Guild?.Id);
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = await guildBuilders.MemberOverviewAsync(new ContextModel(this.Context, prfx, contextUser),
                guild, GuildViewType.Crowns);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
