using System.Linq;
using System.Threading.Tasks;
using System;
using Discord.Interactions;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Domain.Models;
using FMBot.Bot.Models;
using Fergun.Interactive;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using static FMBot.Bot.Builders.GuildSettingBuilder;
using Discord.WebSocket;
using Discord;

namespace FMBot.Bot.SlashCommands;

public class SettingSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    private readonly GuildSettingBuilder _guildSettingBuilder;
    private readonly IPrefixService _prefixService;
    private readonly GuildService _guildService;

    private InteractiveService Interactivity { get; }

    public SettingSlashCommands(GuildSettingBuilder guildSettingBuilder, InteractiveService interactivity, UserService userService, IPrefixService prefixService, GuildService guildService)
    {
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._userService = userService;
        this._prefixService = prefixService;
        this._guildService = guildService;
    }

    [ComponentInteraction(InteractionConstants.GuildSetting)]
    public async Task GetGuildSetting(string[] inputs)
    {
        var setting = inputs.First().Replace("gs-", "");

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

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
                    response = GuildReactionsAsync(new ContextModel(this.Context), prfx);

                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    return;
                case GuildSetting.DefaultEmbedType:
                    {
                        if (!await this._guildSettingBuilder.UserIsAllowed(this.Context))
                        {
                            await this._guildSettingBuilder.UserNotAllowedResponse(this.Context);
                            return;
                        }

                        response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));

                        await this.Context.SendResponse(this.Interactivity, response, ephemeral: false);
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

        if (Enum.TryParse(inputs.FirstOrDefault(), out FmEmbedType embedType))
        {
            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, embedType);
        }
        else
        {
            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, null);

        }

        var response = await this._guildSettingBuilder.GuildMode(new ContextModel(this.Context));

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await message.ModifyAsync(m =>
        {
            m.Embed = response.Embed.Build();
            m.Components = response.Components.Build();
        });

        await this.Context.Interaction.RespondAsync();
    }
}
