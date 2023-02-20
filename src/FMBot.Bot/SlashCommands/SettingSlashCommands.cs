using System.Linq;
using System.Threading.Tasks;
using System;
using Discord.Interactions;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Bot.Models;
using Fergun.Interactive;
using FMBot.Bot.Services;
using Newtonsoft.Json.Linq;

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

    [ComponentInteraction(Constants.GuildSetting)]
    public async Task SetEmbedType(string[] inputs)
    {
        var setting = inputs.First().Replace("gs-", "");

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
        {
            ResponseModel response;
            switch (guildSetting)
            {
                case GuildSetting.TextPrefix:
                    if (setting.Contains("view"))
                    {
                        await this._guildSettingBuilder.RespondToPrefixSetter(this.Context);
                    }
                    break;
                case GuildSetting.EmoteReactions:
                    break;
                case GuildSetting.DefaultEmbedType:
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings));
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownActivityThreshold:
                    break;
                case GuildSetting.CrownBlockedUsers:
                    response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(this.Context, userSettings), true);
                    await this.Context.SendResponse(this.Interactivity, response, ephemeral: true);
                    break;
                case GuildSetting.CrownMinimumPlaycount:
                    break;
                case GuildSetting.CrownsDisabled:
                    break;
                case GuildSetting.DisabledCommands:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    [ModalInteraction(Constants.TextPrefixModal)]
    public async Task SetNewTextPrefix(GuildSettingBuilder.PrefixModal modal)
    {
        await this._guildSettingBuilder.RespondWithPrefixSet(this.Context, modal.NewPrefix);
    }
}
