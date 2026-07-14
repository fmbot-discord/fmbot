using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using NetCord;
using NetCord.Rest;
using NetCord.Services.Commands;
using Serilog;

namespace FMBot.Bot.Services;

public static class GenericEmbedService
{
    public static void UsernameNotSetErrorResponse(this EmbedProperties embed, Localizer localizer, string name)
    {
        embed.WithDescription(localizer.Translate("welcome.usernameNotSet", ("user", name)));

        embed.WithUrl($"{Constants.DocsUrl}/commands/");

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    public static ActionRowProperties UsernameNotSetErrorComponents(Localizer localizer)
    {
        return new ActionRowProperties()
            .WithButton(localizer.Translate("buttons.signUp"), url: "https://www.last.fm/join")
            .WithButton(localizer.Translate("buttons.connectLastfm"), style: ButtonStyle.Secondary,
                customId: InteractionConstants.User.Login);
    }

    public static ActionRowProperties ReconnectComponents(Localizer localizer)
    {
        return new ActionRowProperties()
            .WithButton(localizer.Translate("buttons.reconnectLastfm"), style: ButtonStyle.Secondary,
                customId: InteractionConstants.User.Login);
    }

    extension(EmbedProperties embed)
    {
        public void RateLimitedResponse(Localizer localizer)
        {
            embed.WithDescription(localizer.Translate("errors.rateLimited"));
            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public void UserBlockedResponse(Localizer localizer)
        {
            embed.WithDescription(localizer.Translate("errors.userBlocked"));
            embed.WithThumbnail("https://i.imgur.com/wNmcoR5.jpg");

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        public void SessionRequiredResponse(Localizer localizer)
        {
            embed.WithDescription(localizer.Translate("errors.sessionRequired"));

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }

        private void NoScrobblesFoundErrorResponse(Localizer localizer, string userName)
        {
            var description = new StringBuilder();
            description.AppendLine(localizer.Translate("errors.noScrobbles.header",
                ("user", userName), ("url", $"{Constants.LastFMUserUrl}{userName}")));
            description.AppendLine();
            description.AppendLine(localizer.Translate("errors.noScrobbles.trackMyMusic"));
            description.AppendLine();
            description.AppendLine(localizer.Translate("errors.noScrobbles.spotify"));
            description.AppendLine();
            description.AppendLine(localizer.Translate("errors.noScrobbles.disclaimer"));

            embed.WithDescription(description.ToString());

            embed.WithColor(DiscordConstants.WarningColorOrange);
        }
    }

    private static ActionRowProperties NoScrobblesFoundComponents(Localizer localizer)
    {
        return new ActionRowProperties()
            .WithButton(localizer.Translate("buttons.trackMyMusicApp"), url: "https://www.last.fm/about/trackmymusic")
            .WithButton(localizer.Translate("buttons.trackSpotify"), url: "https://www.last.fm/settings/applications");
    }

    public static bool RecentScrobbleCallFailed(Response<RecentTrackList> recentScrobbles)
    {
        if (!recentScrobbles.Success || recentScrobbles.Content == null || !recentScrobbles.Content.RecentTracks.Any())
        {
            return true;
        }

        return false;
    }

    public static async Task<bool> RecentScrobbleCallFailedReply(Response<RecentTrackList> recentScrobbles,
        string lastFmUserName, CommandContext context, UserService userService)
    {
        var localizer = Localizer.ForGuild(context.Guild?.Id, discordLocale: context.Guild?.PreferredLocale);
        var embed = new EmbedProperties();
        if (!recentScrobbles.Success || recentScrobbles.Content == null)
        {
            embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, context.Message.Content, localizer, context.User);
            await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.LastFmError }, userService);
            await context.Channel.SendMessageAsync(new MessageProperties
            {
                Embeds = [embed]
            });
            return true;
        }

        if (!recentScrobbles.Content.RecentTracks.Any())
        {
            embed.NoScrobblesFoundErrorResponse(localizer, lastFmUserName);
            await context.LogCommandUsedAsync(new ResponseModel { CommandResponse = CommandResponse.NoScrobbles }, userService);
            await context.Channel.SendMessageAsync(new MessageProperties
            {
                Embeds = [embed],
                Components = [NoScrobblesFoundComponents(localizer)]
            });
            return true;
        }

        return false;
    }

    public static ResponseModel RecentScrobbleCallFailedResponse(Response<RecentTrackList> recentScrobbles,
        string lastFmUserName, Localizer localizer)
    {
        var errorResponse = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (recentScrobbles.Content?.RecentTracks == null || !recentScrobbles.Success)
        {
            errorResponse.Embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, null, localizer);
            errorResponse.ComponentsContainer.WithTextDisplay(errorResponse.Embed.Description ?? localizer.Translate("errors.recentScrobblesFailed"));
            errorResponse.CommandResponse = CommandResponse.LastFmError;
            return errorResponse;
        }

