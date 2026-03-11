using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using NetCord.Services.ApplicationCommands;
using NetCord.Rest;

namespace FMBot.Bot.SlashCommands;

public class CrownSlashCommands(
    CrownBuilders crownBuilders,
    InteractiveService interactivity,
    UserService userService,
    GuildService guildService,
    SettingService settingService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;


    [SlashCommand("crown", "History for a specific crown")]
    [UsernameSetRequired]
    public async Task CrownAsync(
        [SlashCommandParameter(Name = "artist",
            Description = "The artist you want to search for (defaults to currently playing)",
            AutocompleteProviderType = typeof(ArtistAutoComplete))]
        string name = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await crownBuilders.CrownAsync(new ContextModel(this.Context, contextUser), guild, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("crowns", "View a list of crowns for you or someone else")]
    [UsernameSetRequired]
    public async Task CrownOverViewAsync(
        [SlashCommandParameter(Name = "view", Description = "View of crowns you want to see")]
        CrownViewType viewType = CrownViewType.Playcount,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await crownBuilders.CrownOverviewAsync(new ContextModel(this.Context, contextUser), guild,
                userSettings, viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
