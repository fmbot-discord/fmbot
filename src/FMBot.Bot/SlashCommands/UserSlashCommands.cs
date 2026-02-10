using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.AutoCompleteHandlers;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class UserSlashCommands(
    UserService userService,
    GuildService guildService,
    UserBuilder userBuilder,
    SettingService settingService,
    OpenAiService openAiService,
    TimerService timerService,
    FmSettingService fmSettingService,
    InteractiveService interactivity)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; } = interactivity;

    [SlashCommand("settings", "Your user settings in .fmbot", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task UserSettingsAsync()
    {
        try
        {
            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var response = UserBuilder.GetUserSettings(new ContextModel(this.Context, contextUser));

            await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [SlashCommand("login", "Gives you a link to connect your Last.fm account to .fmbot", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    public async Task LoginAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        try
        {
            var response = UserBuilder.LoginRequired("/", contextUser != null);

            await this.Context.SendResponse(this.Interactivity, response, userService, true);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [SlashCommand("privacy", "Changes your visibility to other .fmbot users in Global WhoKnows", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task PrivacyAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var response = UserBuilder.Privacy(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("fmmode", "Changes your '/fm' layout", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task FmModeSlashAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var guild = await guildService.GetGuildAsync(this.Context.Guild?.Id);

        contextUser.FmSetting ??= await fmSettingService.GetOrCreateFmSetting(contextUser.UserId);

        var response = UserBuilder.FmMode(new ContextModel(this.Context, contextUser), guild);

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("responsemode", "Changes your default whoknows and top list mode")]
    [UsernameSetRequired]
    public async Task ResponseModeSlashAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

        var response = UserBuilder.ResponseMode(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService, ephemeral: true);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("localization", "Configure your timezone and number format in .fmbot", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task SetLocalization(
        [SlashCommandParameter(Name = "timezone", Description = "Timezone you want to set",
            AutocompleteProviderType = typeof(TimeZoneAutoComplete))]
        string timezone = null,
        [SlashCommandParameter(Name = "numberformat", Description = "Number formatting you want to use")]
        NumberFormat? numberFormat = null)
    {
        try
        {
            var userSettings = await userService.GetUserSettingsAsync(this.Context.User);

            var embeds = new List<EmbedProperties>();
            EmbedProperties timezoneEmbed = null;
            EmbedProperties numberFormatEmbed = null;
            if (timezone != null)
            {
                timezoneEmbed = new EmbedProperties();

                var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone);

                await userService.SetTimeZone(userSettings.UserId, timezone);

                var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZoneInfo);
                var nextMidnight = localTime.Date.AddDays(1);
                var dateValue = ((DateTimeOffset)TimeZoneInfo.ConvertTimeToUtc(nextMidnight, timeZoneInfo))
                    .ToUnixTimeSeconds();

                var reply = new StringBuilder();
                reply.AppendLine($"Your timezone has successfully been updated.");
                reply.AppendLine();
                reply.AppendLine($"- ID: `{timeZoneInfo.Id}`");
                reply.AppendLine($"- Zone: `{timeZoneInfo.DisplayName}`");
                reply.AppendLine($"- Midnight: <t:{dateValue}:t>");

                timezoneEmbed.WithColor(DiscordConstants.InformationColorBlue);
                timezoneEmbed.WithDescription(reply.ToString());
                embeds.Add(timezoneEmbed);
            }

            if (numberFormat.HasValue)
            {
                numberFormatEmbed = new EmbedProperties();

                var setValue = await userService.SetNumberFormat(userSettings.UserId, numberFormat.Value);

                var reply = new StringBuilder();
                reply.AppendLine($"Your number format has successfully been updated.");
                reply.AppendLine();
                reply.AppendLine($"- Format: **{numberFormat}**");
                reply.AppendLine($"- **{231737456.Format(setValue)}** plays");
                reply.AppendLine($"- **{((decimal)42.3).Format(setValue)}** average");

                numberFormatEmbed.WithColor(DiscordConstants.InformationColorBlue);
                numberFormatEmbed.WithDescription(reply.ToString());
                embeds.Add(numberFormatEmbed);
            }

            if (embeds.Count == 0)
            {
                await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                    .WithContent(
                        "No options set. Select one of the slash command options to configure your localization settings.")
                    .WithFlags(MessageFlags.Ephemeral)));
                await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
                return;
            }

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithEmbeds(embeds.ToArray())
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (Exception e)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent(
                    "Something went wrong while setting localization. Please check if you entered a valid timezone.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.WrongInput }, userService);
            await this.Context.HandleCommandException(e, userService, sendReply: false);
        }
    }

    [SlashCommand("remove", "Deletes your .fmbot account", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task RemoveAsync()
    {
        var userSettings = await userService.GetFullUserAsync(this.Context.User.Id);

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedProperties()
                .WithColor(DiscordConstants.WarningColorOrange)
                .WithDescription("Check your DMs to continue with your .fmbot account deletion.");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties()
                    .WithEmbeds([serverEmbed])
                    .WithFlags(MessageFlags.Ephemeral)));
        }
        else
        {
            var serverEmbed = new EmbedProperties()
                .WithColor(DiscordConstants.WarningColorOrange)
                .WithDescription("Check the message below to continue with your .fmbot account deletion.");

            await this.Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                new InteractionMessageProperties()
                    .WithEmbeds([serverEmbed])
                    .WithFlags(MessageFlags.Ephemeral)));
        }

        var response = UserBuilder.RemoveDataResponse(new ContextModel(this.Context, userSettings));
        var dmChannel = await this.Context.User.GetDMChannelAsync();
        await dmChannel.SendMessageAsync(new MessageProperties
        {
            Embeds = [response.Embed],
            Components = response.Components?.Any() == true ? [response.Components] : null
        });
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [MessageCommand("Delete response")]
    [UsernameSetRequired]
    public async Task DeleteResponseAsync(RestMessage message)
    {
        var userSettings = await userService.GetUserSettingsAsync(this.Context.User);
        var interactionToDelete = await userService.GetMessageIdToDelete(message.Id);

        if (interactionToDelete == null)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("No .fmbot response to delete or interaction wasn't stored. \n" +
                             "You can only use this option on the command itself or on the .fmbot response.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (interactionToDelete.UserId != userSettings.UserId)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("You can only delete .fmbot responses to your own commands.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        if (!interactionToDelete.DiscordResponseId.HasValue)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("No response was stored in .fmbot for this command. It might be an older command for which we no longer support deletion.")
                .WithFlags(MessageFlags.Ephemeral)));
            return;
        }

        try
        {
            var fetchedMessage = await this.Context.Channel.GetMessageAsync(interactionToDelete.DiscordResponseId.Value);

            if (interactionToDelete.DiscordId.HasValue && interactionToDelete.Type == UserInteractionType.TextCommand)
            {
                try
                {
                    var ogMessage = await this.Context.Channel.GetMessageAsync(interactionToDelete.DiscordId.Value);
                    await ogMessage.AddReactionAsync(new ReactionEmojiProperties("🚮"));
                }
                catch (RestException)
                {
                    // Original command message was already deleted, continue with response deletion
                }
            }

            await fetchedMessage.DeleteAsync(new RestRequestProperties
                { AuditLogReason = "Deleted by user through message command" });

            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("Removed .fmbot response.")
                .WithFlags(MessageFlags.Ephemeral)));
            await this.Context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.Ok }, userService);
        }
        catch (RestException)
        {
            await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
                .WithContent("This message has already been deleted or is no longer accessible.")
                .WithFlags(MessageFlags.Ephemeral)));
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService, deferFirst: true);
        }
    }

    [SlashCommand("judge", "Judges your music taste using AI", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task JudgeAsync(
        [SlashCommandParameter(Name = "time-period", Description = "Time period",
            AutocompleteProviderType = typeof(DateTimeAutoComplete))]
        string timePeriod = null,
        [SlashCommandParameter(Name = "user", Description = "The user to judge")]
        string user = null)
    {
        var contextUser = await userService.GetUserAsync(this.Context.User.Id);
        var timeSettings = SettingService.GetTimePeriod(timePeriod, TimePeriod.Quarterly);

        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var commandUsesLeft = await openAiService.GetJudgeUsesLeft(contextUser);

        var response =
            UserBuilder.JudgeAsync(new ContextModel(this.Context, contextUser), userSettings, timeSettings,
                contextUser.UserType, commandUsesLeft);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("featured", "Shows what is currently featured (and the bot's avatar)", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task FeaturedAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = await userBuilder.FeaturedAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);

        var message = await this.Context.Interaction.GetResponseAsync();

        if (message != null && response.CommandResponse == CommandResponse.Ok)
        {
            PublicProperties.UsedCommandsResponseMessageId.TryAdd(this.Context.Interaction.Id, message.Id);
            PublicProperties.UsedCommandsResponseContextId.TryAdd(message.Id, this.Context.Interaction.Id);

            if (timerService.CurrentFeatured?.Reactions != null &&
                timerService.CurrentFeatured.Reactions.Length != 0)
            {
                await GuildService.AddReactionsAsync(message, timerService.CurrentFeatured.Reactions);
            }
            else
            {
                if (contextUser.EmoteReactions != null && contextUser.EmoteReactions.Length != 0 &&
                    SupporterService.IsSupporter(contextUser.UserType))
                {
                    await GuildService.AddReactionsAsync(message, contextUser.EmoteReactions);
                }
                else if (this.Context.Guild != null)
                {
                    await guildService.AddGuildReactionsAsync(message, this.Context.Guild,
                        response.Text == "in-server");
                }
            }
        }
    }

    [SlashCommand("botscrobbling", "Shows info about music bot scrobbling and allows you to change your settings",
        Contexts =
        [
            InteractionContextType.Guild
        ], IntegrationTypes =
        [
            ApplicationIntegrationType.GuildInstall
        ])]
    [UsernameSetRequired]
    public async Task BotScrobblingAsync()
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var response = UserBuilder.BotScrobblingAsync(new ContextModel(this.Context, contextUser));

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("featuredlog", "Shows you or someone else's featured history", Contexts =
    [
        InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,
        InteractionContextType.Guild
    ], IntegrationTypes =
    [
        ApplicationIntegrationType.GuildInstall,
        ApplicationIntegrationType.UserInstall
    ])]
    [UsernameSetRequired]
    public async Task FeaturedLogAsync(
        [SlashCommandParameter(Name = "view", Description = "Type of log you want to view")]
        FeaturedView view = FeaturedView.User,
        [SlashCommandParameter(Name = "user", Description = "The user to view the featured log for (defaults to self)")]
        string user = null)
    {
        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response =
            await userBuilder.FeaturedLogAsync(new ContextModel(this.Context, contextUser), userSettings, view);

        await this.Context.SendResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }

    [SlashCommand("profile", "Shows you or someone else's profile",
        Contexts =
            [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel, InteractionContextType.Guild],
        IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task ProfileAsync(
        [SlashCommandParameter(Name = "user", Description = "The user of which you want to view their profile")]
        string user = null)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        var contextUser = await userService.GetFullUserAsync(this.Context.User.Id);
        var userSettings =
            await settingService.GetUser(user, contextUser, this.Context.Guild, this.Context.User, true);

        var response = await userBuilder.ProfileAsync(new ContextModel(this.Context, contextUser), userSettings);

        await this.Context.SendFollowUpResponse(this.Interactivity, response, userService);
        await this.Context.LogCommandUsedAsync(response, userService);
    }
}
