using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using NetCord;
using NetCord.Gateway;
using NetCord.JsonModels;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.Commands;
using Prometheus;
using Serilog;

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace FMBot.Bot.Handlers;

public class CommandHandler
{
    private readonly CommandService<CommandContext> _commands;
    private readonly UserService _userService;
    private readonly ShardedGatewayClient _discord;
    private readonly MusicBotService _musicBotService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly InteractiveService _interactiveService;
    private readonly IServiceProvider _provider;
    private readonly BotSettings _botSettings;
    private readonly IMemoryCache _cache;
    private readonly IndexService _indexService;
    private readonly GameBuilders _gameBuilders;
    private readonly ShortcutService _shortcutService;

    public CommandHandler(
        ShardedGatewayClient discord,
        CommandService<CommandContext> commands,
        IServiceProvider provider,
        IPrefixService prefixService,
        UserService userService,
        MusicBotService musicBotService,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        InteractiveService interactiveService,
        IMemoryCache cache,
        IndexService indexService,
        GameBuilders gameBuilders,
        ShortcutService shortcutService)
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
        this._shortcutService = shortcutService;
        this._botSettings = botSettings.Value;
        this._discord.MessageCreate += MessageReceived;
        this._discord.MessageUpdate += MessageUpdated;
    }

    private ValueTask MessageReceived(GatewayClient client, Message msg)
    {
        Statistics.DiscordEvents.WithLabels("MessageReceived").Inc();

        var context = new CommandContext(msg, client);

        if (msg.Author.IsBot && !msg.Flags.HasFlag(MessageFlags.Loading))
        {
            if (string.IsNullOrWhiteSpace(msg.Author.Username))
            {
                return ValueTask.CompletedTask;
            }

            _ = Task.Run(async () => await TryScrobbling(msg, context));
            return ValueTask.CompletedTask;
        }

        if (context.Guild != null &&
            PublicProperties.PremiumServers.ContainsKey(context.Guild.Id) &&
            PublicProperties.RegisteredUsers.ContainsKey(context.User.Id))
        {
            _ = Task.Run(async () => await UpdateUserLastMessageDate(context));
        }

        var possibleCommandExecuted = TryForCommand(context, msg, false);

        if (!possibleCommandExecuted &&
            context.Channel?.Id != null &&
            this._cache.TryGetValue(GameService.CacheKeyForJumbleSession(context.Channel.Id), out _) &&
            !string.IsNullOrWhiteSpace(context.Message.Content))
        {
            _ = Task.Run(async () => await this._gameBuilders.JumbleProcessAnswer(new ContextModel(context, "."), context));
        }

        return ValueTask.CompletedTask;
    }

    private async Task TryScrobbling(Message msg, CommandContext context)
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

    private ValueTask MessageUpdated(GatewayClient client, Message message)
    {
        Statistics.DiscordEvents.WithLabels("MessageUpdated").Inc();

        var context = new CommandContext(message, client);

        if (message.Author.IsBot && !message.Flags.HasFlag(MessageFlags.Loading) && message.InteractionMetadata != null)
        {
            _ = Task.Run(async () => await TryScrobbling(message, context));
        }

        TryForCommand(context, message, true);

        return ValueTask.CompletedTask;
    }

    private bool TryForCommand(CommandContext commandContext, Message message,
        bool update)
    {
        var argPos = 0;
        var prfx = this._prefixService.GetPrefix(commandContext.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        // New prefix '.' but user still uses the '.fm' prefix anyway (e.g., '.fmhelp' -> 'help')
        // Only strip '.fm' if the next character is NOT a space (to distinguish '.fmhelp' from '.fm help')
        var fmPrefixLength = (prfx + "fm").Length;
        if (prfx == this._botSettings.Bot.Prefix &&
            message.Content.StartsWith(prfx + "fm", StringComparison.OrdinalIgnoreCase) &&
            message.Content.Length > fmPrefixLength &&
            !char.IsWhiteSpace(message.Content[fmPrefixLength]))
        {
            argPos = fmPrefixLength;
            _ = Task.Run(async () => await ExecuteCommand(message, commandContext, argPos, prfx, update));
            return true;
        }

        // Prefix is set to '.fm' and the user uses '.fm'
        const string fm = ".fm";
        if (prfx == fm && message.Content.StartsWith(".", StringComparison.OrdinalIgnoreCase))
        {
            argPos = 1;
            var searchResult = this._commands.Search(message.Content[argPos..]);
            if (searchResult.IsSuccess &&
                searchResult.Command != null &&
                searchResult.Command.Aliases.Any(a => a == "fm"))
            {
                _ = Task.Run(async () => await ExecuteCommand(message, commandContext, argPos, prfx, update));
                return true;
            }
        }

        // Normal or custom prefix
        if (message.Content.StartsWith(prfx, StringComparison.OrdinalIgnoreCase))
        {
            argPos = prfx.Length;
            _ = Task.Run(async () => await ExecuteCommand(message, commandContext, argPos, prfx, update));
            return true;
        }

        // Mention - check if message starts with mention
        var currentUser = this._discord?.GetCurrentUser();
        if (currentUser != null && message.Content.StartsWith($"<@{currentUser.Id}>"))
        {
            argPos = $"<@{currentUser.Id}>".Length;
            _ = Task.Run(async () => await ExecuteCommand(message, commandContext, argPos, prfx, update));
            return true;
        }
        if (currentUser != null && message.Content.StartsWith($"<@!{currentUser.Id}>"))
        {
            argPos = $"<@!{currentUser.Id}>".Length;
            _ = Task.Run(async () => await ExecuteCommand(message, commandContext, argPos, prfx, update));
            return true;
        }

        return false;
    }

    private async Task ExecuteCommand(Message msg, CommandContext context, int argPos, string prfx,
        bool update = false)
    {
        var messageContent = msg.Content[argPos..];
        var shortcutResult = ShortcutService.FindShortcut(context, messageContent);

        if (shortcutResult.HasValue)
        {
            var (shortcut, remainingArgs) = shortcutResult.Value;
            messageContent = $"{shortcut.Output} {remainingArgs}".Trim();
            _ = Task.Run(() => ShortcutService.AddShortcutReaction(context));
        }

        var searchResult = this._commands.Search(messageContent);

        if (!searchResult.IsSuccess &&
            !msg.Content.StartsWith(prfx, StringComparison.OrdinalIgnoreCase))
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
            // If command possibly equals .fm
            if (!searchResult.IsSuccess &&
                msg.Content.StartsWith(this._botSettings.Bot.Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var fmSearchResult = this._commands.Search(msg.Content[1..]);

                if (!fmSearchResult.IsSuccess)
                {
                    return;
                }

                if (await this._userService.UserBlockedAsync(context.User.Id))
                {
                    await UserBlockedResponse(context, prfx);
                    return;
                }

                var rateLimit = CheckUserRateLimit(context.User.Id);
                if (rateLimit.rateLimited)
                {
                    if (!rateLimit.messageSent)
                    {
                        var embed = new EmbedProperties()
                            .WithColor(DiscordConstants.WarningColorOrange);
                        embed.RateLimitedResponse();
                        await context.Channel.SendMessageAsync(new MessageProperties
                        {
                            Embeds = [embed]
                        });
                    }

                    context.LogCommandUsed(CommandResponse.RateLimited);
                    return;
                }

                if (!await CommandEnabled(context, fmSearchResult, prfx, update))
                {
                    return;
                }

                var commandPrefixResult = await this._commands.ExecuteAsync(messageContent.AsMemory(), context, this._provider);

                if (commandPrefixResult is not IFailResult)
                {
                    Statistics.CommandsExecuted.WithLabels("fm").Inc();

                    _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));
                    _ = Task.Run(async () => await this._userService.AddUserTextCommandInteraction(context, "fm"));
                }
                else if (commandPrefixResult is IFailResult failResult)
                {
                    Log.Error(
                        "CommandHandler error (.fm): {discordUserName} / {discordUserId} | {guildName} / {guildId}  | {messageContent} | Error: {error}",
                        context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, context.Message.Content,
                        failResult.Message);
                }

                return;
            }

            if (!searchResult.IsSuccess)
            {
                return;
            }

            if (!await CommandEnabled(context, searchResult, prfx, update))
            {
                return;
            }

            if (await this._userService.UserBlockedAsync(context.User.Id))
            {
                await UserBlockedResponse(context, prfx);
                return;
            }

            // Get command attributes (NetCord uses a dictionary)
            bool HasAttribute<T>() where T : Attribute =>
                searchResult.Command.Attributes.ContainsKey(typeof(T)) &&
                searchResult.Command.Attributes[typeof(T)].Count > 0;

            if (HasAttribute<UsernameSetRequired>())
            {
                var userIsRegistered = await this._userService.UserRegisteredAsync(context.User);
                if (!userIsRegistered)
                {
                    var embed = new EmbedProperties()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    var guildUser = context.User as GuildUser;

                    embed.UsernameNotSetErrorResponse(prfx ?? this._botSettings.Bot.Prefix,
                        guildUser?.GetDisplayName() ?? context.User.GetDisplayName());
                    await context.Channel.SendMessageAsync(new MessageProperties
                    {
                        Embeds = [embed],
                        Components = [GenericEmbedService.UsernameNotSetErrorComponents()]
                    });
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }

                var rateLimit = CheckUserRateLimit(context.User.Id);
                if (rateLimit.rateLimited)
                {
                    if (!rateLimit.messageSent)
                    {
                        var embed = new EmbedProperties()
                            .WithColor(DiscordConstants.WarningColorOrange);
                        embed.RateLimitedResponse();
                        await context.Channel.SendMessageAsync(new MessageProperties
                        {
                            Embeds = [embed]
                        });
                    }

                    context.LogCommandUsed(CommandResponse.RateLimited);
                    return;
                }
            }

            if (HasAttribute<UserSessionRequired>())
            {
                var userSession = await this._userService.UserHasSessionAsync(context.User);
                if (!userSession)
                {
                    var embed = new EmbedProperties()
                        .WithColor(DiscordConstants.LastFmColorRed);
                    embed.SessionRequiredResponse(prfx ?? this._botSettings.Bot.Prefix);
                    await context.Channel.SendMessageAsync(new MessageProperties
                    {
                        Embeds = [embed]
                    });
                    context.LogCommandUsed(CommandResponse.UsernameNotSet);
                    return;
                }
            }

            if (HasAttribute<GuildOnly>())
            {
                if (context.Guild == null)
                {
                    var dmChannel = await context.User.GetDMChannelAsync();
                    await dmChannel.SendMessageAsync(new MessageProperties
                    {
                        Content = "This command is not supported in DMs."
                    });
                    context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                    return;
                }
            }

            if (HasAttribute<RequiresIndex>() && context.Guild != null)
            {
                var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
                if (lastIndex == null)
                {
                    var embed = new EmbedProperties();
                    embed.WithDescription(
                        "To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                        $"Please run `{prfx}refreshmembers` to create this.\n" +
                        $"Note that this can take some time on large servers.");
                    await context.Channel.SendMessageAsync(new MessageProperties
                    {
                        Embeds = [embed]
                    });
                    context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }

                if (lastIndex < DateTime.UtcNow.AddDays(-180))
                {
                    _ = Task.Run(async () => await this.UpdateGuildIndex(context));
                }
            }

            var commandName = searchResult.Command.Aliases[0];
            if (msg.Content.EndsWith(" help", StringComparison.OrdinalIgnoreCase) && commandName != "help")
            {
                var embed = new EmbedProperties();
                var guildUser = context.Message.Author as GuildUser;
                var userName = guildUser?.GetDisplayName() ?? context.User.GetDisplayName();

                var helpResponse =
                    GenericEmbedService.HelpResponse(embed, searchResult.Command, prfx, userName);

                var messageProps = new MessageProperties
                {
                    Embeds = [embed]
                };

                if (helpResponse.showPurchaseButtons && !await this._userService.UserIsSupporter(context.User))
                {
                    messageProps.Components = [GenericEmbedService.PurchaseButtons(searchResult.Command)];
                }

                await context.Channel.SendMessageAsync(messageProps);
                context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var result = await this._commands.ExecuteAsync(messageContent.AsMemory(), context, this._provider);

            if (result is not IFailResult)
            {
                Statistics.CommandsExecuted.WithLabels(commandName).Inc();

                _ = Task.Run(async () => await this._userService.UpdateUserLastUsedAsync(context.User.Id));

                if (!update)
                {
                    _ = Task.Run(async () => await this._userService.AddUserTextCommandInteraction(context, commandName));
                }
                else
                {
                    _ = Task.Run(async () =>
                        await this._userService.UpdateInteractionContextThroughReference(context.Message.Id, true, false));
                }
            }
            else if (result is IFailResult failResult)
            {
                Log.Error(
                    "CommandHandler error: {discordUserName} / {discordUserId} | {guildName} / {guildId}  | {messageContent} | Error: {error}",
                    context.User?.Username, context.User?.Id, context.Guild?.Name, context.Guild?.Id, context.Message.Content,
                    failResult.Message);
            }
        }
    }

    private (bool rateLimited, bool messageSent) CheckUserRateLimit(ulong discordUserId)
    {
        var shortKey = $"{discordUserId}-ratelimit-short";
        const int shortSeconds = 10;

        var longKey = $"{discordUserId}-ratelimit-long";
        const int longSeconds = 40;

        var cacheKeyErrorSent = $"{discordUserId}-ratelimit-errorsent";

        this._cache.TryGetValue(cacheKeyErrorSent, out bool errorSent);

        if (this._cache.TryGetValue(shortKey, out int recentShortRequests))
        {
            var cacheTime = TimeSpan.FromSeconds(shortSeconds);
            if (recentShortRequests >= 13)
            {
                var cooldown = TimeSpan.FromSeconds(shortSeconds - 2);
                this._cache.Set(cacheKeyErrorSent, true, cooldown);
                this._cache.Set(shortKey, recentShortRequests, cooldown);
                return (true, errorSent);
            }

            recentShortRequests++;
            this._cache.Set(shortKey, recentShortRequests, cacheTime);
        }
        else
        {
            this._cache.Set(shortKey, 1, TimeSpan.FromSeconds(shortSeconds));
        }

        if (this._cache.TryGetValue(longKey, out int recentLongRequests))
        {
            var cacheTime = TimeSpan.FromSeconds(longSeconds);
            if (recentLongRequests >= 35)
            {
                var cooldown = TimeSpan.FromSeconds(longSeconds - 20);
                this._cache.Set(cacheKeyErrorSent, true, cooldown);
                this._cache.Set(shortKey, recentShortRequests, cooldown);
                return (true, errorSent);
            }

            recentLongRequests++;
            this._cache.Set(longKey, recentLongRequests, cacheTime);
        }
        else
        {
            this._cache.Set(longKey, 1, TimeSpan.FromSeconds(longSeconds));
        }

        return (false, errorSent);
    }

    private static async Task<bool> CommandEnabled(CommandContext context, CommandSearchResult searchResult, string prfx,
        bool update = false)
    {
        if (context.Guild != null)
        {
            var isMod = false;
            var guildUser = context.User as GuildUser;
            if (guildUser != null)
            {
                var permissions = guildUser.GetPermissions(context.Guild);
                if (permissions.HasFlag(Permissions.BanUsers) ||
                    permissions.HasFlag(Permissions.Administrator))
                {
                    isMod = true;
                }
            }

            if (searchResult.IsSuccess &&
                (searchResult.Command.Aliases[0].Equals("togglecommand", StringComparison.OrdinalIgnoreCase) ||
                 searchResult.Command.Aliases[0].Equals("toggleservercommand", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var channelDisabled = DisabledChannelService.GetDisabledChannel(context.Channel.Id);
            if (channelDisabled)
            {
                var toggledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
                if (searchResult.IsSuccess &&
                    toggledChannelCommands != null &&
                    toggledChannelCommands.Any() &&
                    toggledChannelCommands.Any(searchResult.Command.Aliases[0].Equals) &&
                    context.Channel != null)
                {
                    return true;
                }

                if (!searchResult.IsSuccess)
                {
                    return false;
                }

                if (!update)
                {
                    if (toggledChannelCommands != null &&
                        toggledChannelCommands.Any())
                    {
                        _ = (await context.Channel.SendMessageAsync(new MessageProperties
                            {
                                Content = "The command you're trying to execute is not enabled in this channel." +
                                    (isMod ? $"\n-# *Configured with `{prfx}togglecommand`*" : null)
                            })).DeleteAfterAsync(8);
                    }
                    else
                    {
                        _ = (await context.Channel.SendMessageAsync(new MessageProperties
                            {
                                Content = "The bot has been disabled in this channel." +
                                    (isMod ? $"\n-# *Configured with `{prfx}togglecommand`*" : null)
                            })).DeleteAfterAsync(8);
                    }
                }

                return false;
            }

            var disabledGuildCommands = GuildDisabledCommandService.GetToggledCommands(context.Guild?.Id);
            if (searchResult.IsSuccess &&
                disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Command.Aliases[0].Equals))
            {
                if (!update)
                {
                    _ = (await context.Channel.SendMessageAsync(new MessageProperties
                        {
                            Content = "The command you're trying to execute has been disabled in this server." +
                                (isMod ? $"\n-# *Configured with `{prfx}toggleservercommand`*" : null)
                        })).DeleteAfterAsync(8);
                }

                return false;
            }

            var disabledChannelCommands = ChannelToggledCommandService.GetToggledCommands(context.Channel?.Id);
            if (searchResult.IsSuccess &&
                disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Command.Aliases[0].Equals) &&
                context.Channel != null)
            {
                if (!update)
                {
                    _ = (await context.Channel.SendMessageAsync(new MessageProperties
                        {
                            Content = "The command you're trying to execute has been disabled in this channel." +
                                (isMod ? $"\n-# *Configured with `{prfx}togglecommand`*" : null)
                        })).DeleteAfterAsync(8);
                }

                return false;
            }
        }

        return true;
    }

    private async Task UserBlockedResponse(CommandContext context, string s)
    {
        var embed = new EmbedProperties()
            .WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse(s ?? this._botSettings.Bot.Prefix);
        await context.Channel.SendMessageAsync(new MessageProperties
        {
            Embeds = [embed]
        });
        context.LogCommandUsed(CommandResponse.UserBlocked);
    }

    private async Task UpdateUserLastMessageDate(CommandContext context)
    {
        var cacheKey = $"{context.User.Id}-{context.Guild.Id}-lst-msg-updated";
        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        this._cache.Set(cacheKey, 1, TimeSpan.FromMinutes(30));

        var guildSuccess = PublicProperties.PremiumServers.TryGetValue(context.Guild.Id, out var guildId);
        var userSuccess = PublicProperties.RegisteredUsers.TryGetValue(context.User.Id, out var userId);

        // In NetCord, context.User is already a GuildUser if in a guild
        var guildUser = context.User as GuildUser;

        if (!guildSuccess || !userSuccess || guildUser == null)
        {
            return;
        }

        await this._guildService.UpdateGuildUserLastMessageDate(guildUser, userId, guildId);
    }

    private async Task UpdateGuildIndex(CommandContext context)
    {
        var guildUsers = new List<GuildUser>();
        await foreach (var user in context.Guild.GetUsersAsync())
        {
            guildUsers.Add(user);
        }

        Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
            guildUsers.Count, context.Guild.Id, context.Guild.Name);

        await this._indexService.StoreGuildUsers(context.Guild, guildUsers);

        await this._guildService.UpdateGuildIndexTimestampAsync(context.Guild, DateTime.UtcNow);

        // NetCord doesn't have PurgeUserCache - cache is managed differently
    }
}
