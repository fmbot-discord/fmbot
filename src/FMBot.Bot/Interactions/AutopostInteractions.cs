using System;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class AutopostInteractions(
    AutopostBuilders autopostBuilders,
    AutopostService autopostService,
    GuildService guildService,
    GuildSettingBuilder guildSettingBuilder,
    UserService userService,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Autopost.Overview)]
    [ServerStaffOnly]
    public async Task OverviewAsync()
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var response = await autopostBuilders.Overview(new ContextModel(this.Context));
            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.Add)]
    [ServerStaffOnly]
    public async Task AddAsync()
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var response = AutopostBuilders.AddChannelPicker(new ContextModel(this.Context));
            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.AddChannel)]
    [ServerStaffOnly]
    public async Task AddChannelAsync()
    {
        if (!await CheckAccess())
        {
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedChannelIds = entityMenuInteraction.Data.SelectedValues;

        try
        {
            if (selectedChannelIds.Count == 0)
            {
                var overview = await autopostBuilders.Overview(new ContextModel(this.Context));
                await this.Context.UpdateInteractionEmbed(overview);
                return;
            }

            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
            var autopost = await autopostService.CreateAutopostAsync(guild.GuildId, selectedChannelIds[0],
                this.Context.User.Id);

            if (autopost == null)
            {
                await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                    new InteractionMessageProperties()
                        .WithContent(
                            "⚠️ Could not add an autopost for that channel. It may already use every content type and schedule combination, or your server may be at the autopost limit.")
                        .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var response = await autopostBuilders.EditView(new ContextModel(this.Context), autopost.Id);
            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.Edit)]
    [ServerStaffOnly]
    public async Task EditAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);
            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetType)]
    [ServerStaffOnly]
    public async Task SetTypeAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null && selectedValues.Count > 0 &&
                Enum.TryParse(selectedValues[0], out AutopostType contentType))
            {
                TimePeriod? timePeriod = contentType == AutopostType.ServerRecap ? null : autopost.TimePeriod;
                var artistFilter = contentType is AutopostType.ServerAlbums or AutopostType.ServerTracks
                    ? autopost.ArtistFilter
                    : null;
                if (await autopostService.ChannelHasDuplicateAsync(autopost, contentType, autopost.Schedule,
                        timePeriod, artistFilter, autopost.RoleIds))
                {
                    await UpdateEditView(autopost);
                    await SendDuplicateWarning();
                    return;
                }

                await autopostService.UpdateAutopostAsync(autopost.Id, a =>
                {
                    a.ContentType = contentType;

                    if (contentType == AutopostType.ServerRecap)
                    {
                        a.TimePeriod = null;
                    }

                    if (contentType is not (AutopostType.ServerAlbums or AutopostType.ServerTracks))
                    {
                        a.ArtistFilter = null;
                    }
                });
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetSchedule)]
    [ServerStaffOnly]
    public async Task SetScheduleAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null && selectedValues.Count > 0 &&
                Enum.TryParse(selectedValues[0], out AutopostSchedule schedule) &&
                Enum.IsDefined(schedule))
            {
                if (await autopostService.ChannelHasDuplicateAsync(autopost, autopost.ContentType, schedule,
                        autopost.TimePeriod, autopost.ArtistFilter, autopost.RoleIds))
                {
                    await UpdateEditView(autopost);
                    await SendDuplicateWarning();
                    return;
                }

                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.Schedule = schedule);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetTimePeriod)]
    [ServerStaffOnly]
    public async Task SetTimePeriodAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null && selectedValues.Count > 0 && autopost.ContentType != AutopostType.ServerRecap)
            {
                TimePeriod? timePeriod = selectedValues[0] == "alltime" ? TimePeriod.AllTime : null;
                if (await autopostService.ChannelHasDuplicateAsync(autopost, autopost.ContentType, autopost.Schedule,
                        timePeriod, autopost.ArtistFilter, autopost.RoleIds))
                {
                    await UpdateEditView(autopost);
                    await SendDuplicateWarning();
                    return;
                }

                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.TimePeriod = timePeriod);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetRole)]
    [ServerStaffOnly]
    public async Task SetRoleAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var entityMenuInteraction = (EntityMenuInteraction)this.Context.Interaction;
        var selectedRoleIds = entityMenuInteraction.Data.SelectedValues;

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null)
            {
                var roleIds = selectedRoleIds.Count > 0 ? selectedRoleIds.ToArray() : null;
                if (await autopostService.ChannelHasDuplicateAsync(autopost, autopost.ContentType, autopost.Schedule,
                        autopost.TimePeriod, autopost.ArtistFilter, roleIds))
                {
                    await UpdateEditView(autopost);
                    await SendDuplicateWarning();
                    return;
                }

                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.RoleIds = roleIds);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetSize)]
    [ServerStaffOnly]
    public async Task SetSizeAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null && selectedValues.Count > 0 &&
                Enum.TryParse(selectedValues[0], out AutopostSize size))
            {
                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.ContentSize = size);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetArtist)]
    [ServerStaffOnly]
    public async Task SetArtistButtonAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        var autopost = await GetOwnedAutopost(autopostId);
        if (autopost == null)
        {
            return;
        }

        await RespondAsync(InteractionCallback.Modal(ModalFactory.CreateAutopostArtistFilterModal(
            $"{InteractionConstants.Autopost.SetArtistModal}:{autopost.Id}", autopost.ArtistFilter)));
    }

    [ComponentInteraction(InteractionConstants.Autopost.SetArtistModal)]
    [ServerStaffOnly]
    public async Task SetArtistModalAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null)
            {
                var artistName = this.Context.GetModalValue("artist_name")?.Trim();
                var artistFilter = string.IsNullOrWhiteSpace(artistName) ? null : artistName;

                if (await autopostService.ChannelHasDuplicateAsync(autopost, autopost.ContentType, autopost.Schedule,
                        autopost.TimePeriod, artistFilter, autopost.RoleIds))
                {
                    await UpdateEditView(autopost);
                    await SendDuplicateWarning();
                    return;
                }

                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.ArtistFilter = artistFilter);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.Toggle)]
    [ServerStaffOnly]
    public async Task ToggleAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null)
            {
                await autopostService.UpdateAutopostAsync(autopost.Id, a => a.Enabled = !a.Enabled);
            }

            await UpdateEditView(autopost);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.Remove)]
    [ServerStaffOnly]
    public async Task RemoveAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            var autopost = await GetOwnedAutopost(autopostId);

            if (autopost != null)
            {
                await autopostService.RemoveAutopostAsync(autopost.Id);
            }

            var response = await autopostBuilders.Overview(new ContextModel(this.Context));
            await this.Context.UpdateInteractionEmbed(response);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.PostNow)]
    [ServerStaffOnly]
    public async Task PostNowAsync(string autopostId)
    {
        if (!await CheckAccess())
        {
            return;
        }

        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var autopost = await GetOwnedAutopost(autopostId);
            var result = autopost != null
                ? await autopostService.PostAutopostNow(autopost.Id)
                : AutopostPostResult.Failed;

            var content = result switch
            {
                AutopostPostResult.Posted => $"✅ Autopost posted in <#{autopost.ChannelId}>.",
                AutopostPostResult.NoData => "⚠️ No listening data found for the previous period, so nothing was posted.",
                _ => "❌ Could not post the autopost. Make sure the bot can send messages in the configured channel."
            };

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Content = content;
            });

            var commandResponse = result switch
            {
                AutopostPostResult.Posted => CommandResponse.Ok,
                AutopostPostResult.NoData => CommandResponse.NoScrobbles,
                _ => CommandResponse.Error
            };

            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = commandResponse }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: false);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.DeepDive)]
    public async Task DeepDiveAsync(string runId)
    {
        try
        {
            ResponseModel response = null;
            if (long.TryParse(runId, out var parsedRunId))
            {
                var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
                var run = await autopostService.GetRun(parsedRunId);

                if (guild != null && run != null && run.GuildId == guild.GuildId)
                {
                    response = await autopostBuilders.DeepDive(parsedRunId);
                }
            }

            if (response == null)
            {
                response = new ResponseModel
                {
                    ResponseType = ResponseType.Embed,
                    CommandResponse = CommandResponse.NotFound
                };
                response.Embed.WithDescription("The data behind this post is no longer available.");
                response.Embed.WithColor(DiscordConstants.WarningColorOrange);
            }

            await this.Context.SendResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Autopost.DeepDiveSection)]
    public async Task DeepDiveSectionAsync(string runId)
    {
        var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
        var selectedValues = stringMenuInteraction.Data.SelectedValues;

        try
        {
            if (!long.TryParse(runId, out var parsedRunId) ||
                selectedValues.Count == 0 ||
                !int.TryParse(selectedValues[0], out var sectionIndex))
            {
                return;
            }

            var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
            var run = await autopostService.GetRun(parsedRunId);

            if (guild == null || run == null || run.GuildId != guild.GuildId)
            {
                return;
            }

            var response = await autopostBuilders.DeepDive(parsedRunId, sectionIndex);
            await this.Context.UpdateInteractionEmbed(response, interactivity);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private async Task<bool> CheckAccess()
    {
        if (!await guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoPermission }, userService);
            return false;
        }

        if (!PublicProperties.PremiumServers.ContainsKey(this.Context.Guild.Id))
        {
            var premiumRequiredResponse = PremiumSettingBuilder.PremiumServerRequired("autoposts",
                AutopostBuilders.PremiumFeatureDescription);
            await this.Context.SendResponse(interactivity, premiumRequiredResponse, userService, true);
            await this.Context.LogCommandUsedAsync(premiumRequiredResponse, userService);
            return false;
        }

        return true;
    }

    private async Task<GuildAutopost> GetOwnedAutopost(string autopostId)
    {
        if (!int.TryParse(autopostId, out var parsedId))
        {
            return null;
        }

        var autopost = await autopostService.GetAutopost(parsedId);
        if (autopost == null)
        {
            return null;
        }

        var guild = await guildService.GetGuildAsync(this.Context.Guild.Id);
        return guild != null && autopost.GuildId == guild.GuildId ? autopost : null;
    }

    private async Task SendDuplicateWarning()
    {
        await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .WithContent(
                "⚠️ This channel already has an identical autopost: same content type, time period, roles and artist filter.")
            .WithFlags(MessageFlags.Ephemeral));
    }

    private async Task UpdateEditView(GuildAutopost autopost)
    {
        var response = autopost != null
            ? await autopostBuilders.EditView(new ContextModel(this.Context), autopost.Id)
            : await autopostBuilders.Overview(new ContextModel(this.Context));

        await this.Context.UpdateInteractionEmbed(response);
    }
}
