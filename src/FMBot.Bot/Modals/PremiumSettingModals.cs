using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class PremiumSettingModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly GuildService _guildService;
    private readonly PremiumSettingBuilder _premiumSettingBuilder;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    public PremiumSettingModals(
        GuildService guildService,
        PremiumSettingBuilder premiumSettingBuilder,
        GuildSettingBuilder guildSettingBuilder)
    {
        this._guildService = guildService;
        this._premiumSettingBuilder = premiumSettingBuilder;
        this._guildSettingBuilder = guildSettingBuilder;
    }

    [ComponentInteraction($"{InteractionConstants.SetGuildActivityThresholdModal}:*")]
    [ServerStaffOnly]
    public async Task SetGuildActivityThreshold(string messageId)
    {
        if (!await this._guildSettingBuilder.UserIsAllowed(new ContextModel(this.Context)))
        {
            await GuildSettingBuilder.UserNotAllowedResponse(this.Context);
            return;
        }

        var amount = this.Context.GetModalValue("amount");

        if (!int.TryParse(amount, out var result) || result < 2 || result > 999)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Please enter a valid number between `2` and `999`.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        await this._guildService.SetGuildActivityThresholdDaysAsync(this.Context.Guild, result);

        var response = await this._premiumSettingBuilder.SetGuildActivityThreshold(new ContextModel(this.Context), this.Context.User);
        await this.Context.UpdateMessageEmbed(response, messageId);
    }
}
