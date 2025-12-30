using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class ArtistInteractions : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly ArtistBuilders _artistBuilders;
    private readonly SettingService _settingService;
    private readonly ArtistsService _artistsService;
    private readonly InteractiveService _interactivity;

    public ArtistInteractions(
        UserService userService,
        ArtistBuilders artistBuilders,
        SettingService settingService,
        ArtistsService artistsService,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._artistBuilders = artistBuilders;
        this._settingService = settingService;
        this._artistsService = artistsService;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.Artist.Info)]
    [UsernameSetRequired]
    public async Task ArtistInfoAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));

        try
        {
            var response = await this._artistBuilders.ArtistInfoAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings, artist.Name, false);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.Overview)]
    [UsernameSetRequired]
    public async Task ArtistOverviewAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetUserWithDiscogs(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));

        try
        {
            var response = await this._artistBuilders.ArtistOverviewAsync(new ContextModel(this.Context, contextUser, discordContextUser), userSettings,
                artist.Name, false);

            await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.Tracks)]
    public async Task ArtistTracksAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetUserAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));
        var timeSettings = SettingService.GetTimePeriod(Enum.GetName(typeof(PlayTimePeriod), PlayTimePeriod.AllTime), TimePeriod.AllTime);

        var response = await this._artistBuilders.ArtistTracksAsync(new ContextModel(this.Context, contextUser, discordContextUser), timeSettings,
            userSettings, artist.Name, false);

        await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.Artist.Albums)]
    public async Task ArtistAlbumsAsync(string artistId, string discordUser, string requesterDiscordUser)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var discordUserId = ulong.Parse(discordUser);
        var requesterDiscordUserId = ulong.Parse(requesterDiscordUser);

        var contextUser = await this._userService.GetUserAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));

        var response = await this._artistBuilders.ArtistAlbumsAsync(new ContextModel(this.Context, contextUser, discordContextUser),
            userSettings, artist.Name, false);

        await this.Context.UpdateInteractionEmbed(response, this._interactivity, false);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [ComponentInteraction(InteractionConstants.WhoKnowsRolePicker)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsFilteringAsync(string artistId)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var roleIds = entityMenuInteraction.Data.SelectedValues.ToList();

        try
        {
            // TODO add redirects
            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), ResponseMode.Embed, artist.Name, true, roleIds);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.Artist.WhoKnows)]
    [UsernameSetRequired]
    [RequiresIndex]
    public async Task WhoKnowsAsync(string artistId)
    {
        await RespondAsync(InteractionCallback.DeferredModifyMessage);
        await this.Context.DisableInteractionButtons();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));
        var mode = contextUser.Mode ?? ResponseMode.Embed;

        try
        {
            var response = await this._artistBuilders.WhoKnowsArtistAsync(new ContextModel(this.Context, contextUser), mode, artist.Name, showCrownButton: true);

            await this.Context.UpdateInteractionEmbed(response, defer: false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
