using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Handlers
{
    public class CommandHandler
    {
        private static readonly List<DateTimeOffset> StackCooldownTimer = new List<DateTimeOffset>();
        private static readonly List<SocketUser> StackCooldownTarget = new List<SocketUser>();
        private readonly CommandService _commands;
        private readonly UserService _userService;
        private readonly DiscordShardedClient _discord;
        private readonly IPrefixService _prefixService;
        private readonly IGuildDisabledCommandService _guildDisabledCommandService;
        private readonly IChannelDisabledCommandService _channelDisabledCommandService;
        private readonly IServiceProvider _provider;

        // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
        public CommandHandler(
            DiscordShardedClient discord,
            CommandService commands,
            IServiceProvider provider,
            IPrefixService prefixService,
            IGuildDisabledCommandService guildDisabledCommandService,
            IChannelDisabledCommandService channelDisabledCommandService,
            UserService userService)
        {
            this._discord = discord;
            this._commands = commands;
            this._provider = provider;
            this._prefixService = prefixService;
            this._guildDisabledCommandService = guildDisabledCommandService;
            this._channelDisabledCommandService = channelDisabledCommandService;
            this._userService = userService;
            this._discord.MessageReceived += OnMessageReceivedAsync;
        }

        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            var msg = s as SocketUserMessage; // Ensure the message is from a user/bot
            if (msg == null)
            {
                return;
            }

            if (this._discord?.CurrentUser != null && (msg.Author.Id == this._discord.CurrentUser.Id))
            {
                return; // Ignore self when checking commands
            }

            if (msg.Author.IsBot)
            {
                return; // Ignore bots
            }

            var context = new ShardedCommandContext(this._discord, msg); // Create the command context

            var argPos = 0; // Check if the message has a valid command prefix
            var customPrefix = this._prefixService.GetPrefix(context.Guild?.Id);
            if (msg.HasStringPrefix(ConfigData.Data.Bot.Prefix, ref argPos, StringComparison.CurrentCultureIgnoreCase) && customPrefix == null || msg.HasMentionPrefix(this._discord.CurrentUser, ref argPos))
            {
                await ExecuteCommand(msg, context, argPos);
            }
            else if (!string.IsNullOrWhiteSpace(customPrefix) && msg.HasStringPrefix(customPrefix, ref argPos, StringComparison.CurrentCultureIgnoreCase))
            {
                await ExecuteCommand(msg, context, argPos, customPrefix);
            }
            else if (string.IsNullOrWhiteSpace(customPrefix) && msg.HasStringPrefix(".", ref argPos))
            {
                var searchResult = this._commands.Search(context, argPos);
                if (searchResult.IsSuccess && searchResult.Commands.FirstOrDefault().Command.Name == "fm")
                {
                    await ExecuteCommand(msg, context, argPos);
                }
            }
        }

        private async Task ExecuteCommand(SocketUserMessage msg, ShardedCommandContext context, int argPos, string customPrefix = null)
        {
            if (StackCooldownTarget.Contains(msg.Author))
            {
                //If they have used this command before, take the time the user last did something, add 800ms, and see if it's greater than this very moment.
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddMilliseconds(800) >=
                    DateTimeOffset.Now)
                {
                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
            }
            else
            {
                //If they've never used this command before, add their username and when they just used this command.
                StackCooldownTarget.Add(msg.Author);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }

            var searchResult = this._commands.Search(context, argPos);

            // If custom prefix is enabled, no commands found and message does not start with custom prefix, return
            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && customPrefix != null && !msg.Content.StartsWith(customPrefix))
            {
                return;
            }

            if (context.Guild != null)
            {
                var disabledGuildCommands = this._guildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
                if (searchResult.Commands != null &&
                    disabledGuildCommands != null &&
                    disabledGuildCommands.Any(searchResult.Commands.First().Command.Name.Contains))
                {
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this server.");
                    return;
                }

                var disabledChannelCommands = this._channelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
                if (searchResult.Commands != null &&
                    disabledChannelCommands != null &&
                    disabledChannelCommands.Any() &&
                    disabledChannelCommands.Any(searchResult.Commands.First().Command.Name.Contains))
                {
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this channel.");
                    return;
                }
            }

            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && msg.Content.StartsWith(ConfigData.Data.Bot.Prefix))
            {
                var commandPrefixResult = await this._commands.ExecuteAsync(context, 1, this._provider);

                if (commandPrefixResult.IsSuccess)
                {
                    Statistics.CommandsExecuted.Inc();
                }
                else
                {
                    Log.Error(commandPrefixResult.ToString(), context.Message.Content);
                }

                return;
            }

            if (searchResult.Commands[0].Command.Attributes.OfType<UsernameSetRequired>().Any())
            {
                var userRegistered = await this._userService.UserRegisteredAsync(context.User);
                if (!userRegistered)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.UsernameNotSetErrorResponse(customPrefix ?? ConfigData.Data.Bot.Prefix);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }

                var userBlocked = await this._userService.UserBlockedAsync(context.User);
                if (userBlocked)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.UserBlockedResponse(customPrefix ?? ConfigData.Data.Bot.Prefix);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UserBlocked);
                    return;
                }

            }
            if (searchResult.Commands[0].Command.Attributes.OfType<UserSessionRequired>().Any())
            {
                var userSession = await this._userService.UserHasSessionAsync(context.User);
                if (!userSession)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.SessionRequiredResponse(customPrefix ?? ConfigData.Data.Bot.Prefix);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }

                var userBlocked = await this._userService.UserBlockedAsync(context.User);
                if (userBlocked)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.UserBlockedResponse(customPrefix ?? ConfigData.Data.Bot.Prefix);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UserBlocked);
                    return;
                }
            }
            if (searchResult.Commands[0].Command.Attributes.OfType<GuildOnly>().Any())
            {
                if (context.Guild == null)
                {
                    await context.User.SendMessageAsync("This command is not supported in DMs.");
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }
            }

            var result = await this._commands.ExecuteAsync(context, argPos, this._provider);

            if (result.IsSuccess)
            {
                Statistics.CommandsExecuted.Inc();
                await this._userService.UpdateUserLastUsedAsync(context.User);
            }
            else
            {
                Log.Error(result.ToString(), context.Message.Content);
            }
        }

    }
}
