using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;
using NetCord.Rest;

namespace FMBot.Bot.TextCommands;

[ModuleName("Friends")]
public class FriendsCommands(
    FriendsService friendsService,
    GuildService guildService,
    IPrefixService prefixService,
    IDataSourceFactory dataSourceFactory,
    UserService userService,
    IOptions<BotSettings> botSettings,
    SettingService settingService,
    UpdateService updateService,
    FriendBuilders friendBuilders,
    InteractiveService interactivity)
    : BaseCommandModule(botSettings)
{
    private readonly GuildService _guildService = guildService;
    private readonly IDataSourceFactory _dataSourceFactory = dataSourceFactory;
    private readonly SettingService _settingService = settingService;
    private readonly UpdateService _updateService = updateService;

    private InteractiveService Interactivity { get; } = interactivity;

    [Command("friends", "recentfriends", "friendsrecent", "f")]
    [Summary("Displays your friends and what they're listening to.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    [SupporterEnhanced($"Supporters can add up to 18 friends (up from 12)")]
    public async Task FriendsAsync([CommandParameter(Remainder = true)]string unused = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await friendBuilders.FriendsAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("friended")]
    [Summary("Displays people who have added you as a friend.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task FriendedAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await friendBuilders.FriendedAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("addfriends", "friend", "friendsset", "setfriends", "friendsadd", "addfriend", "setfriend", "add")]
    [Summary("Adds users to your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("addfriends fm-bot @user", "addfriends 356268235697553409")]
    [UsernameSetRequired]
    [GuildOnly]
    [CommandCategories(CommandCategory.Friends)]
    [SupporterEnhanced($"Supporters can add up to 18 friends (up from 12)")]
    public async Task AddFriends([CommandParameter(Remainder = true)] string friendsInput = null)
    {
        var enteredFriends = string.IsNullOrWhiteSpace(friendsInput)
            ? []
            : friendsInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (enteredFriends.Length == 0)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please enter at least one friend to add. You can use their Last.fm usernames, Discord mention or Discord id." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NotSupportedInDm }, userService);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await friendBuilders.AddFriendsAsync(new ContextModel(this.Context, prfx, contextUser), enteredFriends);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("removefriends", "unfriend", "friendsremove", "deletefriend", "deletefriends", "removefriend", "unadd")]
    [Summary("Removes users from your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("removefriends fm-bot @user", "removefriend 356268235697553409")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveFriends([CommandParameter(Remainder = true)] string friendsInput = null)
    {
        var enteredFriends = string.IsNullOrWhiteSpace(friendsInput)
            ? []
            : friendsInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        if (enteredFriends.Length == 0)
        {
            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Please enter at least one friend to remove. You can use their Last.fm usernames, Discord mention or discord id." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            return;
        }

        try
        {
            var response = await friendBuilders.RemoveFriendsAsync(new ContextModel(this.Context, prfx, contextUser), enteredFriends);

            await this.Context.SendResponse(this.Interactivity, response, userService);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [Command("removeallfriends", "friendsremoveall")]
    [Summary("Remove all your friends")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveAllFriends()
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            await friendsService.RemoveAllFriendsAsync(userSettings.UserId);

            await this.Context.Client.Rest.SendMessageAsync(this.Context.Message.ChannelId, new MessageProperties { Content = "Removed all your friends." });
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
