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
using FMBot.Domain.Models;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.SlashCommands;

public class UserSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;
    private readonly LastFmRepository _lastFmRepository;
    private readonly GuildService _guildService;
    private readonly FriendsService _friendsService;
    private readonly IIndexService _indexService;
    private readonly UserBuilder _userBuilder;
    private readonly SettingService _settingService;

    private readonly BotSettings _botSettings;

    private InteractiveService Interactivity { get; }

    public UserSlashCommands(UserService userService,
        LastFmRepository lastFmRepository,
        IOptions<BotSettings> botSettings,
        GuildService guildService,
        IIndexService indexService,
        InteractiveService interactivity,
        FriendsService friendsService,
        UserBuilder userBuilder,
        SettingService settingService)
    {
        this._userService = userService;
        this._lastFmRepository = lastFmRepository;
        this._guildService = guildService;
        this._indexService = indexService;
        this.Interactivity = interactivity;
        this._friendsService = friendsService;
        this._userBuilder = userBuilder;
        this._settingService = settingService;
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
                $"http://www.last.fm/api/auth/?api_key={this._botSettings.LastFm.PublicKey}&token={token.Content.Token}";

            if (contextUser == null)
            {
                reply.AppendLine($"**[Click here add your Last.fm account to .fmbot]({link})**");
                reply.AppendLine();
                reply.AppendLine("Link will expire after 5 minutes, please wait a moment after allowing access...");
                reply.AppendLine();
                reply.AppendLine("Don't have a Last.fm account yet? \n" +
                                 "[Sign up here](https://last.fm/join) and see [how to track your music here](https://last.fm/about/trackmymusic).");
            }
            else
            {
                reply.AppendLine(
                    $"You have already logged in before. If you want to change or reconnect your connected Last.fm account, [click here.]({link}) " +
                    $"Note that this link will expire after 5 minutes.");
                reply.AppendLine();
                reply.AppendLine(
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? " +
                    $"Re-logging in again will not fix this, please use `/outofsync` for help instead.");
            }

            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.LastFmColorRed);
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
                        var discordGuildUser = await this.Context.Guild.GetUserAsync(this.Context.User.Id);
                        var newGuildUser = new GuildUser
                        {
                            Bot = false,
                            GuildId = guild.GuildId,
                            UserId = newUserSettings.UserId,
                            UserName = discordGuildUser?.DisplayName,
                        };

                        if (guild.WhoKnowsWhitelistRoleId.HasValue && discordGuildUser != null)
                        {
                            newGuildUser.WhoKnowsWhitelisted = discordGuildUser.RoleIds.Contains(guild.WhoKnowsWhitelistRoleId.Value);
                        }

                        await this._indexService.AddGuildUserToDatabase(newGuildUser);
                    }
                }
            }
            else
            {
                followUpEmbed.WithColor(DiscordConstants.WarningColorOrange);
                followUpEmbed.WithDescription(
                    $"Login expired. Re-run the command to try again.\n\n" +
                    $"Having trouble connecting your Last.fm to .fmbot? Feel free to ask for help on our support server.");

                await FollowupAsync(null, new[] { followUpEmbed.Build() }, ephemeral: true);

                this.Context.LogCommandUsed(CommandResponse.WrongInput);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, deferFirst: true);
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
        reply.Append($"Your `/fm` mode has been set to **{newUserSettings.FmEmbedType}**");
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

    [SlashCommand("remove", "Deletes your .fmbot account")]
    [UsernameSetRequired]
    public async Task RemoveAsync()
    {
        var userSettings = await this._userService.GetFullUserAsync(this.Context.User.Id);

        var embed = UserBuilder.GetRemoveDataEmbed(userSettings, "/");

        var builder = new ComponentBuilder()
            .WithButton("Confirm", "id");

        await RespondAsync("", new[] { embed.Build() }, components: builder.Build(), ephemeral: true);
        var msg = await this.Context.Interaction.GetOriginalResponseAsync();

        var result = await this.Interactivity.NextInteractionAsync(x => x is SocketMessageComponent c && c.Message.Id == msg.Id && x.User.Id == this.Context.User.Id,
            timeout: TimeSpan.FromSeconds(60));

        if (result.IsSuccess)
        {
            await result.Value.DeferAsync();

            await this._friendsService.RemoveAllFriendsAsync(userSettings.UserId);
            await this._friendsService.RemoveUserFromOtherFriendsAsync(userSettings.UserId);

            await this._userService.DeleteUser(userSettings.UserId);

            var followUpEmbed = new EmbedBuilder();
            followUpEmbed.WithTitle("Removal successful");
            followUpEmbed.WithDescription("Your settings, friends and any other data have been successfully deleted from .fmbot.");
            await FollowupAsync(embeds: new[] { followUpEmbed.Build() }, ephemeral: true);
        }
        else
        {
            var followUpEmbed = new EmbedBuilder();
            followUpEmbed.WithTitle("Removal timed out");
            followUpEmbed.WithDescription("If you still wish to delete your .fmbot account, please try again.");
            await FollowupAsync(embeds: new[] { followUpEmbed.Build() }, ephemeral: true);
        }

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: true);
        this.Context.LogCommandUsed();
    }

    [SlashCommand("featured", "Shows what is currently featured (and the bots avatar)")]
    public async Task FeaturedAsync()
    {
        var response = await this._userBuilder.FeaturedAsync(new ContextModel(this.Context));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);

        var message = await this.Context.Interaction.GetOriginalResponseAsync();

        if (message != null && response.CommandResponse == CommandResponse.Ok && this.Context.Guild != null)
        {
            await this._guildService.AddReactionsAsync(message, this.Context.Guild, response.Text == "in-server");
        }
    }

    [SlashCommand("botscrobbling", "Shows info about music bot scrobbling and allows you to change your settings")]
    public async Task BotScrobblingAsync([Summary("Enabled", "Whether bot scrobbling should be enabled or not")] bool option = false)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var response = await this._userBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser), option.ToString());

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("featuredlog", "Shows you or someone else their featured history")]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync(
        [Summary("User", "The user to view the featured log for (defaults to self)")] string user = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._userBuilder.FeaturedLogAsync(new ContextModel(this.Context, contextUser), userSettings);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [SlashCommand("stats", "Shows you or someone else their profile")]
    [UsernameSetRequired]
    public async Task StatsAsync(
        [Summary("User", "The user of which you want to view their profile")] string user = null)
    {
        _ = DeferAsync();

        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var userSettings =
            await this._settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await this._userBuilder.StatsAsync(new ContextModel(this.Context, contextUser), userSettings, contextUser);

        await this.Context.SendFollowUpResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }
}
