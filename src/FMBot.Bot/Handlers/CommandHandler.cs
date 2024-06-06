using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Models.MusicBot;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;

namespace FMBot.Bot.Handlers;

public class CommandHandler
{
    private readonly CommandService _commands;
    private readonly UserService _userService;
    private readonly DiscordShardedClient _discord;
    private readonly MusicBotService _musicBotService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly InteractiveService _interactiveService;
    private readonly IServiceProvider _provider;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly IIndexService _indexService;
    private readonly GameBuilders _gameBuilders;

    // DiscordSocketClient, CommandService, IConfigurationRoot, and IServiceProvider are injected automatically from the IServiceProvider
    public CommandHandler(
        DiscordShardedClient discord,
        CommandService commands,
        IServiceProvider provider,
        IPrefixService prefixService,
        UserService userService,
        MusicBotService musicBotService,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        InteractiveService interactiveService,
        IMemoryCache cache,
        IIndexService indexService,
        GameBuilders gameBuilders)
    {
        this._discord = discord;
        this._commands = commands;
        this._provider = provider;
        this._prefixService = prefixService;
        this._userService = userService;
        this._musicBotService = musicBotService;
        this._guildService = guildService;
        this._interactiveService = interactiveService;
        this._cache = cache;
        this._indexService = indexService;
        this._gameBuilders = gameBuilders;
        this._botSettings = botSettings.Value;
        this._discord.MessageReceived += MessageReceived;
        this._discord.MessageUpdated += MessageUpdated;
    }

    private async Task MessageReceived(SocketMessage s)
    {
        Statistics.DiscordEvents.WithLabels(nameof(MessageReceived)).Inc();

        if (s is not SocketUserMessage msg)
        {
            return;
        }

        if (this._discord?.CurrentUser != null && msg.Author?.Id == this._discord.CurrentUser?.Id)
        {
            return;
        }

        var context = new ShardedCommandContext(this._discord, msg);

        if (msg.Author != null && msg.Author.IsBot && msg.Flags != MessageFlags.Loading)
        {
            if (string.IsNullOrWhiteSpace(msg.Author.Username))
            {
                return;
            }

            _ = Task.Run(() => TryScrobbling(msg, context));
            return;
        }

        if (context.Guild != null &&
            PublicProperties.PremiumServers.ContainsKey(context.Guild.Id) &&
            PublicProperties.RegisteredUsers.ContainsKey(context.User.Id))
        {
            _ = Task.Run(() => UpdateUserLastMessageDate(context));
        }

        var possibleCommandExecuted = TryForCommand(context, msg, false);

        if (!possibleCommandExecuted &&
            context.Channel?.Id != null &&
            this._cache.TryGetValue(GameService.CacheKeyForJumbleSession(context.Channel.Id), out _) &&
            !string.IsNullOrWhiteSpace(context.Message.Content))
        {
            _ = Task.Run(() => this._gameBuilders.JumbleProcessAnswer(new ContextModel(context, "."), context));
        }
    }

    private async Task TryScrobbling(SocketUserMessage msg, ICommandContext context)
    {
        foreach (var musicBot in MusicBot.SupportedBots)
        {
            if (musicBot.IsAuthor(msg.Author))
            {
                await this._musicBotService.Scrobble(musicBot, msg, context);
                break;
            }
        }
    }

    private async Task MessageUpdated(Cacheable<IMessage, ulong> originalMessage, SocketMessage updatedMessage, ISocketMessageChannel sourceChannel)
    {
        Statistics.DiscordEvents.WithLabels(nameof(MessageUpdated)).Inc();

        var msg = updatedMessage as SocketUserMessage;

        if (msg == null || msg.Author == null)
        {
            return;
        }

        var context = new ShardedCommandContext(this._discord, msg);

        if (msg.Author.IsBot && msg.Flags != MessageFlags.Loading && msg.Interaction != null)
        {
            _ = Task.Run(() => TryScrobbling(msg, context));
        }

        TryForCommand(context, msg, true);
    }

