using Discord;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Linq;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Builders;
using Fergun.Interactive;
using FMBot.Domain.Attributes;

namespace FMBot.Bot.SlashCommands;

public class ImportSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly ImportService _importService;
    private readonly IndexService _indexService;
    private readonly ImportBuilders _importBuilders;
    private readonly UserBuilder _userBuilder;
    private InteractiveService Interactivity { get; }

    public ImportSlashCommands(UserService userService,
        ImportService importService,
        IndexService indexService,
        InteractiveService interactivity,
        ImportBuilders importBuilders,
        UserBuilder userBuilder)
    {
        this._userService = userService;
        this._importService = importService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._importBuilders = importBuilders;
        this._userBuilder = userBuilder;
    }

    [ComponentInteraction(InteractionConstants.ImportSetting)]
    [UsernameSetRequired]
    public async Task SetImport(string[] inputs)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(inputs.FirstOrDefault(), out DataSource dataSource))
        {
            var newUserSettings = await this._userService.SetDataSource(contextUser, dataSource);

            var name = newUserSettings.DataSource.GetAttribute<OptionAttribute>().Name;

            var description = new StringBuilder();
            description.AppendLine($"Import mode set to **{name}**");
            description.AppendLine();

            var embed = new EmbedBuilder();
            embed.WithDescription(description +
                                  $"{DiscordConstants.Loading} Your stored top artist/albums/tracks are being recalculated, please wait for this to complete...");
            embed.WithColor(DiscordConstants.WarningColorOrange);

            ComponentBuilder components = null;
            if (dataSource == DataSource.LastFm)
            {
                components = new ComponentBuilder()
                    .WithButton("Delete imported Spotify history", InteractionConstants.ImportClearSpotify,
                        style: ButtonStyle.Danger, row: 0)
                    .WithButton("Delete imported Apple Music history", InteractionConstants.ImportClearAppleMusic,
                        style: ButtonStyle.Danger, row: 0);
            }

            await this.Context.Interaction.RespondAsync(null, [embed.Build()], ephemeral: true, components: components?.Build());
            this.Context.LogCommandUsed();

            await this._indexService.RecalculateTopLists(newUserSettings);

            embed.WithColor(DiscordConstants.SuccessColorGreen);
            embed.WithDescription(description +
                                  "✅ Your stored top artist/albums/tracks have successfully been recalculated.");
            await this.Context.Interaction.ModifyOriginalResponseAsync(msg =>
            {
                msg.Embed = embed.Build();
            });
        }
    }

    [ComponentInteraction(InteractionConstants.ImportClearSpotify)]
    [UsernameSetRequired]
    public async Task ClearImportSpotify()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        await this._importService.RemoveImportedSpotifyPlays(contextUser);

        var embed = new EmbedBuilder();
        embed.WithDescription($"All your imported Spotify history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportClearAppleMusic)]
    [UsernameSetRequired]
    public async Task ClearImportAppleMusic()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        await this._importService.RemoveImportedAppleMusicPlays(contextUser);

        var embed = new EmbedBuilder();
        embed.WithDescription($"All your imported Apple Music history has been removed from .fmbot.");
        embed.WithColor(DiscordConstants.SuccessColorGreen);

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [ComponentInteraction(InteractionConstants.ImportManage)]
    [UsernameSetRequired]
    public async Task ImportManage()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        await DeferAsync(true);

        try
        {
            var response =
                await this._userBuilder.ImportMode(new ContextModel(this.Context, contextUser), contextUser.UserId);

            await this.Context.SendFollowUpResponse(this.Interactivity, response, ephemeral: true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsSpotify)]
    [UsernameSetRequired]
    public async Task ImportInstructionsSpotify()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._importBuilders.GetSpotifyImportInstructions(new ContextModel(this.Context, contextUser),
                    true);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction(InteractionConstants.ImportInstructionsAppleMusic)]
    [UsernameSetRequired]
    public async Task ImportInstructionsAppleMusic()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response =
                await this._importBuilders.GetAppleMusicImportInstructions(new ContextModel(this.Context, contextUser),
                    true);

            await this.Context.UpdateInteractionEmbed(response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
