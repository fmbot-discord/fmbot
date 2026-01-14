using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class CommandContextExtensions
{
    extension(CommandContext context)
    {
        private async Task<TextChannel> GetChannelAsync()
        {
            if (context.Channel != null)
            {
                return context.Channel;
            }

            if (context.Guild == null)
            {
                return await context.User.GetDMChannelAsync();
            }

            var channel = await context.Client.Rest.GetChannelAsync(context.Message.ChannelId);
            return channel as TextChannel;
        }

        public void LogCommandUsed(CommandResponse commandResponse = CommandResponse.Ok)
        {
            var shardId = context.Client.Shard?.Id ?? 0;
            Log.Information(
                "CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} #{shardId} | {commandResponse} | {messageContent}",
                context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, shardId, commandResponse,
                context.Message.Content);

            PublicProperties.UsedCommandsResponses.TryAdd(context.Message.Id, commandResponse);
        }

        public async Task HandleCommandException(Exception exception,
            string message = null, bool sendReply = true)
        {
            var referenceId = GenerateRandomCode();
            var shardId = context.Client.Shard?.Id ?? 0;
            Log.Error(exception,
                "CommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} #{shardId} | {commandResponse} ({message}) | {messageContent}",
                referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, shardId,
                CommandResponse.Error, message, context.Message.Content);

            if (sendReply)
            {
                var channel = await context.GetChannelAsync();
                if (exception?.Message != null && exception.Message.Contains("error 50013"))
                {
                    await channel.SendMessageAsync(new MessageProperties
                    {
                        Content = "Sorry, something went wrong because the bot is missing permissions. Make sure the bot has `Embed links` and `Attach Files`.\n" +
                                  "Please adjust .fmbot permissions or ask server staff to do this for you.\n" +
                                  $"*Reference id: `{referenceId}`*",
                        AllowedMentions = AllowedMentionsProperties.None
                    });
                }
                else
                {
                    await channel.SendMessageAsync(new MessageProperties
                    {
                        Content = "Sorry, something went wrong. Please try again later.\n" +
                                  $"*Reference id: `{referenceId}`*",
                        AllowedMentions = AllowedMentionsProperties.None
                    });
                }
            }

            PublicProperties.UsedCommandsErrorReferences.TryAdd(context.Message.Id, referenceId);
        }

        public async Task<RestMessage> SendResponse(InteractiveService interactiveService, ResponseModel response)
        {
            RestMessage responseMessage = null;
            var channel = await context.GetChannelAsync();

            if (PublicProperties.UsedCommandsResponseMessageId.ContainsKey(context.Message.Id))
            {
                switch (response.ResponseType)
                {
                    case ResponseType.Text:
                        await channel.ModifyMessageAsync(
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id], msg =>
                            {
                                msg.Content = response.Text;
                                msg.Embeds = null;
                                msg.Components = response.Components != null ? new[] { response.Components } : null;
                            });
                        break;
                    case ResponseType.Embed:
                    case ResponseType.ImageWithEmbed:
                    case ResponseType.ImageOnly:
                        await channel.ModifyMessageAsync(
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id], msg =>
                            {
                                msg.Content = response.Text;
                                msg.Embeds = response.ResponseType == ResponseType.ImageOnly
                                    ? null
                                    : new[] { response.Embed };
                                msg.Components = response.Components != null ? new[] { response.Components } : null;
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
                        await channel.ModifyMessageAsync(
                            PublicProperties.UsedCommandsResponseMessageId[context.Message.Id], msg =>
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
                        var existingMsgPaginator =
                            await channel.GetMessageAsync(
                                PublicProperties.UsedCommandsResponseMessageId[context.Message.Id]);
                        if (existingMsgPaginator.Attachments != null && existingMsgPaginator.Attachments.Any())
                        {
                            await channel.ModifyMessageAsync(
                                PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                                msg => { msg.Attachments = null; });
                        }

                        // Delete old message and send new paginator
                        await channel.DeleteMessageAsync(existingMsgPaginator.Id);
                        await interactiveService.SendPaginatorAsync(
                            response.StaticPaginator.Build(),
                            channel,
                            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                        break;
                    case ResponseType.ComponentPaginator:
                        var existingMsgComponentPaginator =
                            await channel.GetMessageAsync(
                                PublicProperties.UsedCommandsResponseMessageId[context.Message.Id]);
                        if (existingMsgComponentPaginator.Attachments != null && existingMsgComponentPaginator.Attachments.Any())
                        {
                            await channel.ModifyMessageAsync(
                                PublicProperties.UsedCommandsResponseMessageId[context.Message.Id],
                                msg => { msg.Attachments = null; });
                        }

                        // Delete old message and send new paginator
                        await channel.DeleteMessageAsync(existingMsgComponentPaginator.Id);
                        await interactiveService.SendPaginatorAsync(
                            response.ComponentPaginator.Build(),
                            channel,
                            TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (response.ReferencedMusic != null)
                {
                    PublicProperties.UsedCommandsReferencedMusic.TryRemove(context.Message.Id, out _);
                    PublicProperties.UsedCommandsReferencedMusic.TryAdd(context.Message.Id, response.ReferencedMusic);

                    if (PublicProperties.UsedCommandsTracks.TryRemove(context.Message.Id, out _))
                    {
                        PublicProperties.UsedCommandsTracks.TryAdd(context.Message.Id, response.ReferencedMusic.Track);
                    }

                    if (PublicProperties.UsedCommandsAlbums.TryRemove(context.Message.Id, out _))
                    {
                        if (response.ReferencedMusic.Album != null)
                        {
                            PublicProperties.UsedCommandsAlbums.TryAdd(context.Message.Id, response.ReferencedMusic.Album);
                        }
                    }

                    if (PublicProperties.UsedCommandsArtists.TryRemove(context.Message.Id, out _))
                    {
                        PublicProperties.UsedCommandsArtists.TryAdd(context.Message.Id, response.ReferencedMusic.Artist);
                    }
                }

                return null;
            }

            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    var text = await channel.SendMessageAsync(new MessageProperties()
                        .WithContent(response.Text)
                        .WithAllowedMentions(AllowedMentionsProperties.None)
                        .WithComponents(response.GetMessageComponents()));
                    responseMessage = text;
                    break;
                case ResponseType.Embed:
                    var embed = await channel.SendMessageAsync(new MessageProperties()
                        .AddEmbeds(response.Embed)
                        .WithComponents(response.GetMessageComponents()));
                    responseMessage = embed;
                    break;
                case ResponseType.Paginator:
                    var staticPaginator = await interactiveService.SendPaginatorAsync(
                        response.StaticPaginator.Build(),
                        channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    responseMessage = staticPaginator.Message;
                    break;
                case ResponseType.ImageWithEmbed:
                    response.FileName = StringExtensions.ReplaceInvalidChars(response.FileName);
                    var imageWithEmbed = await channel.SendMessageAsync(new MessageProperties()
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
                    var image = await channel.SendMessageAsync(new MessageProperties()
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
                        var componentImage = await channel.SendMessageAsync(new MessageProperties()
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
                        var components = await channel.SendMessageAsync(new MessageProperties()
                            .WithComponents(response.GetComponentsV2())
                            .WithFlags(MessageFlags.IsComponentsV2)
                            .WithAllowedMentions(AllowedMentionsProperties.None));
                        responseMessage = components;
                    }

                    break;
                case ResponseType.ComponentPaginator:
                    var componentPaginator = await interactiveService.SendPaginatorAsync(
                        response.ComponentPaginator.Build(),
                        channel,
                        TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                    responseMessage = componentPaginator.Message;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (responseMessage != null)
            {
                PublicProperties.UsedCommandsResponseMessageId.TryAdd(context.Message.Id, responseMessage.Id);
                PublicProperties.UsedCommandsResponseContextId.TryAdd(responseMessage.Id, context.Message.Id);
            }

            if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Message.Id))
            {
                PublicProperties.UsedCommandsHintShown.Add(context.Message.Id);
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
                    await context.HandleCommandException(e, "Could not add emote reactions", sendReply: false);
                    var errorMsg = await channel.SendMessageAsync(new MessageProperties()
                        .WithContent("Could not add automatic emoji reactions.\n" +
                                     "-# Make sure the emojis still exist, the bot is the same server as where the emojis come from and the bot has permission to `Add Reactions`."));
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        try { await channel.DeleteMessageAsync(errorMsg.Id); } catch { /* ignore */ }
                    });
                }
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

        if (PublicProperties.UsedCommandsReferencedMusic.TryGetValue(lookupId, out var value))
        {
            return value;
        }

        if (PublicProperties.UsedCommandsArtists.ContainsKey(lookupId) ||
            PublicProperties.UsedCommandsAlbums.ContainsKey(lookupId) ||
            PublicProperties.UsedCommandsTracks.ContainsKey(lookupId))
        {
            PublicProperties.UsedCommandsArtists.TryGetValue(lookupId, out var artist);
            PublicProperties.UsedCommandsAlbums.TryGetValue(lookupId, out var album);
            PublicProperties.UsedCommandsTracks.TryGetValue(lookupId, out var track);

            return new ReferencedMusic
            {
                Artist = artist,
                Album = album,
                Track = track
            };
        }

        return null;
    }
}
