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
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.Handlers
{
    public class CommandHandler
    {
        private readonly CommandService _commands;
        private readonly UserService _userService;
        private readonly DiscordShardedClient _discord;
        private readonly MusicBotService _musicBotService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly IGuildDisabledCommandService _guildDisabledCommandService;
        private readonly IChannelDisabledCommandService _channelDisabledCommandService;
        private readonly IServiceProvider _provider;
        private readonly BotSettings _botSettings;

        // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
        public CommandHandler(
            DiscordShardedClient discord,
            CommandService commands,
            IServiceProvider provider,
            IPrefixService prefixService,
            IGuildDisabledCommandService guildDisabledCommandService,
            IChannelDisabledCommandService channelDisabledCommandService,
            UserService userService,
            MusicBotService musicBotService,
            IOptions<BotSettings> botSettings,
            GuildService guildService)
        {
            this._discord = discord;
            this._commands = commands;
            this._provider = provider;
            this._prefixService = prefixService;
            this._guildDisabledCommandService = guildDisabledCommandService;
            this._channelDisabledCommandService = channelDisabledCommandService;
            this._userService = userService;
            this._musicBotService = musicBotService;
            this._guildService = guildService;
            this._botSettings = botSettings.Value;
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
                return;
            }

            // Mention
            if (this._discord != null && msg.HasMentionPrefix(this._discord.CurrentUser, ref argPos))
            {
                await ExecuteCommand(msg, context, argPos, prfx);
                return;
            }

            // Mention
            if (prfx == this._botSettings.Bot.Prefix && msg.HasStringPrefix(".", ref argPos))
            {
                var searchResult = this._commands.Search(context, argPos);
                if (searchResult.IsSuccess && searchResult.Commands != null && searchResult.Commands.Any() && searchResult.Commands.FirstOrDefault().Command.Name == "fm")
                {
                    await ExecuteCommand(msg, context, argPos, prfx);
                }
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
            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && msg.Content.StartsWith(this._botSettings.Bot.Prefix))
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
                    embed.UsernameNotSetErrorResponse(prfx ?? this._botSettings.Bot.Prefix, userNickname ?? context.User.Username);
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
                    embed.SessionRequiredResponse(prfx ?? this._botSettings.Bot.Prefix);
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
            if (searchResult.Commands[0].Command.Attributes.OfType<RequiresIndex>().Any() && context.Guild != null)
            {
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
                if (lastIndex == null)
                {
                    var embed = new EmbedBuilder();
                    embed.WithDescription("To use .fmbot commands with server-wide statistics you need to index the server first.\n\n" +
                                          $"Please run `{prfx}index` to index this server.\n" +
                                          $"Note that this can take some time on large servers.");
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }
                if (lastIndex < DateTime.UtcNow.AddDays(-100))
                {
                    var embed = new EmbedBuilder();
                    embed.WithDescription("Server index data is out of date, it was last updated over 100 days ago.\n" +
                                          $"Please run `{prfx}index` to re-index this server.");
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.IndexRequired);
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

        private async Task UserBlockedResponse(ShardedCommandContext shardedCommandContext, string s)
        {
            var embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            embed.UserBlockedResponse(s ?? this._botSettings.Bot.Prefix);
            await shardedCommandContext.Channel.SendMessageAsync("", false, embed.Build());
            shardedCommandContext.LogCommandUsed(CommandResponse.UserBlocked);
            return;
        }
    }
}
