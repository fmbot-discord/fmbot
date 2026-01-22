using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services.Commands;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class CommandContextExtensions
{
    extension(CommandContext context)
    {
        private void LogCommandUsed(CommandResponse commandResponse = CommandResponse.Ok)
        {
            var shardId = context.Client.Shard?.Id ?? 0;
            Log.Information(
                "CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} #{shardId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, shardId, commandResponse,
                context.Message.Content);

            PublicProperties.UsedCommandsResponses.TryAdd(context.Message.Id, commandResponse);
        }

        public async Task LogCommandUsedAsync(ResponseModel response,
            UserService userService,
            string commandName = null)
        {
            context.LogCommandUsed(response.CommandResponse);

            await userService.InsertAndCompleteInteractionAsync(
                context.Message.Id,
                context.User.Id,
                commandName,
                response.CommandResponse,
                context.Guild?.Id,
                context.Channel?.Id,
                UserInteractionType.TextCommand,
                context.Message.Content,
                referencedMusic: response.ReferencedMusic);
        }

        public async Task HandleCommandException(Exception exception,
            UserService userService, string message = null, bool sendReply = true)
        {
            var referenceId = GenerateRandomCode();
            var shardId = context.Client.Shard?.Id ?? 0;
            Log.Error(exception,
                "CommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} #{shardId} | {commandResponse} ({message}) | {messageContent}",
                referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, shardId,
                CommandResponse.Error, message, context.Message.Content);

            if (sendReply)
            {
                if (exception?.Message != null && exception.Message.Contains("error 50013"))
                {
                    await context.Client.Rest.SendMessageAsync(context.Message.ChannelId, new MessageProperties
                    {
                        Content =
                            "Sorry, something went wrong because the bot is missing permissions. Make sure the bot has `Embed links` and `Attach Files`.\n" +
                            "Please adjust .fmbot permissions or ask server staff to do this for you.\n" +
                            $"*Reference id: `{referenceId}`*",
                        AllowedMentions = AllowedMentionsProperties.None
                    });
                }
                else
                {
                    await context.Client.Rest.SendMessageAsync(context.Message.ChannelId, new MessageProperties
                    {
                        Content = "Sorry, something went wrong. Please try again later.\n" +
                                  $"*Reference id: `{referenceId}`*",
                        AllowedMentions = AllowedMentionsProperties.None
                    });
                }
            }

            if (userService != null)
            {
                var messageId = context.Message.Id;
                _ = Task.Run(async () =>
                {
                    await userService.UpdateCommandInteractionAsync(
                        messageId,
                        commandResponse: CommandResponse.Error,
                        errorReference: referenceId);
                });
            }
        }

        public async Task<RestMessage> SendResponse(InteractiveService interactiveService, ResponseModel response, UserService userService)
        {
            RestMessage responseMessage = null;
            if (PublicProperties.UsedCommandsResponseMessageId.ContainsKey(context.Message.Id))
            {
                switch (response.ResponseType)
                {
                    case ResponseType.Text:
                        await context.Client.Rest.ModifyMessageAsync(
                            context.Message.ChannelId,
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                            msg =>
                            {
                                msg.Content = response.Text;
                                msg.Embeds = null;
                                msg.Components = response.Components?.Any() == true ? new[] { response.Components } : null;
                            });
                        break;
                    case ResponseType.Embed:
                    case ResponseType.ImageWithEmbed:
                    case ResponseType.ImageOnly:
                        await context.Client.Rest.ModifyMessageAsync(
                            context.Message.ChannelId,
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                            msg =>
                            {
                                msg.Content = response.Text;
                                msg.Embeds = response.ResponseType == ResponseType.ImageOnly
                                    ? null
                                    : new[] { response.Embed };
                                msg.Components = response.Components?.Any() == true ? new[] { response.Components } : null;
                                msg.Attachments = response.Stream != null
                                    ? new List<AttachmentProperties>
                                    {
                                        new AttachmentProperties(
                                            response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                            response.Stream).WithDescription(response.FileDescription)
                                    }
                                    : null;
                            });

                        if (response.Stream != null)
                        {
                            await response.Stream.DisposeAsync();
                        }

                        break;
                    case ResponseType.ComponentsV2:
                        await context.Client.Rest.ModifyMessageAsync(
                            context.Message.ChannelId,
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                            msg =>
                            {
                                msg.Flags = MessageFlags.IsComponentsV2;
                                msg.Components = response.GetComponentsV2();
                                msg.Attachments = response.Stream != null
                                    ? new List<AttachmentProperties>
                                    {
                                        new AttachmentProperties(
                                            response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                            response.Stream).WithDescription(response.FileDescription)
                                    }
                                    : null;
                            });

                        if (response.Stream != null)
                        {
                            await response.Stream.DisposeAsync();
                        }

                        break;
                    case ResponseType.Paginator:
                        var existingMsgPaginator = await context.Client.Rest.GetMessageAsync(context.Message.ChannelId,
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id]);

                        if (existingMsgPaginator.Attachments != null && existingMsgPaginator.Attachments.Any())
                        {
                            await context.Client.Rest.ModifyMessageAsync(
                                context.Message.ChannelId,
                                PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                                msg => { msg.Attachments = null; });
                        }

                        await interactiveService.SendPaginatorAsync(
                            response.ComponentPaginator.Build(),
                            existingMsgPaginator,
                            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (response.ReferencedMusic != null)
                {
                    PublicProperties.UsedCommandsReferencedMusic.TryRemove(context.Message.Id, out _);
                    PublicProperties.UsedCommandsReferencedMusic.TryAdd(context.Message.Id, response.ReferencedMusic);
                }

                return null;
            }

            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    var text = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                        new MessageProperties()
                            .WithContent(response.Text)
                            .WithAllowedMentions(AllowedMentionsProperties.None)
                            .WithComponents(response.GetMessageComponents()));
                    responseMessage = text;
                    break;
                case ResponseType.Embed:
                    var embed = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                        new MessageProperties()
                            .AddEmbeds(response.Embed)
                            .WithComponents(response.GetMessageComponents()));
                    responseMessage = embed;
                    break;
                case ResponseType.Paginator:
                    var channel = context.Guild == null ? await context.User.GetDMChannelAsync() : context.Channel;
                    var componentPaginatorResult = await interactiveService.SendPaginatorAsync(
                        response.ComponentPaginator.Build(),
                        channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    responseMessage = componentPaginatorResult.Message;
                    break;
                case ResponseType.ImageWithEmbed:
                    response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                    var imageWithEmbed = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                        new MessageProperties()
                            .AddAttachments(new AttachmentProperties(
                                response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                response.Stream).WithDescription(response.FileDescription))
                            .AddEmbeds(response.Embed)
                            .WithComponents(response.GetMessageComponents()));

                    await response.Stream.DisposeAsync();
                    responseMessage = imageWithEmbed;
                    break;
                case ResponseType.ImageOnly:
                    response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                    var image = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                        new MessageProperties()
                            .AddAttachments(new AttachmentProperties(
                                response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                response.Stream).WithDescription(response.FileDescription))
                            .WithComponents(response.GetMessageComponents()));
                    await response.Stream.DisposeAsync();
                    responseMessage = image;
                    break;
                case ResponseType.ComponentsV2:
                    if (response.Stream is { Length: > 0 })
                    {
                        response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                        var componentImage = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                            new MessageProperties()
                                .AddAttachments(new AttachmentProperties(
                                    response.Spoiler ? $"SPOILER_{response.FileName}" : response.FileName,
                                    response.Stream).WithDescription(response.FileDescription))
                                .WithComponents(response.GetComponentsV2())
                                .WithFlags(MessageFlags.IsComponentsV2)
                                .WithAllowedMentions(AllowedMentionsProperties.None));

                        await response.Stream.DisposeAsync();
                        responseMessage = componentImage;
                    }
                    else
                    {
                        var components = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                            new MessageProperties()
                                .WithComponents(response.GetComponentsV2())
                                .WithFlags(MessageFlags.IsComponentsV2)
                                .WithAllowedMentions(AllowedMentionsProperties.None));
                        responseMessage = components;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            PublicProperties.UsedCommandsResponseMessageId.TryAdd(context.Message.Id, responseMessage.Id);
            PublicProperties.UsedCommandsResponseContextId.TryAdd(responseMessage.Id, context.Message.Id);

            if (response.ReferencedMusic != null)
            {
                PublicProperties.UsedCommandsReferencedMusic.TryAdd(context.Message.Id, response.ReferencedMusic);
            }

            if (response.EmoteReactions != null && response.EmoteReactions.Length != 0 &&
                response.EmoteReactions.FirstOrDefault()?.Length > 0 && response.CommandResponse == CommandResponse.Ok)
            {
                try
                {
                    await GuildService.AddReactionsAsync(responseMessage, response.EmoteReactions);
                }
                catch (Exception e)
                {
                    await context.HandleCommandException(e, userService, "Could not add emote reactions", sendReply: false);
                    var errorMsg = await context.Client.Rest.SendMessageAsync(context.Message.ChannelId,
                        new MessageProperties()
                            .WithContent("Could not add automatic emoji reactions.\n" +
                                         "-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`."));
                    _ = errorMsg.DeleteAfterAsync(30);
                }
            }

            if (userService != null)
            {
                var messageId = context.Message.Id;
                var responseIdForDb = responseMessage.Id;
                var commandResponse = response.CommandResponse;
                var artist = response.ReferencedMusic?.Artist;
                var album = response.ReferencedMusic?.Album;
                var track = response.ReferencedMusic?.Track;
                var hintShown = response.HintShown;

                await userService.UpdateCommandInteractionAsync(
                    messageId,
                    responseId: responseIdForDb,
                    commandResponse: commandResponse,
                    artist: artist,
                    album: album,
                    track: track,
                    hintShown: hintShown);
            }

            return responseMessage;
        }
    }

    public static string GenerateRandomCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
        const int size = 8;

        var data = new byte[4 * size];
        using (var crypto = RandomNumberGenerator.Create())
        {
            crypto.GetBytes(data);
        }

        var result = new StringBuilder(size);
        for (var i = 0; i < size; i++)
        {
            var rnd = Math.Abs(BitConverter.ToInt32(data, i * 4));
            var idx = rnd % chars.Length;

            result.Append(chars[idx]);
        }

        return result.ToString();
    }

    public static ReferencedMusic GetReferencedMusic(ulong lookupId)
    {
        if (PublicProperties.UsedCommandsResponseContextId.ContainsKey(lookupId))
        {
            PublicProperties.UsedCommandsResponseContextId.TryGetValue(lookupId, out lookupId);
        }

        return PublicProperties.UsedCommandsReferencedMusic.GetValueOrDefault(lookupId);
    }
}