        if (!recentScrobbles.Content.RecentTracks.Any())
        {
            errorResponse.Embed.NoScrobblesFoundErrorResponse(localizer, lastFmUserName);
            errorResponse.ComponentsContainer.WithTextDisplay(errorResponse.Embed.Description ?? localizer.Translate("errors.noScrobblesShort"));
            errorResponse.CommandResponse = CommandResponse.NoScrobbles;
            errorResponse.Components = NoScrobblesFoundComponents(localizer);
            return errorResponse;
        }

        return null;
    }

    public static (EmbedProperties EmbedProperties, bool showPurchaseButtons) HelpResponse(EmbedProperties embed,
        ICommandInfo<CommandContext> commandInfo, string prfx, string userName, Localizer localizer)
    {
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithTitle(localizer.Translate("help.title",
            ("command", $"{prfx}{commandInfo.Aliases[0]}"), ("user", userName)));

        var allAttributes = commandInfo.Attributes.Values.SelectMany(x => x);
        var summary = allAttributes.OfType<SummaryAttribute>().FirstOrDefault()?.Summary;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            embed.WithDescription(summary.Replace("{{prfx}}", prfx));
        }

        var options = allAttributes.OfType<OptionsAttribute>()
            .FirstOrDefault();
        if (options?.Options != null && options.Options.Any())
        {
            var optionsString = new StringBuilder();
            foreach (var option in options.Options)
            {
                optionsString.AppendLine($"- {option}");
            }

            embed.AddField(localizer.Translate("help.options"), optionsString.ToString());
        }

        var examples = allAttributes.OfType<ExamplesAttribute>()
            .FirstOrDefault();
        if (examples?.Examples != null && examples.Examples.Any())
        {
            var examplesString = new StringBuilder();
            foreach (var example in examples.Examples)
            {
                examplesString.AppendLine($"`{prfx}{example}`");
            }

            embed.AddField(localizer.Translate("help.examples"), examplesString.ToString());
        }

        var aliases = commandInfo.Aliases.Skip(1).ToList();
        if (aliases.Any())
        {
            var aliasesString = new StringBuilder();
            for (var index = 0; index < aliases.Count; index++)
            {
                if (index != 0)
                {
                    aliasesString.Append(", ");
                }

                var alias = aliases[index];
                aliasesString.Append($"`{prfx}{alias}`");
            }

            embed.AddField(localizer.Translate("help.aliases"), aliasesString.ToString());
        }

        var showPurchaseButtons = false;
        var supporterEnhanced = allAttributes.OfType<SupporterEnhancedAttribute>()
            .FirstOrDefault();
        if (supporterEnhanced?.Explainer != null)
        {
            showPurchaseButtons = true;
            embed.AddField(localizer.Translate("help.supporterEnhanced"), supporterEnhanced.Explainer);
        }

        var supporterExclusive = allAttributes.OfType<SupporterExclusiveAttribute>()
            .FirstOrDefault();
        if (supporterExclusive?.Explainer != null)
        {
            showPurchaseButtons = true;
            embed.AddField(localizer.Translate("help.supporterExclusive"), supporterExclusive.Explainer);
        }

        return (embed, showPurchaseButtons);
    }

    public static ActionRowProperties PurchaseButtons(ICommandInfo<CommandContext> commandInfo)
    {
        return new ActionRowProperties()
            .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(
                    source: $"help-{commandInfo.Aliases[0]}"));
    }

    extension(EmbedProperties embed)
    {
        public void AddField(string name, string value, bool inline = false)
        {
            embed.AddFields(new EmbedFieldProperties
            {
                Name = name,
                Value = value,
                Inline = inline
            });
        }

        public void WithFooter(string value)
        {
            embed.WithFooter(new EmbedFooterProperties
            {
                Text = value
            });
        }

        public void WithAuthor(string value)
        {
            embed.WithAuthor(new EmbedAuthorProperties
            {
                Name = value
            });
        }

        public void ErrorResponse(ResponseStatus? responseStatus, string message,
            string commandContent, Localizer localizer, User contextUser = null, string expectedResultType = null)
        {
            if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
            {
                embed.AddField(localizer.Translate("errors.staffNote"), $"*\"{PublicProperties.IssuesReason}\"*");
            }

            var loginCommand = PublicProperties.SlashCommands.TryGetValue("login", out var slashCommand)
                ? $"</login:{slashCommand}>"
                : "`/login`";

            switch (responseStatus)
            {
                case ResponseStatus.Failure:
                    embed.WithDescription(localizer.Translate("errors.lastFmFailure")
                        + LastfmErrorRateTracker.GetFailureRateDescription());
                    break;
                case ResponseStatus.LoginRequired:
                    embed.WithDescription(localizer.Translate("errors.lastFmLoginRequired", ("loginCommand", loginCommand)));
                    break;
                case ResponseStatus.BadAuth:
                    embed.WithDescription(localizer.Translate("errors.lastFmBadAuth", ("loginCommand", loginCommand)));
                    break;
                case ResponseStatus.SessionExpired:
                    embed.WithDescription(localizer.Translate("errors.lastFmSessionExpired", ("loginCommand", loginCommand)));
                    break;
                case ResponseStatus.MissingParameters:
                    if (expectedResultType != null)
                    {
                        embed.Title = null;
                        embed.WithDescription(localizer.Translate("errors.lastFmNoResult", ("resultType", expectedResultType)));
                    }
                    else if (message.Equals("Not found"))
                    {
                        embed.WithDescription(localizer.Translate("errors.lastFmUserNotFound"));
                    }
                    else
                    {
                        goto default;
                    }

                    break;
                default:
                    embed.WithDescription(message ?? localizer.Translate("errors.unknown"));
                    break;
            }

            var footer = new StringBuilder();

            if (responseStatus != null)
            {
                footer.Append(localizer.Translate("errors.errorCode", ("code", responseStatus.ToString())));
                footer.AppendLine();
            }

            footer.Append(localizer.Translate("errors.notAffiliated"));

            embed.WithFooter(footer.ToString());

            embed.WithColor(DiscordConstants.WarningColorOrange);
            Log.Information(
                "Last.fm returned error: {message} | {responseStatus} | {discordUserName} / {discordUserId} | {messageContent}",
                message, responseStatus, contextUser?.Username, contextUser?.Id, commandContent);
        }
    }

    extension(ActionRowProperties actionRow)
    {
        public ActionRowProperties WithButton(EmojiProperties emote,
            string url = null, string label = null)
        {
            var linkButton = label == null
                ? new LinkButtonProperties(url, emote)
                : new LinkButtonProperties(url, label, emote);
            actionRow.Add(linkButton);
            return actionRow;
        }

        public ActionRowProperties WithButton(string label,
            string customId = null, ButtonStyle style = ButtonStyle.Secondary, EmojiProperties emote = null,
            bool disabled = false, string url = null, int row = 0)
        {
            if (url != null)
            {
                var linkButton = emote == null
                    ? new LinkButtonProperties(url, label)
                    : new LinkButtonProperties(url, label, emote);
                actionRow.Add(linkButton);
            }
            else if (customId != null)
            {
                var button = emote == null
                    ? new ButtonProperties(customId, label, style)
                    : new ButtonProperties(customId, label, emote, style);
                button.Disabled = disabled;
                actionRow.Add(button);
            }

            return actionRow;
        }
    }

    public static List<ActionRowProperties> WithButton(this List<ActionRowProperties> rows, string label,
        string customId = null, ButtonStyle style = ButtonStyle.Secondary, EmojiProperties emote = null,
        bool disabled = false, string url = null, int row = 0)
    {
        while (rows.Count <= row)
        {
            rows.Add([]);
        }

        if (url != null)
        {
            rows[row].WithButton(label, url: url, emote: emote);
        }
        else if (customId != null)
        {
            rows[row].WithButton(label, customId, style, emote, disabled);
        }

        return rows;
    }

    public static Dictionary<int, ActionRowProperties> WithButton(this Dictionary<int, ActionRowProperties> rows, string label,
        string customId = null, ButtonStyle style = ButtonStyle.Secondary, EmojiProperties emote = null,
        bool disabled = false, string url = null, int row = 0)
    {
        if (!rows.ContainsKey(row))
        {
            rows[row] = [];
        }

        if (url != null)
        {
            rows[row].WithButton(label, url: url, emote: emote);
        }
        else if (customId != null)
        {
            rows[row].WithButton(label, customId, style, emote, disabled);
        }

        return rows;
    }

    public static List<ActionRowProperties> AddComponent(this List<ActionRowProperties> components, ActionRowProperties actionRow)
    {
        components.Add(actionRow);
        return components;
    }

    extension(StringMenuProperties menu)
    {
        public StringMenuProperties AddOption(StringMenuSelectOptionProperties properties)
        {
            menu.Add(properties);
            return menu;
        }

        public StringMenuProperties AddOption(string label, string value,
            bool isDefault = false, string description = null)
        {
            var option = new StringMenuSelectOptionProperties(label, value)
            {
                Default = isDefault,
                Description = description
            };

            menu.Add(option);
            return menu;
        }
    }

    extension(ComponentContainerProperties component)
    {
        public ComponentContainerProperties AddComponent(IComponentContainerComponentProperties properties)
        {
            component.AddComponents(properties);
            return component;
        }

        public ComponentContainerProperties WithSection(IEnumerable<TextDisplayProperties> textDisplays, string thumbnailUrl)
        {
            var section = new ComponentSectionProperties(
                new ComponentSectionThumbnailProperties(new ComponentMediaProperties(thumbnailUrl)),
                textDisplays);
            component.AddComponents(section);
            return component;
        }

        public ComponentContainerProperties WithSection(IEnumerable<TextDisplayProperties> textDisplays, ComponentSectionThumbnailProperties thumbnail)
        {
            var section = new ComponentSectionProperties(thumbnail, textDisplays);
            component.AddComponents(section);
            return component;
        }

        public ComponentContainerProperties WithTextDisplay(string text)
        {
            component.AddComponents(new TextDisplayProperties(text));
            return component;
        }

        public ComponentContainerProperties WithSeparator()
        {
            component.AddComponents(new ComponentSeparatorProperties());
            return component;
        }

        public ComponentContainerProperties WithActionRow(ActionRowProperties actionRow)
        {
            component.AddComponents(actionRow);
            return component;
        }
    }
}
