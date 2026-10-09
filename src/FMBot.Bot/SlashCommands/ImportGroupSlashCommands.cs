using System.Threading.Tasks;
using System;
using System.Linq;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

[SlashCommand("import", "Manage your data imports",
    Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
    IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
[UsernameSetRequired]
public class ImportGroupSlashCommands(
    UserService userService,
    InteractiveService interactivity,
    ImportBuilders importBuilders,
    SupporterService supporterService,
    UserBuilder userBuilder)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SubSlashCommand("spotify", "⭐ Import your Spotify history into .fmbot")]
    [UsernameSetRequired]
    public async Task SpotifyAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            userService.RemoveUserFromCache(contextUser);
            contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendFollowUpResponse(this.Interactivity, supporterRequired, userService);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        var instructionResponse =
            await importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser));
        await this.Context.SendFollowUpResponse(this.Interactivity, instructionResponse, userService);
        await this.Context.LogCommandUsedAsync(instructionResponse, userService);
    }

    [SubSlashCommand("applemusic", "⭐ Import your Apple Music history into .fmbot")]
    [UsernameSetRequired]
    public async Task AppleMusicAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        if (this.Context.Interaction.Entitlements.Any() && !SupporterService.IsSupporter(contextUser.UserType))
        {
            await supporterService.UpdateSingleDiscordSupporter(this.Context.User.Id);
            userService.RemoveUserFromCache(contextUser);
            contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        }

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendFollowUpResponse(this.Interactivity, supporterRequired, userService);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        var instructionResponse =
            await importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser));
        await this.Context.SendFollowUpResponse(this.Interactivity, instructionResponse, userService);
        await this.Context.LogCommandUsedAsync(instructionResponse, userService);
    }

    [SubSlashCommand("manage", "⭐ Manage your imports and configure how they are used")]
    [UsernameSetRequired]
    public async Task ManageImportAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired, userService);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        try
        {
            var response =
                await userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SubSlashCommand("modify", "⭐ Edit and delete artists, albums and tracks in your .fmbot imports")]
    [UsernameSetRequired]
    public async Task ModifyImportAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var supporterRequired = ImportBuilders.ImportSupporterRequired(new ContextModel(this.Context, contextUser));

        if (supporterRequired != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequired, userService);
            await this.Context.LogCommandUsedAsync(supporterRequired, userService);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        if (this.Context.Guild != null)
        {
            var serverContainer = new ComponentContainerProperties();
            serverContainer.WithAccentColor(DiscordConstants.InformationColorBlue);
            serverContainer.WithTextDisplay("Check your DMs to continue with modifying your .fmbot imports.");

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithComponents([serverContainer])
                .WithFlags(MessageFlags.Ephemeral | MessageFlags.IsComponentsV2));
        }
        else if (this.Context.Channel != null)
        {
            _ = this.Context.Channel?.TriggerTypingAsync()!;
        }

        try
        {
            var response =
                await importBuilders.ImportModify(new ContextModel(this.Context, contextUser),
                    contextUser.UserId);
            var dmChannel = await userService.GetDmChannel(this.Context.User);
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Components = response.GetComponentsV2(),
                Flags = MessageFlags.IsComponentsV2
            });
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
