using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Modals;

public class UserModals : ComponentInteractionModule<ComponentInteractionContext>
{
    private readonly UserService _userService;
    private readonly FriendsService _friendsService;
    private readonly UserBuilder _userBuilder;
    private readonly InteractiveService _interactivity;

    public UserModals(
        UserService userService,
        FriendsService friendsService,
        UserBuilder userBuilder,
        InteractiveService interactivity)
    {
        this._userService = userService;
        this._friendsService = friendsService;
        this._userBuilder = userBuilder;
        this._interactivity = interactivity;
    }

    [ComponentInteraction($"{InteractionConstants.RemoveFmbotAccountModal}-*-*")]
    [UsernameSetRequired]
    public async Task RemoveConfirmAsync(string discordUserId, string messageId)
    {
        var confirmation = this.Context.GetModalValue("confirmation");
        var parsedId = ulong.Parse(discordUserId);

        if (parsedId != this.Context.User.Id)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Hey, this button is not for you. At least you tried.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (confirmation?.ToLower() != "confirm")
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Account deletion cancelled, wrong modal input")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (userSettings == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("We don't have any data from you in our database")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not Message message)
        {
            return;
        }

        try
        {
            await message.ModifyAsync(m => m.Components = []);

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
            await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

            await this._userService.DeleteUser(userSettings.UserId);

            var followUpEmbed = new EmbedProperties();
            followUpEmbed.WithTitle("Removal successful");
            followUpEmbed.WithDescription(
                "Your settings, friends and any other data have been successfully deleted from .fmbot.");
            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithEmbeds([followUpEmbed])
                .WithFlags(MessageFlags.Ephemeral));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Shortcuts.CreateModal}-*")]
    public async Task CreateShortcutModal(string messageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");
            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);

            var response = await this._userBuilder.CreateShortcutAsync(
                new ContextModel(this.Context, contextUser),
                input,
                output);

            if (response == null)
            {
                var parsedMessageId = ulong.Parse(messageId);
                if (parsedMessageId != 0)
                {
                    var list = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context, contextUser));
                    await this.Context.UpdateMessageEmbed(list, messageId);
                }
            }
            else
            {
                await this.Context.SendResponse(this._interactivity, response, ephemeral: true);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"{InteractionConstants.Shortcuts.ModifyModal}-*-*")]
    public async Task ModifyShortcutModal(string shortcutId, string overviewMessageId)
    {
        try
        {
            var input = this.Context.GetModalValue("input");
            var output = this.Context.GetModalValue("output");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var id = int.Parse(shortcutId);

            var response = await this._userBuilder.ModifyShortcutAsync(
                new ContextModel(this.Context, contextUser),
                id,
                input,
                output);

            if (response == null)
            {
                var parsedOverviewMessageId = ulong.Parse(overviewMessageId);
                if (parsedOverviewMessageId != 0)
                {
                    var list = await this._userBuilder.ListShortcutsAsync(new ContextModel(this.Context, contextUser));
                    await this.Context.UpdateMessageEmbed(list, overviewMessageId, defer: false);
                }

                var manage = await this._userBuilder.ManageShortcutAsync(new ContextModel(this.Context, contextUser),
                    id,
                    parsedOverviewMessageId);
                await this.Context.Interaction.ModifyResponseAsync(m =>
                    m.Components = manage.ComponentsV2);
            }
            else
            {
                await this.Context.SendFollowUpResponse(this._interactivity, response, ephemeral: true);
                this.Context.LogCommandUsed(response.CommandResponse);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
