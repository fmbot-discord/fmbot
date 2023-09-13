using System;
using System.Threading.Tasks;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
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

    public FriendSlashCommands(UserService userService, FriendsService friendsService, InteractiveService interactivity, SettingService settingService, FriendBuilders friendBuilders)
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
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your artist on Last.fm due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }

    [SlashCommand("friended", "Displays people who have added you as a friend.")]
    [UsernameSetRequired]
    public async Task FriendedAsync()
    {
        _ = DeferAsync();
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try {
            var response = await this._friendBuilders.FriendedAsync(new ContextModel(this.Context, contextUser));

            await this.Context.SendFollowUpResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync("Unable to show people who follow you due to an internal error.");
        }
    }
}
