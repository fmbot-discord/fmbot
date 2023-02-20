using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Handlers;

public class InteractionHandler
{
    private readonly DiscordShardedClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _provider;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly GuildSettingBuilder _guildSettingBuilder;

    private readonly IGuildDisabledCommandService _guildDisabledCommandService;
    private readonly IChannelDisabledCommandService _channelDisabledCommandService;

    private readonly IMemoryCache _cache;

    private InteractiveService Interactivity { get; }


    public InteractionHandler(DiscordShardedClient client,
        InteractionService interactionService,
        IServiceProvider provider,
        UserService userService,
        GuildService guildService,
        IGuildDisabledCommandService guildDisabledCommandService,
        IChannelDisabledCommandService channelDisabledCommandService,
        IMemoryCache cache, GuildSettingBuilder guildSettingBuilder, InteractiveService interactivity)
    {
        this._client = client;
        this._interactionService = interactionService;
        this._provider = provider;
        this._userService = userService;
        this._guildService = guildService;
        this._guildDisabledCommandService = guildDisabledCommandService;
        this._channelDisabledCommandService = channelDisabledCommandService;
        this._cache = cache;
        this._guildSettingBuilder = guildSettingBuilder;
        this.Interactivity = interactivity;
        this._client.SlashCommandExecuted += SlashCommandAsync;
        this._client.AutocompleteExecuted += AutoCompleteAsync;
        this._client.SelectMenuExecuted += SelectMenuHandler;
        this._client.ModalSubmitted += ModalSubmitted;
        client.UserCommandExecuted += UserCommandAsync;

    }

    private async Task SlashCommandAsync(SocketInteraction socketInteraction)
    {
        if (socketInteraction is not SocketSlashCommand socketSlashCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var contextUser = await this._userService.GetUserAsync(context.User.Id);

        var commandSearch = this._interactionService.SearchSlashCommand(socketSlashCommand);

        if (!commandSearch.IsSuccess)
        {
            Log.Error("Someone tried to execute a non-existent slash command! {slashCommand}", socketSlashCommand.CommandName);
            return;
        }

        var command = commandSearch.Command;

        if (contextUser?.Blocked == true)
        {
            await UserBlockedResponse(context);
            return;
        }

        if (!await CommandDisabled(context, command))
        {
            return;
        }

        if (command.Attributes.OfType<UsernameSetRequired>().Any())
        {
            if (contextUser == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                var userNickname = (context.User as SocketGuildUser)?.Nickname;
                embed.UsernameNotSetErrorResponse("/", userNickname ?? context.User.Username);

                await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return;
            }
        }
        if (command.Attributes.OfType<UserSessionRequired>().Any())
        {
            if (contextUser?.SessionKeyLastFm == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                embed.SessionRequiredResponse("/");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return;
            }
        }
        if (command.Attributes.OfType<GuildOnly>().Any())
        {
            if (context.Guild == null)
            {
                await context.Interaction.RespondAsync("This command is not supported in DMs.");
                context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }
        }
        if (command.Attributes.OfType<RequiresIndex>().Any() && context.Guild != null)
        {
            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(context.Guild);
            if (lastIndex == null)
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("To use .fmbot commands with server-wide statistics you need to create a memberlist cache first.\n\n" +
                                      $"Please run `/refreshmembers` to create this.\n" +
                                      $"Note that this can take some time on large servers.");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (lastIndex < DateTime.UtcNow.AddDays(-120))
            {
                var embed = new EmbedBuilder();
                embed.WithDescription("Server member data is out of date, it was last updated over 120 days ago.\n" +
                                      $"Please run `/refreshmembers` to update this server.");
                await context.Interaction.RespondAsync(null, new[] { embed.Build() });
                context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);

        Statistics.SlashCommandsExecuted.WithLabels(command.Name).Inc();
        _ = this._userService.UpdateUserLastUsedAsync(context.User.Id);
    }

    private async Task AutoCompleteAsync(SocketInteraction socketInteraction)
    {
        var context = new ShardedInteractionContext(this._client, socketInteraction);
        await this._interactionService.ExecuteCommandAsync(context, this._provider);
    }

    private async Task UserCommandAsync(SocketInteraction socketInteraction)
    {
        if (socketInteraction is not SocketUserCommand socketUserCommand)
        {
            return;
        }

        var context = new ShardedInteractionContext(this._client, socketInteraction);
        var commandSearch = this._interactionService.SearchUserCommand(socketUserCommand);

        if (!commandSearch.IsSuccess)
        {
            Log.Error("Someone tried to execute a non-existent user command! {slashCommand}", socketUserCommand.CommandName);
            return;
        }

        var contextUser = await this._userService.GetUserAsync(context.User.Id);

        if (commandSearch.Command.Attributes.OfType<UsernameSetRequired>().Any())
        {
            if (contextUser == null)
            {
                var embed = new EmbedBuilder()
                    .WithColor(DiscordConstants.LastFmColorRed);
                var userNickname = (context.User as SocketGuildUser)?.Nickname;

                embed.UsernameNotSetErrorResponse("/", userNickname ?? context.User.Username);
                await context.Interaction.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
                context.LogCommandUsed(CommandResponse.UsernameNotSet);
                return;
            }
        }

        await this._interactionService.ExecuteCommandAsync(context, this._provider);
    }

    private async Task<bool> CommandDisabled(ShardedInteractionContext context, SlashCommandInfo searchResult)
    {
        if (context.Guild != null)
        {
            var disabledGuildCommands = this._guildDisabledCommandService.GetDisabledCommands(context.Guild?.Id);
            if (disabledGuildCommands != null &&
                disabledGuildCommands.Any(searchResult.Name.Equals))
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this server.",
                    ephemeral: true);
                return false;
            }

            var disabledChannelCommands = this._channelDisabledCommandService.GetDisabledCommands(context.Channel?.Id);
            if (disabledChannelCommands != null &&
                disabledChannelCommands.Any() &&
                disabledChannelCommands.Any(searchResult.Name.Equals) &&
                context.Channel != null)
            {
                await context.Interaction.RespondAsync(
                    "The command you're trying to execute has been disabled in this channel.",
                    ephemeral: true);
                return false;
            }
        }

        return true;
    }