    private bool TryForCommand(ShardedCommandContext shardedCommandContext, SocketUserMessage socketUserMessage, bool update)
    {
        var argPos = 0;
        var prfx = this._prefixService.GetPrefix(shardedCommandContext.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        // New prefix '.' but user still uses the '.fm' prefix anyway
        if (prfx == this._botSettings.Bot.Prefix &&
            socketUserMessage.HasStringPrefix(prfx + "fm", ref argPos, StringComparison.OrdinalIgnoreCase) &&
            socketUserMessage.Content.Length > $"{prfx}fm".Length)
        {
            _ = Task.Run(() => ExecuteCommand(socketUserMessage, shardedCommandContext, argPos, prfx, update));
            return true;
        }

        // Prefix is set to '.fm' and the user uses '.fm'
        const string fm = ".fm";
        if (prfx == fm && socketUserMessage.HasStringPrefix(".", ref argPos, StringComparison.OrdinalIgnoreCase))
        {
            var searchResult = this._commands.Search(shardedCommandContext, argPos);
            if (searchResult.IsSuccess &&
                searchResult.Commands != null &&
                searchResult.Commands.Any() &&
                searchResult.Commands.FirstOrDefault().Command.Name == "fm")
            {
                _ = Task.Run(() => ExecuteCommand(socketUserMessage, shardedCommandContext, argPos, prfx, update));
                return true;
            }
        }

        // Normal or custom prefix
        if (socketUserMessage.HasStringPrefix(prfx, ref argPos, StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(() => ExecuteCommand(socketUserMessage, shardedCommandContext, argPos, prfx, update));
            return true;
        }

        // Mention
        if (this._discord != null && socketUserMessage.HasMentionPrefix(this._discord.CurrentUser, ref argPos))
        {
            _ = Task.Run(() => ExecuteCommand(socketUserMessage, shardedCommandContext, argPos, prfx, update));
            return true;
        }

        return false;
    }

    private async Task ExecuteCommand(SocketUserMessage msg, ShardedCommandContext context, int argPos, string prfx, bool update = false)
    {
        var searchResult = this._commands.Search(context, argPos);

        // If no commands found and message does not start with prefix
        if ((searchResult.Commands == null || searchResult.Commands.Count == 0) && !msg.Content.StartsWith(prfx, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (update)
        {
            var exists = await this._userService.InteractionExists(context.Message.Id);
            if (!exists)
            {
                return;
            }
        }

        using (Statistics.TextCommandHandlerDuration.NewTimer())
        {
            if (!await CommandEnabled(context, searchResult, update))
            {
                return;
            }

            // If command possibly equals .fm
            if ((searchResult.Commands == null || searchResult.Commands.Count == 0) &&
                msg.Content.StartsWith(this._botSettings.Bot.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var fmSearchResult = this._commands.Search(context, 1);

                if (fmSearchResult.Commands == null || fmSearchResult.Commands.Count == 0)
                {
                    return;
                }

                if (await this._userService.UserBlockedAsync(context.User.Id))
                {
                    await UserBlockedResponse(context, prfx);
                    return;
                }

                if (!await CommandEnabled(context, fmSearchResult, update))
                {
                    return;
                }

                var commandPrefixResult = await this._commands.ExecuteAsync(context, 1, this._provider);

                if (commandPrefixResult.IsSuccess)
                {
                    Statistics.CommandsExecuted.WithLabels("fm").Inc();

                    _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));
                    _ = Task.Run(() => this._userService.AddUserTextCommandInteraction(context, "fm"));
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

            if (await this._userService.UserBlockedAsync(context.User.Id))
            {
                await UserBlockedResponse(context, prfx);
                return;
            }

            if (searchResult.Commands[0].Command.Attributes.OfType<UsernameSetRequired>().Any())
            {
                var userIsRegistered = await this._userService.UserRegisteredAsync(context.User);
                if (!userIsRegistered)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    var userNickname = (context.User as SocketGuildUser)?.DisplayName;

                    embed.UsernameNotSetErrorResponse(prfx ?? this._botSettings.Bot.Prefix,
                        userNickname ?? context.User.GlobalName ?? context.User.Username);
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }

                var rateLimit = CheckUserRateLimit(context.User.Id);
                if (!rateLimit)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(DiscordConstants.WarningColorOrange);
                    embed.RateLimitedResponse();
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.RateLimited);
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
                    embed.WithDescription(
                        "To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                        $"Please run `{prfx}refreshmembers` to create this.\n" +
                        $"Note that this can take some time on large servers.");
                    await context.Channel.SendMessageAsync("", false, embed.Build());
                    context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }

                if (lastIndex < DateTime.UtcNow.AddDays(-180))
                {
                    _ = Task.Run(() => this.UpdateGuildIndex(context));
                }
            }

            var commandName = searchResult.Commands[0].Command.Name;
            if (msg.Content.EndsWith(" help", StringComparison.OrdinalIgnoreCase) && commandName != "help")
            {
                var embed = new EmbedBuilder();
                var userName = (context.Message.Author as SocketGuildUser)?.DisplayName ??
                               context.User.GlobalName ?? context.User.Username;

                embed.HelpResponse(searchResult.Commands[0].Command, prfx, userName);
                await context.Channel.SendMessageAsync("", false, embed.Build());
                context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var result = await this._commands.ExecuteAsync(context, argPos, this._provider);

            if (result.IsSuccess)
            {
                Statistics.CommandsExecuted.WithLabels(commandName).Inc();

                _ = Task.Run(() => this._userService.UpdateUserLastUsedAsync(context.User.Id));

                if (!update)
                {
                    _ = Task.Run(() => this._userService.AddUserTextCommandInteraction(context, commandName));
                }
                else
                {
                    _ = Task.Run(() => this._userService.UpdateInteractionContextThroughReference(context.Message.Id, true, false));
                }
            }
            else
            {
                Log.Error(result?.ToString() ?? "Command error (null)", context.Message.Content);
            }
        }
    }

    private bool CheckUserRateLimit(ulong discordUserId)
    {
        var cacheKey = $"{discordUserId}-ratelimit";
        if (this._cache.TryGetValue(cacheKey, out int requestsInLastMinute))
        {
            if (requestsInLastMinute > 35)
            {
                return false;
            }

            requestsInLastMinute++;
            this._cache.Set(cacheKey, requestsInLastMinute, TimeSpan.FromSeconds(60 - requestsInLastMinute));
        }
        else
        {
            this._cache.Set(cacheKey, 1, TimeSpan.FromMinutes(1));
        }

        return true;
    }

    private async Task<bool> CommandEnabled(SocketCommandContext context, SearchResult searchResult, bool update = false)
    {
        if (context.Guild != null)
        {
            if (searchResult.Commands != null &&
                searchResult.Commands.Any(a => a.Command.Name.Equals("togglecommand", StringComparison.OrdinalIgnoreCase) ||
                                               a.Command.Name.Equals("toggleservercommand", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var channelDisabled = DisabledChannelService.GetDisabledChannel(context.Channel.Id);
            if (channelDisabled)
            {
                if (searchResult.Commands == null || !searchResult.Commands.Any())
                {
                    return false;
                }

                if (!update)
                {
                    _ = this._interactiveService.DelayedDeleteMessageAsync(
                        await context.Channel.SendMessageAsync("The bot has been disabled in this channel."),
                        TimeSpan.FromSeconds(8));
                }
                return false;
            }

            var disabledGuildCommands = GuildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
            if (searchResult.Commands != null &&
                disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Commands[0].Command.Name.Equals))
            {
                if (!update)
                {
                    _ = this._interactiveService.DelayedDeleteMessageAsync(
                        await context.Channel.SendMessageAsync(
                            "The command you're trying to execute has been disabled in this server."),
                        TimeSpan.FromSeconds(8));
                }

                return false;
            }

            var disabledChannelCommands = ChannelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
            if (searchResult.Commands != null &&
                disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Commands[0].Command.Name.Equals) &&
                context.Channel != null)
            {
                if (!update)
                {
                    _ = this._interactiveService.DelayedDeleteMessageAsync(
                        await context.Channel.SendMessageAsync(
                            "The command you're trying to execute has been disabled in this channel."),
                        TimeSpan.FromSeconds(8));
                }

                return false;
            }
        }

        return true;
    }

    private async Task UserBlockedResponse(SocketCommandContext context, string s)
    {
        var embed = new EmbedBuilder()
            .WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse(s ?? this._botSettings.Bot.Prefix);
        await context.Channel.SendMessageAsync("", false, embed.Build());
        context.LogCommandUsed(CommandResponse.UserBlocked);
        return;
    }

    private async Task UpdateUserLastMessageDate(SocketCommandContext context)
    {
        var cacheKey = $"{context.User.Id}-{context.Guild.Id}-lst-msg-updated";
        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        this._cache.Set(cacheKey, 1, TimeSpan.FromMinutes(30));

        var guildSuccess = PublicProperties.PremiumServers.TryGetValue(context.Guild.Id, out var guildId);
        var userSuccess = PublicProperties.RegisteredUsers.TryGetValue(context.User.Id, out var userId);

        var guildUser = await ((IGuild)context.Guild).GetUserAsync(context.User.Id, CacheMode.CacheOnly);

        if (!guildSuccess || !userSuccess || guildUser == null)
        {
            return;
        }

        await this._guildService.UpdateGuildUserLastMessageDate(guildUser, userId, guildId);
    }

    private async Task UpdateGuildIndex(ICommandContext context)
    {
        var guildUsers = await context.Guild.GetUsersAsync();

        Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord", guildUsers.Count, context.Guild.Id, context.Guild.Name);

        await this._indexService.StoreGuildUsers(context.Guild, guildUsers);

        await this._guildService.UpdateGuildIndexTimestampAsync(context.Guild, DateTime.UtcNow);
    }
}
