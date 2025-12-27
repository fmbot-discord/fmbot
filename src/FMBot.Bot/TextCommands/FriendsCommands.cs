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
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[ModuleName("Friends")]
public class FriendsCommands : BaseCommandModule
{
    private readonly FriendsService _friendsService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly UpdateService _updateService;
    private readonly FriendBuilders _friendBuilders;

    private InteractiveService Interactivity { get; }

    public FriendsCommands(
        FriendsService friendsService,
        GuildService guildService,
        IPrefixService prefixService,
        IDataSourceFactory dataSourceFactory,
        UserService userService,
        IOptions<BotSettings> botSettings,
        SettingService settingService,
        UpdateService updateService,
        FriendBuilders friendBuilders,
        InteractiveService interactivity) : base(botSettings)
    {
        this._friendsService = friendsService;
        this._guildService = guildService;
        this._dataSourceFactory = dataSourceFactory;
        this._prefixService = prefixService;
        this._userService = userService;
        this._settingService = settingService;
        this._updateService = updateService;
        this._friendBuilders = friendBuilders;
        this.Interactivity = interactivity;
    }

    [Command("friends", "recentfriends", "friendsrecent", "f")]
    [Summary("Displays your friends and what they're listening to.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    [SupporterEnhanced($"Supporters can add up to 18 friends (up from 12)")]
    public async Task FriendsAsync([CommandParameter(Remainder = true)]string unused = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await this._friendBuilders.FriendsAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("friended")]
    [Summary("Displays people who have added you as a friend.")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task FriendedAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await this._friendBuilders.FriendedAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("addfriends", "friend", "friendsset", "setfriends", "friendsadd", "addfriend", "setfriend", "friends add", "add")]
    [Summary("Adds users to your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("addfriends fm-bot @user", "addfriends 356268235697553409")]
    [UsernameSetRequired]
    [GuildOnly]
    [CommandCategories(CommandCategory.Friends)]
    [SupporterEnhanced($"Supporters can add up to 18 friends (up from 12)")]
    public async Task AddFriends(params string[] enteredFriends)
    {
        if (enteredFriends.Length == 0)
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please enter at least one friend to add. You can use their Last.fm usernames, Discord mention or Discord id." });
            this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
            return;
        }

        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        try
        {
            var response = await this._friendBuilders.AddFriendsAsync(new ContextModel(this.Context, prfx, contextUser), enteredFriends);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("removefriends", "unfriend", "friendsremove", "deletefriend", "deletefriends", "removefriend", "friends remove", "friend remove", "unadd")]
    [Summary("Removes users from your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("removefriends fm-bot @user", "removefriend 356268235697553409")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveFriends(params string[] enteredFriends)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        if (enteredFriends.Length == 0)
        {
            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "Please enter at least one friend to remove. You can use their Last.fm usernames, Discord mention or discord id." });
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        try
        {
            var response = await this._friendBuilders.RemoveFriendsAsync(new ContextModel(this.Context, prfx, contextUser), enteredFriends);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("removeallfriends", "friendsremoveall", "friends remove all")]
    [Summary("Remove all your friends")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveAllFriends()
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);

            await this.Context.Channel.SendMessageAsync(new MessageProperties { Content = "Removed all your friends." });
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
