using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Fergun.Interactive;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("graph", "Visualize your music data with interactive graphs",
    Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
    IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
public class GraphSlashCommands(
    UserService userService,
    GraphBuilders graphBuilders,
    SettingService settingService,
    InteractiveService interactivity)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("artists", "Graph showing scrobble growth over time per artist")]
    [UsernameSetRequired]
    public async Task ArtistGrowthAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "private", Description = "Only show response to you")]
        bool privateResponse = false)
    {
        await Context.Interaction.SendResponseAsync(
            InteractionCallback.DeferredMessage(privateResponse ? MessageFlags.Ephemeral : default));

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var timeSettings = SettingService.GetTimePeriod(timePeriod,
            registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
            defaultTimePeriod: TimePeriod.AllTime);

        if (!SupporterService.IsSupporter(userSettings.UserType) && timeSettings.PlayDays.GetValueOrDefault() > 180)
        {
            var response = new ResponseModel
            {
                ResponseType = ResponseType.Embed
            };
            response.Embed.WithDescription(
                "Graphs with time periods longer than 6 months require your lifetime listening history. " +
                "Your lifetime history and more are only available for supporters.");
            response.Components = new ActionRowProperties()
                .WithButton(Constants.GetSupporterButton,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "graph"));
            response.Embed.WithColor(DiscordConstants.InformationColorBlue);
            response.CommandResponse = CommandResponse.SupporterRequired;

            await this.Context.SendFollowUpResponse(interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
            return;
        }

        try
        {
            var response = await graphBuilders.ArtistGrowthAsync(
                new ContextModel(this.Context, contextUser), userSettings, timeSettings);

            await this.Context.SendFollowUpResponse(interactivity, response, userService, privateResponse);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
