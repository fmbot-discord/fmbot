using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Fergun.Interactive;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class CommandContextExtensions
{
    public static void LogCommandUsed(this ICommandContext context, CommandResponse commandResponse = CommandResponse.Ok)
    {
        Log.Information("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent}",
            context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, commandResponse, context.Message.Content);

        PublicProperties.UsedCommandsResponses.TryAdd(context.Message.Id, commandResponse);
    }

    public static async Task HandleCommandException(this ICommandContext context, Exception exception, string message = null, bool sendReply = true)
    {
        var referenceId = GenerateRandomCode();

        Log.Error(exception, "CommandUsed: Error {referenceId} | {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} ({message}) | {messageContent}",
            referenceId, context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.Error, message, context.Message.Content);

        if (sendReply)
        {
            if (exception?.Message != null && exception.Message.Contains("error 50013"))
            {
                await context.Channel.SendMessageAsync("Sorry, something went wrong because the bot is missing permissions. Make sure the bot has `Embed links` and `Attach Files`.\n" +
                                                       "Please adjust .fmbot permissions or ask server staff to do this for you.\n" +
                                                       $"*Reference id: `{referenceId}`*", allowedMentions: AllowedMentions.None);
            }
            else
            {
                await context.Channel.SendMessageAsync("Sorry, something went wrong. Please try again later.\n" +
                                                       $"*Reference id: `{referenceId}`*", allowedMentions: AllowedMentions.None);
            }
        }

        PublicProperties.UsedCommandsErrorReferences.TryAdd(context.Message.Id, referenceId);
    }

    public static void LogCommandWithLastFmError(this ICommandContext context, ResponseStatus? responseStatus)
    {
        Log.Error("CommandUsed: {discordUserName} / {discordUserId} | {guildName} / {guildId} | {commandResponse} | {messageContent} | Last.fm error: {responseStatus}",
            context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, CommandResponse.LastFmError, context.Message.Content, responseStatus);

        PublicProperties.UsedCommandsResponses.TryAdd(context.Message.Id, CommandResponse.LastFmError);
    }

    public static async Task<ulong?> SendResponse(this ICommandContext context, InteractiveService interactiveService, ResponseModel response)
    {
        ulong? responseId = null;

        if (PublicProperties.UsedCommandsResponseMessageId.ContainsKey(context.Message.Id))
        {
            switch (response.ResponseType)
            {
                case ResponseType.Text:
                    await context.Channel.ModifyMessageAsync(PublicProperties.UsedCommandsResponseMessageId[context.Message.Id], msg =>
                    {
                        msg.Content = response.Text;
                        msg.Embed = null;
                        msg.Components = response.Components?.Build();
                    });
                    break;
                case ResponseType.Embed:
                case ResponseType.ImageWithEmbed:
                case ResponseType.ImageOnly:
                    await context.Channel.ModifyMessageAsync(PublicProperties.UsedCommandsResponseMessageId[context.Message.Id], msg =>
                    {
                        msg.Content = response.Text;
                        msg.Embed = response.ResponseType == ResponseType.ImageOnly ? null : response.Embed?.Build();
                        msg.Components = response.Components?.Build();
                        msg.Attachments = response.Stream != null ? new Optional<IEnumerable<FileAttachment>>(new List<FileAttachment>
                        {
                            new(response.Stream, response.Spoiler ? $"SPOILER_{response.FileName}.png" : $"{response.FileName}.png")
                        }) : null;
                    });

                    if (response.Stream != null)
                    {
                        await response.Stream.DisposeAsync();
                    }

                    break;
                case ResponseType.Paginator:
                    var existingMessage = await context.Channel.GetMessageAsync(PublicProperties.UsedCommandsResponseMessageId[context.Message.Id]);
                    await interactiveService.SendPaginatorAsync(
                                response.StaticPaginator,
                                (IUserMessage)existingMessage,
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
                var text = await context.Channel.SendMessageAsync(response.Text, allowedMentions: AllowedMentions.None, components: response.Components?.Build());
                responseId = text.Id;
                break;
            case ResponseType.Embed:
                var embed = await context.Channel.SendMessageAsync("", false, response.Embed.Build(), components: response.Components?.Build());
                responseId = embed.Id;
                break;
            case ResponseType.Paginator:
                var paginator = await interactiveService.SendPaginatorAsync(
                    response.StaticPaginator,
                    context.Channel,
                    TimeSpan.FromMinutes(DiscordConstants.PaginationTimeoutInSeconds));
                responseId = paginator.Message.Id;
                break;
            case ResponseType.ImageWithEmbed:
                var imageEmbedFilename = StringExtensions.TruncateLongString(StringExtensions.ReplaceInvalidChars(response.FileName), 60);
                var imageWithEmbed = await context.Channel.SendFileAsync(
                    response.Stream,
                    imageEmbedFilename + ".png",
                    null,
                    false,
                    response.Embed.Build(),
                    isSpoiler: response.Spoiler,
                    components: response.Components?.Build());

                await response.Stream.DisposeAsync();
                responseId = imageWithEmbed.Id;
                break;
            case ResponseType.ImageOnly:
                var imageFilename = StringExtensions.TruncateLongString(StringExtensions.ReplaceInvalidChars(response.FileName), 60);
                var image = await context.Channel.SendFileAsync(
                    response.Stream,
                    imageFilename + ".png",
                    null,
                    false,
                    isSpoiler: response.Spoiler,
                    components: response.Components?.Build());

                await response.Stream.DisposeAsync();
                responseId = image.Id;
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (responseId.HasValue)
        {
            PublicProperties.UsedCommandsResponseMessageId.TryAdd(context.Message.Id, responseId.Value);
            PublicProperties.UsedCommandsResponseContextId.TryAdd(responseId.Value, context.Message.Id);
        }

        if (response.HintShown == true && !PublicProperties.UsedCommandsHintShown.Contains(context.Message.Id))
        {
            PublicProperties.UsedCommandsHintShown.Add(context.Message.Id);
        }

        return responseId;
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
