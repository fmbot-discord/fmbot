using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using Serilog;
using NetCord.Gateway;

namespace FMBot.Bot.Extensions;

public static class InteractionContextExtensions
{
    /// <summary>
    /// Gets a TextInput value from a modal interaction by custom ID.
    /// </summary>
    public static string GetModalValue(this ComponentInteractionContext context, string customId)
    {
        if (context.Interaction is not ModalInteraction modal)
            return null;

        // In NetCord, modal TextInput components are wrapped in Label containers
        return modal.Data.Components
            .OfType<Label>()
            .Select(l => l.Component)
            .OfType<TextInput>()
            .FirstOrDefault(t => string.Equals(t.CustomId, customId, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static EmojiProperties ToEmojiProperties(EmojiReference emoji)
    {
        if (emoji == null)
            return null;

        return emoji.Id.HasValue
            ? EmojiProperties.Custom(emoji.Id.Value)
            : EmojiProperties.Standard(emoji.Name);
    }

    public static void LogCommandUsed(this ApplicationCommandContext context,
        CommandResponse commandResponse = CommandResponse.Ok)
    {
        string commandName = context.Interaction switch
        {
            SlashCommandInteraction slashCommand => slashCommand.Data.Name,
            UserCommandInteraction userCommand => userCommand.Data.Name,
            MessageCommandInteraction messageCommand => messageCommand.Data.Name,
            _ => null
        };

        if (context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
            !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            Log.Information(
                "SlashCommandUsed: {discordUserName} / {discordUserId} | UserApp | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, commandResponse, commandName);
        }
        else
        {
            Log.Information(
                "SlashCommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse,
                commandName);
        }

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, commandResponse);
    }

    public static void LogCommandUsed(this ComponentInteractionContext context,
        CommandResponse commandResponse = CommandResponse.Ok)
    {
        string commandName = null;
        if (context.Interaction is MessageComponentInteraction messageComponent)
        {
            var customId = messageComponent.Data?.CustomId;

            if (customId != null)
            {
                var parts = customId.Split('-');

                if (parts.Length >= 2)
                {
                    commandName = parts[0] + '-' + parts[1];
                }
            }
        }

        if (context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
            !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
        {
            Log.Information(
                "ComponentUsed: {discordUserName} / {discordUserId} | UserApp | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, commandResponse, commandName);
        }
        else
        {
            Log.Information(
                "ComponentUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse,
                commandName);
        }

        PublicProperties.UsedCommandsResponses.TryAdd(context.Interaction.Id, commandResponse);
    }

    public static async Task HandleCommandException(this IInteractionContext context, Exception exception,
        string message = null, bool sendReply = true, bool deferFirst = false)
    {
        var referenceId = CommandContextExtensions.GenerateRandomCode();

        var commandName = context.Interaction switch
        {
            SlashCommandInteraction slashCommand => slashCommand.Data.Name,
            UserCommandInteraction userCommand => userCommand.Data.Name,
            MessageComponentInteraction messageComponent => messageComponent.Data?.CustomId,
            ModalInteraction modalInteraction => modalInteraction.Data?.CustomId,
            _ => "Interaction"
        };

        Log.Error(exception,
            "InteractionUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
            referenceId, context.Interaction.User?.Username, context.Interaction.User?.Id, context.Interaction.GuildId,
            context.Interaction.GuildId,
            CommandResponse.Error, message, commandName);

        if (sendReply)
        {
            if (deferFirst)
            {
                await context.Interaction.SendResponseAsync(
                    InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
            }

            if (exception?.Message != null &&
                exception.Message.Contains("50013: Missing Permissions", StringComparison.OrdinalIgnoreCase))
            {
                if (context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType
                        .UserInstall) &&
                    !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType
                        .GuildInstall))
                {
                    await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithContent(
                            "Error while replying: You are missing permissions, so the bot can't reply to your commands.\n" +
                            "Make sure you have permission to 'Embed links' and 'Attach Images'")
                        .WithFlags(MessageFlags.Ephemeral));
                }
                else
                {
                    await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithContent("Error while replying: The bot is missing permissions.\n" +
                                     "Make sure it has permission to 'Embed links' and 'Attach Images'")
                        .WithFlags(MessageFlags.Ephemeral));
                }
            }
            else
            {
                await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithContent(
                        $"Sorry, something went wrong while trying to process `{commandName}`. Please try again later.\n" +
                        $"*Reference id: `{referenceId}`*")
                    .WithFlags(MessageFlags.Ephemeral));
            }
        }

        PublicProperties.UsedCommandsErrorReferences.TryAdd(context.Interaction.Id, referenceId);
    }

    extension(IInteractionContext context)
    {
        public async Task SendResponse(InteractiveService interactiveService,
            ResponseModel response, bool ephemeral = false, ResponseModel extraResponse = null)
        {
            var embeds = new[] { response.Embed };
            if (extraResponse != null)
            {
                embeds = [response.Embed, extraResponse.Embed];
            }

            var flags = ephemeral ? MessageFlags.Ephemeral : (MessageFlags?)null;

            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithContent(response.Text)
                            .WithAllowedMentions(AllowedMentionsProperties.None)
                            .WithFlags(flags)
                            .WithComponents(response.GetMessageComponents())));
                    break;
                case ResponseType.Embed:
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithEmbeds(embeds)
                            .WithFlags(flags)
                            .WithComponents(response.GetMessageComponents())));
                    break;
                case ResponseType.ComponentsV2:
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithComponents(response.GetComponentsV2())
                            .WithFlags(flags)
                            .WithAllowedMentions(AllowedMentionsProperties.None)));
                    break;
                case ResponseType.ImageWithEmbed:
                    response.FileName =
                        StringExtensions.ReplaceInvalidChars(response.FileName);
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .AddAttachments(new AttachmentProperties(
                                response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                response.Stream).WithDescription(response.FileDescription))
                            .WithEmbeds([response.Embed])
                            .WithFlags(flags)
                            .WithComponents(response.GetMessageComponents())));
                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator.Build(),
                        context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        ephemeral: ephemeral);
                    break;
                case ResponseType.ComponentPaginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.ComponentPaginator.Build(),
                        context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        ephemeral: ephemeral);
                    break;
                case ResponseType.SupporterRequired:
                    await context.Interaction.SendResponseAsync(InteractionCallback.Message(
                        new InteractionMessageProperties()
                            .WithContent(
                                "This feature requires .fmbot supporter status. Use `/getsupporter` for more information.")
                            .WithFlags(MessageFlags.Ephemeral)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Interaction.Id))
            {
                PublicProperties.UsedCommandsHintShown.Add(context.Interaction.Id);
            }

            if (response.EmoteReactions != null && response.EmoteReactions.Length != 0 &&
                response.EmoteReactions.FirstOrDefault()?.Length > 0 &&
                response.CommandResponse == CommandResponse.Ok &&
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
            {
                try
                {
                    var message = await context.Interaction.GetResponseAsync();
                    await GuildService.AddReactionsAsync(message, response.EmoteReactions);
                }
                catch (Exception e)
                {
                    await context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                    _ = (await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                            .WithContent("Could not add automatic emoji reactions.\n" +
                                         "-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`.")))
                        .DeleteAfterAsync(30);
                }
            }
        }

        public async Task<ulong?> SendFollowUpResponse(InteractiveService interactiveService, ResponseModel response, bool ephemeral = false)
        {
            ulong? responseId = null;
            var flags = ephemeral ? MessageFlags.Ephemeral : (MessageFlags?)null;

            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    var text = await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithContent(response.Text)
                        .WithAllowedMentions(AllowedMentionsProperties.None)
                        .WithFlags(flags)
                        .WithComponents(response.GetMessageComponents()));
                    responseId = text.Id;
                    break;
                case ResponseType.Embed:
                    var embed = await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .WithEmbeds([response.Embed])
                        .WithFlags(flags)
                        .WithComponents(response.GetMessageComponents()));
                    responseId = embed.Id;
                    break;
                case ResponseType.ComponentsV2:
                    if (response.Stream is { Length: > 0 })
                    {
                        response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                        var componentImage = await context.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties()
                                .AddAttachments(new AttachmentProperties(
                                    response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                    response.Stream).WithDescription(response.FileDescription))
                                .WithComponents(response.ComponentsV2)
                                .WithFlags(flags)
                                .WithAllowedMentions(AllowedMentionsProperties.None));

                        await response.Stream.DisposeAsync();
                        responseId = componentImage.Id;
                    }
                    else
                    {
                        var components = await context.Interaction.SendFollowupMessageAsync(
                            new InteractionMessageProperties()
                                .WithComponents(response.ComponentsV2)
                                .WithFlags(flags)
                                .WithAllowedMentions(AllowedMentionsProperties.None));
                        responseId = components.Id;
                    }

                    break;
                case ResponseType.Paginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.StaticPaginator.Build(),
                        context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        ephemeral: ephemeral);
                    break;
                case ResponseType.ComponentPaginator:
                    _ = interactiveService.SendPaginatorAsync(
                        response.ComponentPaginator.Build(),
                        context.Interaction,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds),
                        ephemeral: ephemeral);
                    break;
                case ResponseType.ImageWithEmbed:
                    response.FileName =
                        StringExtensions.ReplaceInvalidChars(response.FileName);
                    var imageWithEmbed = await context.Interaction.SendFollowupMessageAsync(
                        new InteractionMessageProperties()
                            .AddAttachments(new AttachmentProperties(
                                response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                response.Stream).WithDescription(response.FileDescription))
                            .WithEmbeds([response.Embed])
                            .WithFlags(flags)
                            .WithComponents(response.GetMessageComponents()));

                    await response.Stream.DisposeAsync();
                    responseId = imageWithEmbed.Id;
                    break;
                case ResponseType.ImageOnly:
                    response.FileName =
                        StringExtensions.ReplaceInvalidChars(response.FileName);
                    var image = await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                        .AddAttachments(new AttachmentProperties(
                            response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                            response.Stream).WithDescription(response.FileDescription))
                        .WithFlags(flags));

                    await response.Stream.DisposeAsync();
                    responseId = image.Id;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (responseId.HasValue)
            {
                PublicProperties.UsedCommandsResponseMessageId.TryAdd(context.Interaction.Id, responseId.Value);
                PublicProperties.UsedCommandsResponseContextId.TryAdd(responseId.Value, context.Interaction.Id);
            }

            if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Interaction.Id))
            {
                PublicProperties.UsedCommandsHintShown.Add(context.Interaction.Id);
            }

            if (response.EmoteReactions != null && response.EmoteReactions.Length != 0 &&
                response.EmoteReactions.FirstOrDefault()?.Length > 0 &&
                response.CommandResponse == CommandResponse.Ok &&
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
            {
                try
                {
                    var message = await context.Interaction.GetResponseAsync();
                    await GuildService.AddReactionsAsync(message, response.EmoteReactions);
                }
                catch (Exception e)
                {
                    await context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                    _ = (await context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                            .WithContent("Could not add automatic emoji reactions.\n" +
                                         "-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`.")))
                        .DeleteAfterAsync(30);
                }
            }

            return responseId;
        }
    }

    public static async Task UpdateInteractionEmbed(this ComponentInteractionContext context, ResponseModel response,
        InteractiveService interactiveService = null, bool defer = true)
    {
        var message = (context.Interaction as MessageComponentInteraction)?.Message;

        if (message == null)
        {
            return;
        }

        if (response.ResponseType == ResponseType.Paginator)
        {
            if (defer)
            {
                await context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);
            }

            await context.ModifyPaginator(interactiveService, message, response);
            return;
        }

        await context.ModifyMessage(message, response, defer);
    }


    extension(IInteractionContext context)
    {
        public async Task DisableActionRows(bool interactionEdit = false,
            string specificButtonOnly = null)
        {
            var message = (context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var newComponents = new List<IMessageComponentProperties>();
            foreach (var component in message.Components)
            {
                if (component is ActionRow actionRow)
                {
                    var newRow = new ActionRowProperties();
                    foreach (var rowComponent in actionRow.Components)
                    {
                        if (rowComponent is Button button)
                        {
                            var shouldDisable = specificButtonOnly == null || button.CustomId == specificButtonOnly;
                            newRow.AddComponents(new ButtonProperties(button.CustomId, button.Label, button.Style)
                            {
                                Emoji = ToEmojiProperties(button.Emoji),
                                Disabled = shouldDisable
                            });
                        }
                        else if (rowComponent is LinkButton linkButton)
                        {
                            newRow.AddComponents(new LinkButtonProperties(linkButton.Url, linkButton.Label)
                            {
                                Emoji = ToEmojiProperties(linkButton.Emoji),
                                Disabled = specificButtonOnly == null
                            });
                        }
                    }

                    if (newRow.Any())
                    {
                        newComponents.Add(newRow);
                    }
                }
                else if (component is StringMenu stringMenu)
                {
                    // In NetCord, select menus are added directly as top-level components
                    newComponents.Add(new StringMenuProperties(stringMenu.CustomId, stringMenu.Options.Select(o =>
                        new StringMenuSelectOptionProperties(o.Label, o.Value)
                        {
                            Description = o.Description,
                            Emoji = ToEmojiProperties(o.Emoji),
                            Default = o.Default
                        }))
                    {
                        Placeholder = stringMenu.Placeholder,
                        MinValues = stringMenu.MinValues,
                        MaxValues = stringMenu.MaxValues,
                        Disabled = true
                    });
                }
            }

            await context.ModifyComponentsList(message, newComponents, interactionEdit);
        }

        public async Task DisableInteractionButtons(bool interactionEdit = false,
            string specificButtonOnly = null, bool addLoaderToSpecificButton = false)
        {
            var message = (context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var newComponents = new ActionRowProperties();
            foreach (var component in message.Components)
            {
                if (component is not ActionRow actionRow)
                {
                    continue;
                }

                foreach (var subComponent in actionRow.Components)
                {
                    if (subComponent is Button button)
                    {
                        if (specificButtonOnly != null && specificButtonOnly == button.CustomId)
                        {
                            if (addLoaderToSpecificButton)
                            {
                                newComponents.AddComponents(
                                    new ButtonProperties(button.CustomId, button.Label, button.Style)
                                    {
                                        Emoji = EmojiProperties.Custom(DiscordConstants.Loading),
                                        Disabled = true
                                    });
                            }
                            else
                            {
                                newComponents.AddComponents(
                                    new ButtonProperties(button.CustomId, button.Label, button.Style)
                                    {
                                        Emoji = ToEmojiProperties(button.Emoji),
                                        Disabled = true
                                    });
                            }
                        }
                        else if (specificButtonOnly == null)
                        {
                            newComponents.AddComponents(
                                new ButtonProperties(button.CustomId, button.Label, button.Style)
                                {
                                    Emoji = ToEmojiProperties(button.Emoji),
                                    Disabled = true
                                });
                        }
                        else
                        {
                            newComponents.AddComponents(
                                new ButtonProperties(button.CustomId, button.Label, button.Style)
                                {
                                    Emoji = ToEmojiProperties(button.Emoji),
                                    Disabled = false
                                });
                        }
                    }
                }
            }

            await context.ModifyComponents(message, newComponents, interactionEdit);
        }

        public async Task AddLinkButton(LinkButtonProperties extraButton)
        {
            var message = (context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var newComponents = new ActionRowProperties();
            foreach (var component in message.Components)
            {
                if (component is not ActionRow actionRow)
                {
                    continue;
                }

                foreach (var subComponent in actionRow.Components)
                {
                    if (subComponent is Button button)
                    {
                        newComponents.AddComponents(new ButtonProperties(button.CustomId, button.Label, button.Style)
                        {
                            Emoji = ToEmojiProperties(button.Emoji),
                            Disabled = true
                        });
                    }
                }
            }

            newComponents.AddComponents(extraButton);

            await context.ModifyComponents(message, newComponents);
        }

        public async Task EnableInteractionButtons()
        {
            var message = (context.Interaction as MessageComponentInteraction)?.Message;

            if (message == null)
            {
                return;
            }

            var newComponents = new ActionRowProperties();
            foreach (var component in message.Components)
            {
                if (component is not ActionRow actionRow)
                {
                    continue;
                }

                foreach (var subComponent in actionRow.Components)
                {
                    if (subComponent is Button button)
                    {
                        newComponents.AddComponents(new ButtonProperties(button.CustomId, button.Label, button.Style)
                        {
                            Emoji = ToEmojiProperties(button.Emoji),
                            Disabled = false
                        });
                    }
                }
            }

            await context.ModifyComponents(message, newComponents);
        }

        public async Task UpdateMessageEmbed(ResponseModel response,
            string messageId, bool interactionEdit = false, bool defer = true)
        {
            var parsedMessageId = ulong.Parse(messageId);
            var msg = await context.Interaction.Channel.GetMessageAsync(parsedMessageId);

            if (msg is not Message message)
            {
                return;
            }

            await context.ModifyMessage(message, response, defer);
        }

        public async Task ModifyComponents(RestMessage message,
            ActionRowProperties newComponents, bool interactionEdit = false)
        {
            var components = newComponents != null ? new[] { newComponents } : null;
            var isUserInstalledApp =
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall);
            var isComponentInteraction = context.Interaction is MessageComponentInteraction or ModalInteraction;

            if (isUserInstalledApp || interactionEdit || isComponentInteraction)
            {
                await context.Interaction.ModifyResponseAsync(m => m.Components = components);
            }
            else
            {
                await message.ModifyAsync(m => m.Components = components);
            }
        }

        private async Task ModifyComponentsList(RestMessage message,
            IEnumerable<IMessageComponentProperties> newComponents, bool interactionEdit = false)
        {
            var isUserInstalledApp =
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall);
            var isComponentInteraction = context.Interaction is MessageComponentInteraction or ModalInteraction;

            if (isUserInstalledApp || interactionEdit || isComponentInteraction)
            {
                await context.Interaction.ModifyResponseAsync(m => m.Components = newComponents);
            }
            else
            {
                await message.ModifyAsync(m => m.Components = newComponents);
            }
        }

        public async Task ModifyMessage(RestMessage message,
            ResponseModel response, bool defer = true, bool interactionEdit = false)
        {
            if (defer)
            {
                await context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);
            }

            IEnumerable<IMessageComponentProperties> components = response.ResponseType == ResponseType.ComponentsV2
                ? response.ComponentsV2
                : response.GetMessageComponents();

            var attachments = response.Stream != null
                ? new List<AttachmentProperties>
                {
                    new(response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName, response.Stream)
                }
                : null;

            var isUserInstalledApp =
                context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall);

            // For component interactions (buttons, select menus, modals), always use ModifyResponseAsync
            // since we typically defer first and need to complete the deferred response
            var isComponentInteraction = context.Interaction is MessageComponentInteraction or ModalInteraction;

            if (isUserInstalledApp || interactionEdit || isComponentInteraction)
            {
                await context.Interaction.ModifyResponseAsync(m =>
                {
                    m.Components = components;
                    m.Embeds = response.ResponseType == ResponseType.ComponentsV2 ? null : [response.Embed];
                    m.Attachments = attachments;
                    m.AllowedMentions = AllowedMentionsProperties.None;
                });
            }
            else
            {
                await message.ModifyAsync(m =>
                {
                    m.Components = components;
                    m.Embeds = response.ResponseType == ResponseType.ComponentsV2 ? null : [response.Embed];
                    m.Attachments = attachments;
                    m.AllowedMentions = AllowedMentionsProperties.None;
                });
            }
        }

        private async Task ModifyPaginator(InteractiveService interactiveService,
            Message message, ResponseModel response)
        {
            if (context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.UserInstall) &&
                !context.Interaction.AuthorizingIntegrationOwners.ContainsKey(ApplicationIntegrationType.GuildInstall))
            {
                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator.Build(),
                    context.Interaction,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
            }
            else
            {
                if (message.Attachments != null && message.Attachments.Any())
                {
                    await message.ModifyAsync(m => { m.Attachments = null; });
                }

                _ = interactiveService.SendPaginatorAsync(
                    response.StaticPaginator.Build(),
                    message,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
            }
        }
    }
}
