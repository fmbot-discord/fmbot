using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class CrownSlashCommands : InteractionModuleBase
{
    private readonly CrownBuilders _crownBuilders;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly SettingService _settingService;
    private readonly ArtistsService _artistsService;

    private InteractiveService Interactivity { get; }


    public CrownSlashCommands(CrownBuilders crownBuilders, InteractiveService interactivity, UserService userService, GuildService guildService, SettingService settingService, ArtistsService artistsService)
    {
        this._crownBuilders = crownBuilders;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._guildService = guildService;
        this._settingService = settingService;
        this._artistsService = artistsService;
    }

    [SlashCommand("crown", "Shows history for a specific crown")]
    [UsernameSetRequired]
    public async Task CrownAsync(
        [Summary("Artist", "The artist your want to search for (defaults to currently playing)")]
        [Autocomplete(typeof(ArtistAutoComplete))] string name = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownAsync(new ContextModel(this.Context, contextUser), guild, name);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Artist.Crown}-*-*")]
    [UsernameSetRequired]
    public async Task CrownButtonAsync(string artistId, string stolen)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var artist = await this._artistsService.GetArtistForId(int.Parse(artistId));
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownAsync(new ContextModel(this.Context, contextUser), guild, artist.Name);

            if (stolen.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                _ = this.Context.DisableInteractionButtons();
                response.Components = null;
                await this.Context.SendResponse(this.Interactivity, response);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
            else
            {
                await this.Context.UpdateInteractionEmbed(response);
                this.Context.LogCommandUsed(response.CommandResponse);
            }

        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("crowns", "View a list of crowns for you or someone else")]
    [UsernameSetRequired]
    public async Task CrownOverViewAsync(
        [Summary("View", "View of crowns you want to see")] CrownViewType viewType = CrownViewType.Playcount,
        [Summary("User", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownOverviewAsync(new ContextModel(this.Context, contextUser), guild, userSettings, viewType);

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.User.CrownSelectMenu)]
    [UsernameSetRequired]
    public async Task CrownSelectMenu(string[] inputs)
    {
        _ = DeferAsync();

        var options = inputs.First().Split("-");

        var discordUserId = ulong.Parse(options[0]);
        var requesterDiscordUserId = ulong.Parse(options[1]);

        if (!Enum.TryParse(options[2], out CrownViewType viewType))
        {
            return;
        }

        var contextUser = await this._userService.GetUserWithFriendsAsync(requesterDiscordUserId);
        var discordContextUser = await this.Context.Client.GetUserAsync(requesterDiscordUserId);
        var userSettings = await this._settingService.GetOriginalContextUser(discordUserId, requesterDiscordUserId, this.Context.Guild, this.Context.User);

        var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

        try
        {
            var response = await this._crownBuilders.CrownOverviewAsync(new ContextModel(this.Context, contextUser, discordContextUser), guild, userSettings, viewType);

            await this.Context.UpdateInteractionEmbed(response, this.Interactivity, false);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
