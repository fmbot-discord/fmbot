using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class PlayModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly PlayBuilder _playBuilder;
    private readonly InteractiveService _interactivity;

    public PlayModals(
        UserService userService,
        PlayBuilder playBuilder,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._playBuilder = playBuilder;
        this._interactivity = interactivity;
    }

    [ComponentInteraction(InteractionConstants.DeleteStreakModal)]
    public async Task StreakDeleteButton()
    {
        var streakIdStr = this.Context.GetModalValue("streak_id");
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (!long.TryParse(streakIdStr, out var streakId))
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Invalid input. Please enter the ID of the streak you want to delete.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var response = await this._playBuilder.DeleteStreakAsync(new ContextModel(this.Context, contextUser), streakId);

        await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
