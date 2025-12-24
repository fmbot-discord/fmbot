using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Extensions;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.Extensions.Options;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

[ModuleName("User settings")]
public class UserCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IPrefixService _prefixService;
    private readonly SettingService _settingService;
    private readonly UserService _userService;
    private readonly UserBuilder _userBuilder;
    private readonly OpenAiService _openAiService;
    private readonly TimerService _timerService;
    private readonly TemplateBuilders _templateBuilders;
    private readonly AdminService _adminService;

    private InteractiveService Interactivity { get; }

    public UserCommands(
        GuildService guildService,
        IPrefixService prefixService,
        SettingService settingService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        UserBuilder userBuilder,
        InteractiveService interactivity,
        OpenAiService openAiService,
        TimerService timerService,
        TemplateBuilders templateBuilders,
        AdminService adminService) : base(botSettings)
    {
        this._guildService = guildService;
        this._prefixService = prefixService;
        this._settingService = settingService;
        this._userService = userService;
        this._userBuilder = userBuilder;
        this.Interactivity = interactivity;
        this._openAiService = openAiService;
        this._timerService = timerService;
        this._templateBuilders = templateBuilders;
        this._adminService = adminService;
    }

    [Command("settings", "userconfig", "usersettings", "usersetting", "setting")]
    [Summary("Your user settings in .fmbot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task UserSettingsAsync([CommandParameter(Remainder = true)] string searchValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = UserBuilder.GetUserSettings(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("profile", "stats", "user")]
    [Summary("Displays user stats related to Last.fm and .fmbot")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    [SupporterEnhanced("Get more insights and an overview of all your years")]
    public async Task StatsAsync([CommandParameter(Remainder = true)] string userOptions = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, contextUser, this.Context, true);

            var response =
                await this._userBuilder.ProfileAsync(new ContextModel(this.Context, prfx, contextUser), userSettings);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("link", "lastfm", "lfm")]
    [Summary("Links a users Last.fm profile")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    public async Task LinkAsync([CommandParameter(Remainder = true)] string userOptions = null)
    {
        var user = await this._userService.GetUserAsync(this.Context.User.Id);

        try
        {
            var userSettings = await this._settingService.GetUser(userOptions, user, this.Context, true);
            var guildUsers = await this._guildService.GetGuildUsers(this.Context.Guild?.Id);

            if (userSettings.DifferentUser && guildUsers.ContainsKey(userSettings.UserId))
            {
                await this.Context.Channel.SendMessageAsync(
                    $"<@{userSettings.DiscordUserId}>'s Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}",
                    allowedMentions: AllowedMentions.None);
            }
            else if (userSettings.DifferentUser)
            {
                await this.Context.Channel.SendMessageAsync(
                    $"Their Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}",
                    allowedMentions: AllowedMentions.None);
            }
            else
            {
                await this.Context.Channel.SendMessageAsync(
                    $"Your Last.fm profile: {LastfmUrlExtensions.GetUserUrl(userSettings.UserNameLastFm)}",
                    allowedMentions: AllowedMentions.None);
            }

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("judge", "roast", "compliment")]
    [Summary("Judges your music taste using AI")]
    [UsernameSetRequired]
    [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
    [CommandCategories(CommandCategory.Other)]
    [SupporterEnhanced("Supporters get an improved AI model with better output and a higher usage limit")]
    public async Task JudgeAsync([CommandParameter(Remainder = true)] string extraOptions = null)
    {
        var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var timeSettings = SettingService.GetTimePeriod(extraOptions, TimePeriod.Quarterly);

        var userSettings = await this._settingService.GetUser(timeSettings.NewSearchValue, contextUser, this.Context);

        var commandUsesLeft = await this._openAiService.GetJudgeUsesLeft(contextUser);

        var response =
            UserBuilder.JudgeAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, timeSettings,
                contextUser.UserType, commandUsesLeft);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("userreactions", "usersetreactions", "useremojis", "userreacts")]
    [Summary("Sets the automatic emoji reactions for the `fm` and `featured` command.\n\n" +
             "Use this command without any emojis to disable.")]
    [Examples("userreactions :PagChomp: :PensiveBlob:", "userreactions 😀 😯 🥵", "userreactions 😀 😯 :PensiveBlob:",
        "userreactions")]
    [UsernameSetRequired]
    [SupporterExclusive("Supporters can set their own emote reactions used globally")]
    public async Task SetUserReactionsAsync([CommandParameter(Remainder = true)] string emojis = null)
    {
        var user = await this._userService.GetUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var supporterRequiredResponse =
            UserBuilder.UserReactionsSupporterRequired(new ContextModel(this.Context, prfx, user), prfx);

        if (supporterRequiredResponse != null)
        {
            await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
            this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
            return;
        }

        if (string.IsNullOrWhiteSpace(emojis))
        {
            await this._userService.SetUserReactionsAsync(user.UserId, null);

            if (user?.EmoteReactions == null || !user.EmoteReactions.Any())
            {
                this._embed.WithDescription(
                    "Use this command with emojis to set the default reactions to `fm` and `featured`.\n\n" +
                    "For example:\n" +
                    $"`{prfx}userreactions ⬆️ ⬇️`");
            }
            else
            {
                this._embed.WithDescription("Removed all user reactions!");
            }

            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            await ReplyAsync(embed: this._embed.Build());

            this.Context.LogCommandUsed();

            return;
        }

        emojis = emojis.Replace("><", "> <");
        var emoteArray = emojis.Split(" ");

        if (emoteArray.Count() > 5)
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, you can't set more then 5 emoji reacts. Please try again.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        if (!GuildService.ValidateReactions(emoteArray))
        {
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            this._embed.WithDescription("Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                                        "Please check if you have a space between every emoji.");
            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);

            return;
        }

        await this._userService.SetUserReactionsAsync(user.UserId, emoteArray);

        this._embed.WithTitle("Automatic user emoji reactions set");
        this._embed.WithDescription(
            "This will apply these emotes to all your `fm` and `featured` commands, regardless of server. " +
            "Please check if all reactions have been applied to this message correctly.");
        this._embed.WithColor(DiscordConstants.InformationColorBlue);
        this._embed.WithFooter("⭐ Supporter perk");

        var message = await ReplyAsync(embed: this._embed.Build());
        this.Context.LogCommandUsed();

        try
        {
            await GuildService.AddReactionsAsync(message, emoteArray);
        }
        catch (Exception e)
        {
            this._embed.WithTitle("Error in set emoji reactions");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);

            if (e.Message.ToLower().Contains("permission"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "The bot does not have the `Add Reactions` permission in this server. Make sure that the permissions for the bot and channel are set correctly.");
            }
            else if (e.Message.ToLower().Contains("unknown emoji"))
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "One or more of the emojis you used are from a server that doesn't have .fmbot. Make sure you only use emojis from servers that have .fmbot.");
            }
            else
            {
                this._embed.WithDescription("Emojis could not be added to the message correctly.\n\n" +
                                            "Make sure the permissions are set correctly and the emojis are from a server that .fmbot is in.");
            }

            await ReplyAsync(embed: this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Error);
        }
    }

    [Command("featured", "featuredavatar", "featureduser", "featuredalbum", "avatar", "ftrd", "ftd", "feat", "pǝɹnʇɐǝɟ")]
    [Summary("Displays the currently picked feature and the user.\n\n" +
             "This command will also show something special if the user is in your server")]
    [CommandCategories(CommandCategory.Other)]
    [SupporterEnhanced(
        "Every first Sunday of the month is Supporter Sunday. The bot will then exclusively feature supporters as a thank-you for supporting the bot.")]
    public async Task FeaturedAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = await this._userBuilder.FeaturedAsync(new ContextModel(this.Context, prfx, contextUser));

            IUserMessage message;
            if (response.ResponseType == ResponseType.Embed)
            {
                message = await ReplyAsync("", false, response.Embed);
            }
            else
            {
                message = await ReplyAsync(response.Text);
            }

            if (message != null && response.CommandResponse == CommandResponse.Ok)
            {
                PublicProperties.UsedCommandsResponseMessageId.TryAdd(this.Context.Message.Id, message.Id);
                PublicProperties.UsedCommandsResponseContextId.TryAdd(message.Id, this.Context.Message.Id);

                if (this._timerService.CurrentFeatured?.Reactions != null &&
                    this._timerService.CurrentFeatured.Reactions.Any())
                {
                    await GuildService.AddReactionsAsync(message, this._timerService.CurrentFeatured.Reactions);
                }
                else
                {
                    if (contextUser?.EmoteReactions != null && contextUser.EmoteReactions.Any() &&
                        SupporterService.IsSupporter(contextUser.UserType))
                    {
                        await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
                    }
                    else if (this.Context.Guild != null)
                    {
                        await this._guildService.AddGuildReactionsAsync(message, this.Context.Guild,
                            response.Text == "in-server");
                    }
                }
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, sendReply: false);
            await ReplyAsync(
                "Unable to show the featured avatar on FMBot due to an internal error. \n" +
                "The bot might not have changed its avatar since its last startup. Please wait until a new featured user is chosen.");
        }
    }

    [Command("featuredlog", "featuredhistory", "recentfeatured", "rf", "recentlyfeatured", "fl", "flog", "ɓolpǝɹnʇɐǝɟ")]
    [Summary("Shows featured history")]
    [Options("global/server/friends/self")]
    [CommandCategories(CommandCategory.Other)]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var userSettings = await this._settingService.GetUser(options, contextUser, this.Context);

            var view = SettingService.SetFeaturedTypeView(userSettings.NewSearchValue);

            if (view != FeaturedView.User)
            {
                userSettings = await this._settingService.GetUser("", contextUser, this.Context);
            }

            var response =
                await this._userBuilder.FeaturedLogAsync(new ContextModel(this.Context, prfx, contextUser),
                    userSettings, view);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("rateyourmusic", "rym")]
    [Summary(
        "Enables or disables the rateyourmusic links. This changes all album links in .fmbot to RYM links instead of Last.fm links.")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    public async Task RateYourMusicAsync([CommandParameter(Remainder = true)] string options = null)
    {
        try
        {
            var user = await this._userService.GetUserSettingsAsync(this.Context.User);

            var newRymSetting = await this._userService.ToggleRymAsync(user);

            if (newRymSetting == true)
            {
                this._embed.WithDescription(
                    "**RateYourMusic** integration has now been enabled for your account.\n\n" +
                    "All album links should now link to RateYourMusic. Please note that since RYM does not provide an API you will be linked to their search page.");
            }
            else
            {
                this._embed.WithDescription(
                    "**RateYourMusic** integration has now been disabled.\n\n" +
                    "All available album links should now link to Last.fm.\n" +
                    "To re-enable the integration, simply do this command again.");
            }

            this._embed.WithColor(32, 62, 121);
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("botscrobbling", "botscrobble", "bottrack", "bottracking")]
    [Summary("Enables or disables the bot scrobbling. For more info, use the command.")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    public async Task BotTrackingAsync([CommandParameter(Remainder = true)] string option = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            var response =
                UserBuilder.BotScrobblingAsync(new ContextModel(this.Context, prfx, contextUser));

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [Command("fmmode")]
    [Summary("Sends you a dm so you can configure your `fm` command.\n\n" +
             "Servers can override your mode with `{{prfx}}servermode`.")]
    [Examples("mode")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task ModeAsync(params string[] otherSettings)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);

            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var response = UserBuilder.FmMode(new ContextModel(this.Context, prfx, contextUser), guild);

            await this.Context.User.SendMessageAsync(embed: response.Embed,
                components: response.Components);

            if (this.Context.Guild != null)
            {
                await ReplyAsync("Check your DMs to configure your `fm` settings!");
            }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await ReplyAsync("Error occurred while trying to send DM, maybe you have DMs disabled. \n" +
                             "Try using the slash command version `/fmmode` instead.");
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    [Command("responsemode", "wkmode", "topmode", "toplistmode")]
    [Summary("Change how your whoknows and top list commands look.")]
    [Examples("responsemode")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task ResponseModeAsync(params string[] otherSettings)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.ResponseMode(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("mode", "md")]
    [UsernameSetRequired]
    [ExcludeFromHelp]
    public async Task PickModeAsync(params string[] otherSettings)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.ModePick(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("privacy")]
    [Summary("Changes your visibility to other .fmbot users.\n\n" +
             "The default privacy setting is 'Server'.")]
    [Options("**Global**: You are visible in the global WhoKnows with your Last.fm username",
        "**Server**: You are not visible in global WhoKnows, but users in the same server will still see your name.")]
    [Examples("privacy")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task PrivacyAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var response = UserBuilder.Privacy(new ContextModel(this.Context, prfx, contextUser));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("templates")]
    [Summary("Configure your fm templates.")]
    [Examples("templates")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    [ExcludeFromHelp]
    public async Task TemplatesAsync(params string[] otherSettings)
    {
        return;

        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var guild = await this._guildService.GetGuildAsync(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var supporterRequiredResponse =
                TemplateBuilders.TemplatesSupporterRequired(new ContextModel(this.Context, prfx, contextUser));

            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response =
                await this._templateBuilders.TemplatePicker(new ContextModel(this.Context, prfx, contextUser), guild);

            await this.Context.SendResponse(this.Interactivity, response);

            // await this.Context.User.SendMessageAsync(embed: response.Embed,
            //     components: response.Components);
            //
            // if (this.Context.Guild != null)
            // {
            //     await ReplyAsync("Check your DMs to configure your `fm` settings!");
            // }

            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await ReplyAsync("Error occurred while trying to send DM, maybe you have DMs disabled. \n" +
                             "Try using the slash command version `/fmmode` instead.");
            await this.Context.HandleCommandException(e, sendReply: false);
        }
    }

    [Command("login", "set", "setusername", "fm set", "connect")]
    [Summary("Starts the login process for connecting a Last.fm account to .fmbot.")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task LoginAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);

        var response = UserBuilder.LoginRequired(prfx, contextUser != null);

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("remove", "delete", "removedata", "deletedata", "logout")]
    [Summary("Deletes your .fmbot account")]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task RemoveAsync([CommandParameter(Remainder = true)] string confirmation = null)
    {
        var contextUser = await this._userService.GetFullUserAsync(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser == null)
        {
            await ReplyAsync("Sorry, but we don't have any data from you in our database.");
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedProperties()
                .WithColor(DiscordConstants.WarningColorOrange)
                .WithDescription("Check your DMs to continue with your .fmbot account deletion.");

            await ReplyAsync(embed: serverEmbed.Build());
        }

        var response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, prfx, contextUser));
        await this.Context.User.SendMessageAsync("", false, response.Embed,
            components: response.Components);
        this.Context.LogCommandUsed(response.CommandResponse);
    }


    [Command("linkedroles", "linkedrole", "updatelinkedroles", "updatelinkedrole")]
    [UsernameSetRequired]
    public async Task UpdateLinkedRoles([CommandParameter(Remainder = true)] string trackValues = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var response = this._userBuilder.ManageLinkedRoles(new ContextModel(this.Context, prfx));

        await this.Context.SendResponse(this.Interactivity, response);
        this.Context.LogCommandUsed(response.CommandResponse);
    }


    [Command("shortcuts", "shortcut", "sc", "scs")]
    [Summary(
        "Allows you to set text command shortcuts. Input is converted to your output while parameters are preserved. Shortcuts still need to use the normal text command prefix.")]
    [SupporterExclusive(
        "Shortcuts are a feature exclusive to Supporters. Shortcuts are stored in-memory across all bot instances to ensure command handling remains fast, so there is limited availability.")]
    [Examples("yo` > `fm textoneline", "progress` > `chart 5x5 2025 skip")]
    [UsernameSetRequired]
    public async Task ShortcutsAsync([CommandParameter(Remainder = true)] string _ = null)
    {
        try
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
            var contextUser = await this._userService.GetUserAsync(this.Context.User.Id);
            var context = new ContextModel(this.Context, prfx, contextUser);

            var supporterRequiredResponse = UserBuilder.ShortcutsSupporterRequired(context);
            if (supporterRequiredResponse != null)
            {
                await this.Context.SendResponse(this.Interactivity, supporterRequiredResponse);
                this.Context.LogCommandUsed(supporterRequiredResponse.CommandResponse);
                return;
            }

            var response = await this._userBuilder.ListShortcutsAsync(context);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
