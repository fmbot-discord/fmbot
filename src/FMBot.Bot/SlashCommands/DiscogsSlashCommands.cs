using System;
using System.Collections.Generic;
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
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class DiscogsSlashCommands(
    UserService userService,
    DiscogsBuilder discogsBuilder,
    InteractiveService interactivity,
    SettingService settingService)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("discogs", "Connects your Discogs account by sending a link to your DMs",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task DiscogsAsync()
    {
        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);

        try
        {
            if (contextUser.UserDiscogs == null)
            {
                if (this.Context.Guild != null)
                {
                    var serverEmbed = new EmbedProperties()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                    await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithEmbeds([serverEmbed])
                            .WithFlags(MessageFlags.Ephemeral)));
                }

                var response = discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, contextUser));
                var dmChannel = await this.Context.User.GetDMChannelAsync();
                await dmChannel.SendMessageAsync(new MessageProperties
                {
                    Embeds = [response.Embed],
                    Components = [response.Components]
                });
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            else
            {
                if (this.Context.Guild != null)
                {
                    var serverEmbed = new EmbedProperties()
                        .WithColor(DiscordConstants.InformationColorBlue);

                    serverEmbed.WithDescription(
                        "Check your DMs for a message to manage your connected Discogs account!");
                    await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithEmbeds([serverEmbed])
                            .WithFlags(MessageFlags.Ephemeral)));
                }

                var response = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, contextUser));
                var manageDmChannel = await this.Context.User.GetDMChannelAsync();
                await manageDmChannel.SendMessageAsync(new MessageProperties
                {
                    Embeds = [response.Embed],
                    Components = [response.Components]
                });
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("collection", "You or someone else's Discogs collection",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task AlbumAsync(
        [SlashCommandParameter(Name = "search", Description = "Search query to filter on")]
        string search = null,
        [SlashCommandParameter(Name = "user", Description = "The user to show (defaults to self)")]
        string user = null,
        [SlashCommandParameter(Name = "format", Description = "Media format to include")]
        DiscogsFormat? format = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);
        var collectionSettings = new DiscogsCollectionSettings
        {
            Formats = format != null ? new List<DiscogsFormat> { (DiscogsFormat)format } : new()
        };

        try
        {
            var response = await discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, contextUser),
                userSettings, collectionSettings, search);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
