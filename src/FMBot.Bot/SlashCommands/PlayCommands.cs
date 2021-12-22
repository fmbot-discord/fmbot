using System;
using System.Threading.Tasks;
using Discord.Interactions;
using FMBot.Bot.Builders;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class PlayCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildService _guildService;

    public PlayCommands(UserService userService,
        SettingService settingService,
        PlayBuilder playBuilder,
        GuildService guildService)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._playBuilder = playBuilder;
        this._guildService = guildService;
    }

    [SlashCommand("fm", "Shows you or someone else their current track")]
    public async Task NowPlayingAsync([Summary("user", "The user to show (defaults to self)")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._playBuilder.NowPlayingAsync("/", this.Context.Guild, this.Context.Channel,
            this.Context.User, contextUser, userSettings);

        if (response.ResponseType == ResponseType.Embed)
        {
            await FollowupAsync(null, new[] { response.Embed });
        }
        else
        {
            await FollowupAsync(response.Text);
        }

        var message = await this.Context.Interaction.GetOriginalResponseAsync();

        try
        {
            if (message != null && !response.Error && this.Context.Guild != null)
            {
                await this._guildService.AddReactionsAsync(message, this.Context.Guild);
            }
        }
        catch (Exception e)
        {

        }
    }
}
