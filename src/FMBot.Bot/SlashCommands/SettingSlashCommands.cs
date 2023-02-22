using System.Linq;
using System.Threading.Tasks;
using System;
using Discord.Interactions;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;
using FMBot.Bot.Models;
using Fergun.Interactive;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using static FMBot.Bot.Builders.GuildSettingBuilder;

namespace FMBot.Bot.SlashCommands;

public class SettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    private readonly GuildSettingBuilder _guildSettingBuilder;

    private InteractiveService Interactivity { get; }

    public SettingSlashCommands(GuildSettingBuilder guildSettingBuilder, InteractiveService interactivity, UserService userService)
    {
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._userService = userService;
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    public async Task GetGuildSetting(string[] inputs)
    {
        var setting = inputs.First().Replace("gs-", "");

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
        {
            ResponseModel response;
            switch (guildSetting)
            {
                case GuildSetting.TextPrefix:
                    {
                        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
                        {
                            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
                            return;
                        }

                        await this.Context.Interaction.RespondWithModalAsync<PrefixModal>(InteractionConstants.TextPrefixModal);
                    }
                    break;
                case GuildSetting.EmoteReactions:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.DefaultEmbedType:
                    {
                        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
                        {
                            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
                            return;
                        }

                        response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    }
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownActivityThreshold:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.CrownBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings), true);
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownMinimumPlaycount:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.CrownsDisabled:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                case GuildSetting.DisabledCommands:
                    await RespondAsync("Not implemented yet", ephemeral: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [ModalInteraction(InteractionConstants.TextPrefixModal)]
    public async Task SetNewTextPrefix(GuildSettingBuilder.PrefixModal modal)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var response = await this._guildSettingBuilder.SetPrefix(this.Context, modal.NewPrefix);

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
    }

    [ComponentInteraction(InteractionConstants.FmGuildSettingType)]
    public async Task SetGuildEmbedType(string[] inputs)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
        {
            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        ResponseModel response;
        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            response = await this._guildSettingBuilder.SetGuildMode(new ContextModel(this.Context), embedType);
        }
        else
        {
            response = await this._guildSettingBuilder.SetGuildMode(new ContextModel(this.Context), null);
        }

        await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
    }
}
