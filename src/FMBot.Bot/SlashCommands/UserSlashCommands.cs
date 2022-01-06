using System;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.SlashCommands;

public class UserSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;

    private readonly BotSettings _botSettings;

    public UserSlashCommands(UserService userService,
        LastFmRepository lastFmRepository,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        IIndexService indexService)
    {
        this._userService = userService;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
        this._indexService = indexService;
        this._botSettings = botSettings.Value;
    }

    [SlashCommand("login", "Gives you a link to connect your Last.fm account to .fmbot")]
    public async Task LoginAsync()
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var token = await this._lastFmRepository.GetAuthToken();

        try
        {
            var reply = new StringBuilder();
            var link =
                $"http://www.last.fm/api/auth/?api_key={this._botSettings.LastFm.Key}&token={token.Content.Token}";

            if (contextUser == null)
            {
                reply.AppendLine($"**[Click here add your Last.fm account to .fmbot]({link})**");
                reply.AppendLine();
                reply.AppendLine("Link will expire after 3 minutes, please wait a moment after allowing access...");
            }
            else
            {
                reply.AppendLine(
                    $"You have already logged in before. If you want to change your connected Last.fm account, [click here.]({link}) " +
                    $"Note that this link will expire after 3 minutes.");
                reply.AppendLine();
                reply.AppendLine(
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? " +
                    $"Re-logging in again will not fix this, please use `/outofsync` for help instead.");
            }

            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.InformationColorBlue);
            embed.WithDescription(reply.ToString());

            await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
            this.Context.LogCommandUsed();

            var loginSuccess = await this._userService.GetAndStoreAuthSession(this.Context.User, token.Content.Token);

            var followUpEmbed = new EmbedBuilder();
            if (loginSuccess)
            {
                followUpEmbed.WithColor(DiscordConstants.SuccessColorGreen);
                var newUserSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
                var description =
                    $"✅ You have been logged in to .fmbot with the username [{newUserSettings.UserNameLastFM}]({Constants.LastFMUserUrl}{newUserSettings.UserNameLastFM})!\n\n" +
                    $"`/mode` has been set to: `{newUserSettings.FmEmbedType}`\n" +
                    $"`/privacy` has been set to: `{newUserSettings.PrivacyLevel}`";

                followUpEmbed.WithDescription(description);

                await FollowupAsync(null, new[] { followUpEmbed.Build() }, ephemeral: true);

                this.Context.LogCommandUsed();

                if (contextUser != null && !string.Equals(contextUser.UserNameLastFM, newUserSettings.UserNameLastFM, StringComparison.CurrentCultureIgnoreCase))
                {
                    await this._indexService.IndexUser(newUserSettings);
                }

                if (this.Context.Guild != null)
                {
                    var guild = await this._guildService.GetGuildForWhoKnows(this.Context.Guild.Id);
                    if (guild != null)
                    {
                        await this._indexService.GetOrAddUserToGuild(guild,
                            await this.Context.Guild.GetUserAsync(this.Context.User.Id), newUserSettings);
                    }
                }
            }
            else
            {
                followUpEmbed.WithColor(DiscordConstants.WarningColorOrange);
                followUpEmbed.WithDescription(
                    $"❌ Login failed.. link expired or something went wrong.\n\n" +
                    $"Having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.");

                await FollowupAsync(null, new[] { followUpEmbed.Build() }, ephemeral: true);

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
            }
        }
        catch (Exception e)
        {
            this.Context.LogCommandException(e);
            await ReplyAsync(
                "Unable to send you a login link. Please try again later or contact .fmbot support.");
        }
    }

    [SlashCommand("privacy", "Changes your visibility to other .fmbot users in Global WhoKnows")]
    [UsernameSetRequired]
    public async Task PrivacyAsync([Summary("level", "Privacy level for your .fmbot account")] PrivacyLevel privacyLevel)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var newPrivacyLevel = await this._userService.SetPrivacyLevel(userSettings, privacyLevel);

        var reply = new StringBuilder();
        reply.AppendLine($"Your privacy level has been set to **{newPrivacyLevel}**.");
        reply.AppendLine();

        if (newPrivacyLevel == PrivacyLevel.Global)
        {
            reply.AppendLine("You will now be visible in the global WhoKnows with your Last.fm username.");
        }
        if (newPrivacyLevel == PrivacyLevel.Server)
        {
            reply.AppendLine("You will not be visible in the global WhoKnows with your Last.fm username, but users you share a server with will still see it.");
        }

        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithDescription(reply.ToString());

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);

        this.Context.LogCommandUsed();
    }

    [SlashCommand("mode", "Changes your '/fm' layout")]
    [UsernameSetRequired]
    public async Task ModeAsync([Summary("mode", "Mode your fm command should use")] FmEmbedType embedType,
        [Summary("playcount-type", "Extra playcount your fm command should show")] FmCountType countType = FmCountType.None)
    {
        var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

        var newUserSettings = await this._userService.SetSettings(userSettings, embedType, countType);

        var reply = new StringBuilder();
        reply.Append($"Your `.fm` mode has been set to **{newUserSettings.FmEmbedType}**");
        if (newUserSettings.FmCountType != null)
        {
            reply.Append($" with the **{newUserSettings.FmCountType.ToString().ToLower()} playcount**.");
        }
        else
        {
            reply.Append($" with no extra playcount.");
        }

        if (this.Context.Guild != null)
        {
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
            if (guild?.FmEmbedType != null)
            {
                reply.Append($"\n\nNote that servers can force a specific mode which will override your own mode. " +
                             $"\nThis server has the **{guild?.FmEmbedType}** mode set for everyone, which means your own setting will not apply here.");
            }
        }

        var embed = new EmbedBuilder();
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithDescription(reply.ToString());

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);

        this.Context.LogCommandUsed();
    }
}
