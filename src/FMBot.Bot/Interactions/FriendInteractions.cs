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
using FMBot.Domain;
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
    public async Task FriendManageButton(string friendId, string page, string source = "manage")
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
            var friend = await friendsService.GetFriendAsync(int.Parse(friendId));

            if (friend == null)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("This friend could not be found.")
                    .WithFlags(MessageFlags.Ephemeral)));
                return;
            }

            if (friend.UserId != contextUser.UserId)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent("Only the person who added this friend can change it.")
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
                    $"{InteractionConstants.Friends.TypeModal}:{friendId}:{page}:{source}",
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
    public async Task FriendTypeModal(string friendId, string page, string source)
    {
        var acknowledged = false;
        try
        {
            var selected = this.Context.GetModalMenuValue("friend_type");

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            if (int.TryParse(selected, out var selectedType)
                && (FriendType)selectedType == FriendType.CloseFriend
                && contextUser.UserType == UserType.User)
            {
                var supporterRequired = new ComponentContainerProperties();
                supporterRequired.AddComponent(new TextDisplayProperties(
                    "**Close friends are only available for supporters.** Add someone as a close friend to keep them always visible in WhoKnows no matter their rank, plus in `friendsfm`."));
                supporterRequired.AddComponent(new ActionRowProperties().WithButton(Constants.GetSupporterButton,
                    style: ButtonStyle.Primary,
                    customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: "friends-closefriend")));

                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithComponents([supporterRequired])
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)));
                return;
            }

            if (source == "add")
            {
                var (note, success) = await friendBuilders.ApplyFriendTypeSelectionAsync(
                    new ContextModel(this.Context, contextUser), int.Parse(friendId), selected);

                var container = new ComponentContainerProperties();
                container.WithAccentColor(success
                    ? DiscordConstants.SuccessColorGreen
                    : DiscordConstants.WarningColorOrange);
                container.AddComponent(new TextDisplayProperties(note));

                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithComponents([container])
                    .WithFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)));
                return;
            }

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);
            acknowledged = true;

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
            await this.Context.HandleCommandException(e, userService, deferFirst: !acknowledged);
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
