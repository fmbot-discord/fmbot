using System;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Factories;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class FriendInteractions(
    UserService userService,
    FriendsService friendsService,
    FriendBuilders friendBuilders,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Friends.Overview)]
    [UsernameSetRequired]
    public async Task FriendOverviewButton()
    {
        try
        {
            await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response = await friendBuilders.ManageFriendsAsync(new ContextModel(this.Context, contextUser));

            await this.Context.SendFollowUpResponse(interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Friends.OverviewPage)]
    [UsernameSetRequired]
    public async Task FriendOverviewPageButton(string direction, string page)
    {
        try
        {
            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var response =
                await friendBuilders.ManageFriendsAsync(new ContextModel(this.Context, contextUser), int.Parse(page));

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = response.GetComponentsV2();
                m.AllowedMentions = AllowedMentionsProperties.None;
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Friends.Manage)]
    [UsernameSetRequired]
    public async Task FriendManageButton(string friendId, string page)
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var friend = await friendsService.GetFriendAsync(int.Parse(friendId));

            if (friend == null || friend.UserId != contextUser.UserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("This friend could not be found.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            var friendName = friend.FriendUser?.UserNameLastFM ?? friend.LastFMUserName;
            var title = $"Edit {friendName}";
            if (title.Length > 45)
            {
                title = title[..45];
            }

            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateFriendTypeModal(
                    $"{InteractionConstants.Friends.TypeModal}:{friendId}:{page}",
                    title,
                    friend.FriendType,
                    contextUser.UserType != UserType.User)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.Friends.TypeModal)]
    [UsernameSetRequired]
    public async Task FriendTypeModal(string friendId, string page)
    {
        try
        {
            var selected = this.Context.GetModalMenuValue("friend_type");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            string error = null;
            if (selected == "remove")
            {
                error = await friendBuilders.RemoveFriendAsync(new ContextModel(this.Context, contextUser),
                    int.Parse(friendId));
            }
            else if (int.TryParse(selected, out var typeValue) && Enum.IsDefined(typeof(FriendType), typeValue))
            {
                error = await friendBuilders.SetFriendTypeAsync(new ContextModel(this.Context, contextUser),
                    int.Parse(friendId), (FriendType)typeValue);
            }

            var response = await friendBuilders.ManageFriendsAsync(new ContextModel(this.Context, contextUser),
                int.Parse(page), error);

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = response.GetComponentsV2();
                m.AllowedMentions = AllowedMentionsProperties.None;
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Friends.Sync)]
    [UsernameSetRequired]
    public async Task FriendSyncButton()
    {
        try
        {
            await RespondAsync(InteractionCallback.Modal(
                ModalFactory.CreateLastFmSyncModal(InteractionConstants.Friends.SyncModal)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [ComponentInteraction(InteractionConstants.Friends.SyncModal)]
    [UsernameSetRequired]
    public async Task FriendSyncModal()
    {
        try
        {
            var action = this.Context.GetModalRadioValue("sync_action");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var (note, success) = action == "remove"
                ? await friendBuilders.RemoveSyncedLastFmFriendsAsync(new ContextModel(this.Context, contextUser))
                : await friendBuilders.SyncLastFmFriendsAsync(new ContextModel(this.Context, contextUser));

            var response =
                await friendBuilders.ManageFriendsAsync(new ContextModel(this.Context, contextUser), 0, note, success);

            await this.Context.Interaction.ModifyResponseAsync(m =>
            {
                m.Components = response.GetComponentsV2();
                m.AllowedMentions = AllowedMentionsProperties.None;
            });
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
