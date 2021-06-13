using System;
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
        private readonly CommandService _commands;
        private readonly UserService _userService;
        private readonly DiscordShardedClient _discord;
        private readonly MusicBotService _musicBotService;
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
            UserService userService,
            MusicBotService musicBotService)
        {
            this._discord = discord;
            this._commands = commands;
            this._provider = provider;
            this._prefixService = prefixService;
            this._guildDisabledCommandService = guildDisabledCommandService;
            this._channelDisabledCommandService = channelDisabledCommandService;
            this._userService = userService;
            this._musicBotService = musicBotService;
            this._discord.MessageReceived += OnMessageReceivedAsync;
        }

        private async Task OnMessageReceivedAsync(SocketMessage s)
        {
            var msg = s as SocketUserMessage; // Ensure the message is from a user/bot
            if (msg == null)
            {
                return;
            }

            if (this._discord?.CurrentUser != null && msg.Author.Id == this._discord.CurrentUser.Id)
            {
                return; // Ignore self when checking commands
            }

            // Create the command context
            var context = new ShardedCommandContext(this._discord, msg);

            if (msg.Author.IsBot)
            {
                if (msg.Author.Username.StartsWith("Groovy"))
                {
                    await this._musicBotService.ScrobbleGroovy(msg, context);
                }
                return; // Ignore other bots
            }

            var argPos = 0; // Check if the message has a valid command prefix
            var prfx = this._prefixService.GetPrefix(context.Guild?.Id);

            // Custom prefix
            if (msg.HasStringPrefix(prfx, ref argPos, StringComparison.CurrentCultureIgnoreCase))
            {
                await ExecuteCommand(msg, context, argPos, prfx);
            }

            // Mention
            if (this._discord != null && msg.HasMentionPrefix(this._discord.CurrentUser, ref argPos))
            {
                await ExecuteCommand(msg, context, argPos, prfx);
            }
        }

        private async Task ExecuteCommand(SocketUserMessage msg, ShardedCommandContext context, int argPos, string prfx)
        {
            var searchResult = this._commands.Search(context, argPos);

            // If no commands found and message does not start with prefix
            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && !msg.Content.StartsWith(prfx))
            {
                return;
            }

            if (context.Guild != null)
            {
                var disabledGuildCommands = this._guildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
                if (searchResult.Commands != null &&
                    disabledGuildCommands != null &&
                    disabledGuildCommands.Any(searchResult.Commands[0].Command.Name.Contains))
                {
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this server.");
                    return;
                }

                var disabledChannelCommands = this._channelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
                if (searchResult.Commands != null &&
                    disabledChannelCommands != null &&
                    disabledChannelCommands.Any() &&
                    disabledChannelCommands.Any(searchResult.Commands[0].Command.Name.Contains) &&
                    context.Channel != null)
                {
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this channel.");
                    return;
                }
            }

            var userBlocked = await this._userService.UserBlockedAsync(context.User.Id);

            // If command possibly equals .fm
            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && msg.Content.StartsWith(ConfigData.Data.Bot.Prefix))
            {
                var fmSearchResult = this._commands.Search(context, 1);

                if (fmSearchResult.Commands == null || fmSearchResult.Commands.Count == 0)
                {
                    return;
                }

                if (userBlocked)
                {
                    await UserBlockedResponse(context, prfx);
                    return;
                }

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

            if (searchResult.Commands == null || !searchResult.Commands.Any())
            {
                return;
            }

            if (userBlocked)
            {
                await UserBlockedResponse(context, prfx);
                return;
            }

            if (searchResult.Commands[0].Command.Attributes.OfType<UsernameSetRequired>().Any())
            {
                var userRegistered = await this._userService.UserRegisteredAsync(context.User);
                if (!userRegistered)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    var userNickname = (context.User as SocketGuildUser)?.Nickname;
                    embed.UsernameNotSetErrorResponse(prfx ?? ConfigData.Data.Bot.Prefix, userNickname ?? context.User.Username);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
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
                    embed.SessionRequiredResponse(prfx ?? ConfigData.Data.Bot.Prefix);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }
            }
            if (searchResult.Commands[0].Command.Attributes.OfType<GuildOnly>().Any())
            {
                if (context.Guild == null)
                {
                    await context.User.SendMessageAsync("This command is not supported in DMs.");
                    context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                    return;
                }
            }

            var commandName = searchResult.Commands[0].Command.Name;
            if (msg.Content.ToLower().EndsWith(" help") && commandName != "help")
            {
                var embed = new EmbedBuilder();
                var userName = (context.Message.Author as SocketGuildUser)?.Nickname ?? context.User.Username;

                embed.HelpResponse(searchResult.Commands[0].Command, prfx, userName);
                await context.Channel.SendMessageAsync("", false, embed.Build());
                context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var result = await this._commands.ExecuteAsync(context, argPos, this._provider);

            if (result.IsSuccess)
            {
                Statistics.CommandsExecuted.Inc();
                _ = this._userService.UpdateUserLastUsedAsync(context.User.Id);
            }
            else
            {
                Log.Error(result.ToString(), context.Message.Content);
            }
        }

        private static async Task UserBlockedResponse(ShardedCommandContext shardedCommandContext, string s)
        {
            var embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            embed.UserBlockedResponse(s ?? ConfigData.Data.Bot.Prefix);
            await shardedCommandContext.Channel.SendMessageAsync("", false, embed.Build());
            shardedCommandContext.LogCommandUsed(CommandResponse.UserBlocked);
            return;
        }
    }
}
