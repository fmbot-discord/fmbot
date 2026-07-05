using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class SettingsInteractions(
    UserService userService,
    GuildSettingBuilder guildSettingBuilder,
    PremiumSettingBuilder premiumSettingBuilder,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Settings.Tab)]
    [UsernameSetRequired]
    public async Task SettingsTabAsync(string tabStr, string discordUserIdStr)
    {
        try
        {
            if (!ulong.TryParse(discordUserIdStr, out var ownerDiscordId) ||
                !Enum.TryParse<SettingsTab>(tabStr, out var tab) ||
                !Enum.IsDefined(tab))
            {
                return;
            }

            if (this.Context.User.Id != ownerDiscordId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the user who opened these settings can switch tabs.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var context = new ContextModel(this.Context, contextUser);

            var availableTabs = await guildSettingBuilder.GetAvailableSettingsTabs(context);
            if (!availableTabs.Contains(tab))
            {
                await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
                return;
            }

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var response = tab switch
            {
                SettingsTab.Server => await guildSettingBuilder.GetGuildSettings(context,
                    this.Context.Interaction.AppPermissions, availableTabs),
                SettingsTab.Premium => await premiumSettingBuilder.GetPremiumServerSettings(context, availableTabs),
                _ => UserBuilder.GetUserSettings(context, availableTabs)
            };

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
