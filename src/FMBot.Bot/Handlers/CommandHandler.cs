using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models.MusicBot;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
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
        IMemoryCache cache)
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
        this._botSettings = botSettings.Value;
        this._discord.MessageReceived += OnMessageReceivedAsync;
        this._discord.MessageUpdated += OnMessageUpdatedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage s)
    {
        var msg = s as SocketUserMessage; // Ensure the message is from a user/bot
        if (msg == null)
        {
            return;
        }

        if (this._discord?.CurrentUser != null && msg.Author?.Id == this._discord.CurrentUser?.Id)
        {
            return; // Ignore self when checking commands
        }

        // Create the command context
        var context = new ShardedCommandContext(this._discord, msg);

        if (msg.Author != null && msg.Author.IsBot && msg.Flags != MessageFlags.Loading)
        {
            if (string.IsNullOrWhiteSpace(msg.Author.Username))
            {
                return;
            }
            TryScrobbling(msg, context);
            return;
        }

        if (context.Guild != null &&
            PublicProperties.PremiumServers.ContainsKey(context.Guild.Id) &&
            PublicProperties.RegisteredUsers.ContainsKey(context.User.Id))
        {
            _ = Task.Run(() => UpdateUserLastMessageDate(context));
        }

        var argPos = 0; // Check if the message has a valid command prefix
        var prfx = this._prefixService.GetPrefix(context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        // New prefix '.' but user still uses the '.fm' prefix anyway
        if (prfx == this._botSettings.Bot.Prefix &&
            msg.HasStringPrefix(prfx + "fm", ref argPos, StringComparison.CurrentCultureIgnoreCase) &&
            msg.Content.Length > $"{prfx}fm".Length)
        {
            await ExecuteCommand(msg, context, argPos, prfx);
            return;
        }

        // Prefix is set to '.fm' and the user uses '.fm'
        if (prfx == ".fm" && msg.HasStringPrefix(".", ref argPos))
        {
            var searchResult = this._commands.Search(context, argPos);
            if (searchResult.IsSuccess &&
                searchResult.Commands != null &&
                searchResult.Commands.Any() &&
                searchResult.Commands.FirstOrDefault().Command.Name == "fm")
            {
                await ExecuteCommand(msg, context, argPos, prfx);
                return;
            }
        }

        // Normal or custom prefix
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
    }

    private async void TryScrobbling(SocketUserMessage msg, ICommandContext context)
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

    private async Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> originalMessage, SocketMessage updatedMessage, ISocketMessageChannel sourceChannel)
    {
        var msg = updatedMessage as SocketUserMessage;

        if (msg == null || msg.Author == null || !msg.Author.IsBot || msg.Interaction == null)
        {
            return;
        }

        var context = new ShardedCommandContext(this._discord, msg);


        if (msg.Flags != MessageFlags.Loading)
        {
            TryScrobbling(msg, context);
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

        if (!await CommandEnabled(context, searchResult))
        {
            return;
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

            if (!await CommandEnabled(context, fmSearchResult))
            {
                return;
            }

            var commandPrefixResult = await this._commands.ExecuteAsync(context, 1, this._provider);

            if (commandPrefixResult.IsSuccess)
            {
                Statistics.CommandsExecuted.WithLabels("fm").Inc();
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
                embed.WithDescription("To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                                      $"Please run `{prfx}refreshmembers` to create this.\n" +
                                      $"Note that this can take some time on large servers.");
                await context.Channel.SendMessageAsync("", false, embed.Build());
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-180))
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("Server member cache is out of date, it was last updated over 180 days ago.\n" +
                                      $"Please run `{prfx}refreshmembers` to update the cached memberlist.");
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
            Statistics.CommandsExecuted.WithLabels(commandName).Inc();
            _ = this._userService.UpdateUserLastUsedAsync(context.User.Id);
        }
        else
        {
            Log.Error(result.ToString(), context.Message.Content);
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

    private async Task<bool> CommandEnabled(SocketCommandContext context, SearchResult searchResult)
    {
        if (context.Guild != null)
        {
            if (searchResult.Commands != null &&
                searchResult.Commands.Any(a => a.Command.Name.ToLower() == "togglecommand" ||
                                               a.Command.Name.ToLower() == "toggleservercommand"))
            {
                return true;
            }

            var channelDisabled = DisabledChannelService.GetDisabledChannel(context.Channel.Id);
            if (channelDisabled)
            {
                _ = this._interactiveService.DelayedDeleteMessageAsync(
                    await context.Channel.SendMessageAsync("The bot has been disabled in this channel."),
                    TimeSpan.FromSeconds(8));
                return false;
            }

            var disabledGuildCommands = GuildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
            if (searchResult.Commands != null &&
                disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Commands[0].Command.Name.Equals))
            {
                _ = this._interactiveService.DelayedDeleteMessageAsync(
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this server."),
                    TimeSpan.FromSeconds(8));
                return false;
            }

            var disabledChannelCommands = ChannelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
            if (searchResult.Commands != null &&
                disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Commands[0].Command.Name.Equals) &&
                context.Channel != null)
            {
                _ = this._interactiveService.DelayedDeleteMessageAsync(
                    await context.Channel.SendMessageAsync("The command you're trying to execute has been disabled in this channel."),
                    TimeSpan.FromSeconds(8));
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
        var cacheKey = $"{context.User.Id}-{context.Guild.Id}-last-message-updated";
        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        this._cache.Set(cacheKey, 1, TimeSpan.FromMinutes(15));

        var guildSuccess = PublicProperties.PremiumServers.TryGetValue(context.Guild.Id, out var guildId);
        var userSuccess = PublicProperties.RegisteredUsers.TryGetValue(context.User.Id, out var userId);

        var guildUser = await ((IGuild)context.Guild).GetUserAsync(context.User.Id, CacheMode.CacheOnly);

        if (!guildSuccess || !userSuccess || guildUser == null)
        {
            return;
        }

        await this._guildService.UpdateGuildUserLastMessageDate(guildUser, userId, guildId);
    }
}
