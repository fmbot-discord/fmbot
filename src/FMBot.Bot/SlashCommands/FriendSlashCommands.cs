using System;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class FriendSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly FriendBuilders _friendBuilders;

    private InteractiveService Interactivity { get; }

    public FriendSlashCommands(UserService userService,
        FriendsService friendsService,
        InteractiveService interactivity,
        SettingService settingService,
        FriendBuilders friendBuilders)
    {
        this._userService = userService;
        this.Interactivity = interactivity;
        this._settingService = settingService;
        this._friendBuilders = friendBuilders;
    }

    [SlashCommand("friends", "Displays your friends and what they're listening to.")]
    [UsernameSetRequired]
    public async Task FriendsAsync()
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._friendBuilders.FriendsAsync(new ContextModel(this.Context, contextUser));

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [UserCommand("Add as friend")]
    [UsernameSetRequired]
    public async Task AddFriendAsync(IUser user)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = await this._friendBuilders.AddFriendsAsync(new ContextModel(this.Context, contextUser), new []{ user.Id.ToString() });

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }

    [UserCommand("Remove friend")]
    [UsernameSetRequired]
    public async Task RemoveFriendAsync(IUser user)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        
        try
        {
            var response = await this._friendBuilders.RemoveFriendsAsync(new ContextModel(this.Context, contextUser), new []{ user.Id.ToString() }, true);

            await this.Context.SendResponse(this.Interactivity, response, true);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
        }
    }
}