    public async Task SelectMenuHandler(SocketMessageComponent arg)
    {
#if DEBUG
        Log.Information("Received SelectMenuHandler");
#endif

        if (arg.Data.CustomId == null)
        {
            return;
        }

        var userSettings = await this._userService.GetUserSettingsAsync(arg.User);
        var embed = new EmbedBuilder();

        if (userSettings == null)
        {
            embed.WithColor(DiscordConstants.LastFmColorRed);
            var userNickname = (arg.User as SocketGuildUser)?.Nickname;
            embed.UsernameNotSetErrorResponse("/", userNickname ?? arg.User.Username);
            await arg.RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
            return;
        }

        if (arg.Data.CustomId == Constants.FmSettingType)
        {
            if (Enum.TryParse(arg.Data.Values.FirstOrDefault(), out FmEmbedType embedType))
            {
                var newUserSettings = await this._userService.SetSettings(userSettings, embedType, FmCountType.None);

                embed.WithDescription($"Your `fm` mode has been set to **{newUserSettings.FmEmbedType}**.");
                embed.WithColor(DiscordConstants.InformationColorBlue);
                await arg.RespondAsync(embed: embed.Build(), ephemeral: true);
            }

            return;
        }

        if (arg.Data.CustomId == Constants.FmSettingFooter)
        {
            var maxOptions = userSettings.UserType == UserType.User ? Constants.MaxFooterOptions : Constants.MaxFooterOptionsSupporter;
            var amountSelected = 0;

            foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
            {
                if (Enum.TryParse(option, out FmFooterOption flag))
                {
                    var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                    if (!supporterOnly)
                    {
                        if (arg.Data.Values.Any(a => a == option) && amountSelected <= maxOptions)
                        {
                            userSettings.FmFooterOptions |= flag;
                            amountSelected++;
                        }
                        else
                        {
                            userSettings.FmFooterOptions &= ~flag;
                        }
                    }
                }
            }

            await SaveFooterOptions(arg, userSettings, embed);
            return;
        }
        if (arg.Data.CustomId == Constants.FmSettingFooterSupporter && userSettings.UserType != UserType.User)
        {
            var maxOptions = userSettings.UserType == UserType.User ? 0 : 1;
            var amountSelected = 0;

            foreach (var option in Enum.GetNames(typeof(FmFooterOption)))
            {
                if (Enum.TryParse(option, out FmFooterOption flag))
                {
                    var supporterOnly = flag.GetAttribute<OptionAttribute>().SupporterOnly;
                    if (supporterOnly)
                    {
                        if (arg.Data.Values.Any(a => a == option) && amountSelected <= maxOptions && option != "none")
                        {
                            userSettings.FmFooterOptions |= flag;
                            amountSelected++;
                        }
                        else
                        {
                            userSettings.FmFooterOptions &= ~flag;
                        }
                    }
                }
            }

            await SaveFooterOptions(arg, userSettings, embed);
            return;
        }
        if (arg.Data.CustomId == Constants.GuildSetting && arg.Data.Values.Any())
        {
            var setting = arg.Data.Values.First().Replace("gs-", "");

            var context = new ShardedInteractionContext(this._client, arg);

            if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
            {
                ResponseModel response;
                switch (guildSetting)
                {
                    case GuildSetting.TextPrefix:
                        if (setting.Contains("view"))
                        {
                            await this._guildSettingBuilder.RespondToPrefixSetter(context);
                        }
                        break;
                    case GuildSetting.EmoteReactions:
                        break;
                    case GuildSetting.DefaultEmbedType:
                        break;
                    case GuildSetting.WhoKnowsActivityThreshold:
                        break;
                    case GuildSetting.WhoKnowsBlockedUsers:
                        response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(context, userSettings));
                        await context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    case GuildSetting.CrownActivityThreshold:
                        break;
                    case GuildSetting.CrownBlockedUsers:
                        response = await this._guildSettingBuilder.BlockedUsersAsync(new ContextModel(context, userSettings), true);
                        await context.SendResponse(this.Interactivity, response, ephemeral: true);
                        break;
                    case GuildSetting.CrownMinimumPlaycount:
                        break;
                    case GuildSetting.CrownsDisabled:
                        break;
                    case GuildSetting.DisabledCommands:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return;
        }
    }

    private async Task SaveFooterOptions(IDiscordInteraction arg, User userSettings, EmbedBuilder embed)
    {
        userSettings = await this._userService.SetFooterOptions(userSettings, userSettings.FmFooterOptions);


        var description = new StringBuilder();
        description.AppendLine("Your `fm` footer options have been set to:");

        foreach (var flag in userSettings.FmFooterOptions.GetUniqueFlags())
        {
            if (userSettings.FmFooterOptions.HasFlag(flag) && flag != FmFooterOption.None)
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await arg.RespondAsync(embed: embed.Build(), ephemeral: true);
    }

    public async Task ModalSubmitted(SocketModal socketModal)
    {
        if (!socketModal.Data.CustomId.StartsWith("gs-"))
        {
            return;
        }

        var setting = socketModal.Data.CustomId.Replace("gs-", "");

        var context = new ShardedInteractionContext(this._client, socketModal);

        if (Enum.TryParse(setting.Replace("view-", "").Replace("set-", ""), out GuildSetting guildSetting))
        {
            switch (guildSetting)
            {
                case GuildSetting.TextPrefix:
                    if (setting.Contains("set"))
                    {
                        var value = socketModal.Data.Components.First().Value;
                        await this._guildSettingBuilder.RespondWithPrefixSet(context, value);
                    }
                    break;
                case GuildSetting.EmoteReactions:
                    break;
                case GuildSetting.DefaultEmbedType:
                    break;
                case GuildSetting.WhoKnowsActivityThreshold:
                    break;
                case GuildSetting.WhoKnowsBlockedUsers:
                    break;
                case GuildSetting.CrownActivityThreshold:
                    break;
                case GuildSetting.CrownBlockedUsers:
                    break;
                case GuildSetting.CrownMinimumPlaycount:
                    break;
                case GuildSetting.CrownsDisabled:
                    break;
                case GuildSetting.DisabledCommands:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static async Task UserBlockedResponse(ShardedInteractionContext shardedCommandContext)
    {
        var embed = new EmbedBuilder().WithColor(DiscordConstants.LastFmColorRed);
        embed.UserBlockedResponse("/");
        await shardedCommandContext.Channel.SendMessageAsync("", false, embed.Build());
        shardedCommandContext.LogCommandUsed(CommandResponse.UserBlocked);
        return;
    }
}
