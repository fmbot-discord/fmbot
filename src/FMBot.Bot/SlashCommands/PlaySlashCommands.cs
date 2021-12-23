using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain.Models;
using SummaryAttribute = Discord.Interactions.SummaryAttribute;

namespace FMBot.Bot.SlashCommands;

public class PlaySlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly PlayBuilder _playBuilder;
    private readonly GuildService _guildService;

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<ulong> StackCooldownTarget = new();

    public PlaySlashCommands(UserService userService,
        SettingService settingService,
        PlayBuilder playBuilder,
        GuildService guildService)
    {
        this._userService = userService;
        this._settingService = settingService;
        this._playBuilder = playBuilder;
        this._guildService = guildService;
    }

    [SlashCommand("fm", "Now Playing - Shows you or someone else their current track")]
    [UsernameSetRequired]
    public async Task NowPlayingAsync([Summary("user", "The user to show (defaults to self)")] string user = null)
    {
        var existingFmCooldown = await this._guildService.GetChannelCooldown(this.Context.Channel.Id);
        if (existingFmCooldown.HasValue)
        {
            if (StackCooldownTarget.Contains(this.Context.User.Id))
            {
                if (StackCooldownTimer[StackCooldownTarget.IndexOf(this.Context.User.Id)].AddSeconds(existingFmCooldown.Value) >= DateTimeOffset.Now)
                {
                    var secondsLeft = (int)(StackCooldownTimer[
                            StackCooldownTarget.IndexOf(this.Context.User.Id)]
                        .AddSeconds(existingFmCooldown.Value) - DateTimeOffset.Now).TotalSeconds;
                    if (secondsLeft <= existingFmCooldown.Value - 2)
                    {
                        await RespondAsync(
                            $"This channel has a `{existingFmCooldown.Value}` second cooldown on `/fm`. Please wait for this to expire before using this command again.",
                            ephemeral: true);
                    }

                    return;
                }

                StackCooldownTimer[StackCooldownTarget.IndexOf(this.Context.User.Id)] = DateTimeOffset.Now;
            }
            else
            {
                StackCooldownTarget.Add(this.Context.User.Id);
                StackCooldownTimer.Add(DateTimeOffset.Now);
            }
        }

        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._playBuilder.NowPlayingAsync("/", this.Context.Guild, this.Context.Channel,
            this.Context.User, contextUser, userSettings);

        this.Context.LogCommandUsed(response.CommandResponse);

        if (response.ResponseType == ResponseType.Embed)
        {
            await FollowupAsync(null, new[] { response.Embed });
        }
        else
        {
            await FollowupAsync(response.Text, allowedMentions: AllowedMentions.None);
        }

        var message = await this.Context.Interaction.GetOriginalResponseAsync();

        try
        {
            if (message != null && response.CommandResponse == CommandResponse.Ok && this.Context.Guild != null)
            {
                await this._guildService.AddReactionsAsync(message, this.Context.Guild);
            }
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e, "Could not add emote reactions");
            await ReplyAsync(
                $"Couldn't add emote reactions to `/fm`. If you have recently changed changed any of the configured emotes please use `/serverreactions` to reset the automatic emote reactions.");
        }
    }

    [SlashCommand("recent", "Shows you or someone else their recent tracks")]
    [UsernameSetRequired]
    public async Task RecentAsync(
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Amount", "Amount of recent tracks to show")] int amount = 5)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        if (amount > 10)
        {
            amount = 10;
        }

        try
        {
            var response = await this._playBuilder.RecentAsync(this.Context.Guild, this.Context.User, contextUser,
                userSettings, amount);

            await RespondAsync(null, new[] { response.Embed });

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to show your recent tracks on Last.fm due to an internal error. Please try again later or contact .fmbot support.");
        }
    }

    [SlashCommand("overview", "Shows you or someone else their recent tracks")]
    [UsernameSetRequired]
    public async Task OverviewAsync(
        [Summary("User", "The user to show (defaults to self)")] string user = null,
        [Summary("Amount", "Amount of days to show")] int amount = 4)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        if (amount > 8)
        {
            amount = 8;
        }

        try
        {
            var response = await this._playBuilder.OverviewAsync(this.Context.Guild, this.Context.User, contextUser,
                userSettings, amount);

            await FollowupAsync(null, new[] { response.Embed });

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await FollowupAsync(
                "Unable to show your overview due to an internal error. Please try again later or contact .fmbot support.",
                ephemeral: true);
        }
    }
}
