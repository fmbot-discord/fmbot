using System;
using System.Threading.Tasks;
using Discord.Commands;
using Fergun.Interactive;
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

namespace FMBot.Bot.TextCommands;

[Name("Friends")]
public class FriendsCommands : BaseCommandModule
{
    private readonly FriendsService _friendsService;
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly IUpdateService _updateService;
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
        IUpdateService updateService,
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

    [Command("friends", RunMode = RunMode.Async)]
    [Summary("Displays your friends and what they're listening to.")]
    [Alias("recentfriends", "friendsrecent", "f")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task FriendsAsync()
    {
        _ = this.Context.Channel.TriggerTypingAsync();

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

    [Command("addfriends", RunMode = RunMode.Async)]
    [Summary("Adds users to your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("addfriends fm-bot @user", "addfriends 356268235697553409")]
    [Alias("friend", "friendsset", "setfriends", "friendsadd", "addfriend", "setfriend", "friends add", "add")]
    [UsernameSetRequired]
    [GuildOnly]
    [CommandCategories(CommandCategory.Friends)]
    public async Task AddFriends([Summary("Friend names")] params string[] enteredFriends)
    {
        if (enteredFriends.Length == 0)
        {
            await ReplyAsync("Please enter at least one friend to add. You can use their Last.fm usernames, Discord mention or Discord id.");
            this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
            return;
        }

        _ = this.Context.Channel.TriggerTypingAsync();

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

    [Command("removefriends", RunMode = RunMode.Async)]
    [Summary("Removes users from your friend list")]
    [Options(Constants.UserMentionExample)]
    [Examples("removefriends fm-bot @user", "removefriend 356268235697553409")]
    [Alias("unfriend", "friendsremove", "deletefriend", "deletefriends", "removefriend", "friends remove", "friend remove")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveFriends([Summary("Friend names")] params string[] enteredFriends)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? this._botSettings.Bot.Prefix;

        if (enteredFriends.Length == 0)
        {
            await ReplyAsync("Please enter at least one friend to remove. You can use their Last.fm usernames, Discord mention or discord id.");
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

    [Command("removeallfriends", RunMode = RunMode.Async)]
    [Summary("Remove all your friends")]
    [Alias("friendsremoveall", "friends remove all")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Friends)]
    public async Task RemoveAllFriends()
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);

            await ReplyAsync("Removed all your friends.");
            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
