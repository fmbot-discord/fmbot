using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using Serilog;

namespace FMBot.Bot.Services;

public static class GenericEmbedService
{
    public static void UsernameNotSetErrorResponse(this EmbedBuilder embed, string prfx, string name)
    {
        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login")
            ? $"</login:{PublicProperties.SlashCommands["login"]}>"
            : "`/login`";
        embed.WithDescription($"Hi {name}, welcome to .fmbot. \n" +
                              $"To use this bot you first need to add your Last.fm account.\n\n" +
                              $"Use the buttons below to sign up or connect your existing Last.fm account.");

        embed.WithUrl($"{Constants.DocsUrl}/commands/");

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    public static ComponentBuilder UsernameNotSetErrorComponents()
    {
        return new ComponentBuilder()
            .WithButton("Sign up", style: ButtonStyle.Link, url: "https://www.last.fm/join")
            .WithButton("Connect Last.fm account", style: ButtonStyle.Secondary,
                customId: InteractionConstants.User.Login);
    }

    public static void RateLimitedResponse(this EmbedBuilder embed)
    {
        embed.WithDescription($"Sorry, you're being ratelimited. Please cool down.");
        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    public static void UserBlockedResponse(this EmbedBuilder embed, string prfx)
    {
        embed.WithDescription("You're banned from using .fmbot.");
        embed.WithThumbnailUrl("https://i.imgur.com/wNmcoR5.jpg");

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    public static void SessionRequiredResponse(this EmbedBuilder embed, string prfx)
    {
        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login")
            ? $"</login:{PublicProperties.SlashCommands["login"]}>"
            : "`/login`";
        embed.WithDescription(
            "While you have set your username, you haven't connected .fmbot to your Last.fm account yet, which is required for the command you're trying to use.\n" +
            $"Please use the {loginCommand} command to reconnect your Last.fm account.");

        embed.WithUrl($"{Constants.DocsUrl}/commands/");
        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    private static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, string userName)
    {
        var description = new StringBuilder();
        description.AppendLine(
            $"The Last.fm user **{userName}** has no listening history on [their profile]({Constants.LastFMUserUrl}{userName}) yet.");
        description.AppendLine();
        description.AppendLine(
            "Just created your Last.fm account? Make sure you set it to [track your music app](https://www.last.fm/about/trackmymusic).");
        description.AppendLine();
        description.AppendLine(
            "Using Spotify? You can link that [here](https://www.last.fm/settings/applications). This can take a few minutes to start working.");
        description.AppendLine();
        description.AppendLine($"Please note that .fmbot is not affiliated with Last.fm or Spotify.");

        embed.WithDescription(description.ToString());

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    private static ComponentBuilder NoScrobblesFoundComponents()
    {
        return new ComponentBuilder()
            .WithButton("Track my music app", style: ButtonStyle.Link, url: "https://www.last.fm/about/trackmymusic")
            .WithButton("Track Spotify", style: ButtonStyle.Link, url: "https://www.last.fm/settings/applications");
    }

    public static void ErrorResponse(this EmbedBuilder embed, ResponseStatus? responseStatus, string message,
        string commandContent, IUser contextUser = null, string expectedResultType = null)
    {
        embed.WithTitle("Problem while contacting Last.fm");

        if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
        {
            embed.AddField("Note from .fmbot staff:", $"*\"{PublicProperties.IssuesReason}\"*");
        }

        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login")
            ? $"</login:{PublicProperties.SlashCommands["login"]}>"
            : "`/login`";

        switch (responseStatus)
        {
            case ResponseStatus.Failure:
                embed.WithDescription(
                    "Can't retrieve data because Last.fm returned an error. Please try again later. \n" +
                    $"Please note that .fmbot isn't affiliated with Last.fm.");
                break;
            case ResponseStatus.LoginRequired:
                embed.WithDescription(
                    "Can't retrieve data because your recent tracks are marked as private in your [Last.fm privacy settings](https://www.last.fm/settings/privacy).\n\n" +
                    $"You can either change this setting or authorize .fmbot to access your private scrobbles with {loginCommand}.\n\n" +
                    $"Please note that .fmbot isn't affiliated with Last.fm.");
                break;
            case ResponseStatus.BadAuth:
                embed.WithDescription(
                    "Can't retrieve data because your Last.fm session is expired, invalid or Last.fm is having issues.\n" +
                    $"Please try a re-login to the bot with {loginCommand}.");
                break;
            case ResponseStatus.SessionExpired:
                embed.WithDescription("Can't retrieve data because your Last.fm session is expired or invalid.\n" +
                                      $"Please re-login to the bot with {loginCommand}.");
                break;
            case ResponseStatus.MissingParameters:
                if (expectedResultType != null)
                {
                    embed.Title = null;
                    embed.WithDescription(
                        $"Sorry, Last.fm did not return an {expectedResultType} for the name you searched for.");
                }
                else if (message.Equals("Not found"))
                {
                    embed.WithDescription(
                        $"Last.fm did not return a result. Maybe there are no results or you're looking for a user that recently changed their username (in which case they should re-run /login).");
                }
                else
                {
                    goto default;
                }

                break;
            default:
                embed.WithDescription(message ?? "Unknown error");
                break;
        }

        if (responseStatus != null)
        {
            embed.WithFooter($"Last.fm error code: {responseStatus}");
        }

        embed.WithColor(DiscordConstants.WarningColorOrange);
        Log.Information(
            "Last.fm returned error: {message} | {responseStatus} | {discordUserName} / {discordUserId} | {messageContent}",
            message, responseStatus, contextUser?.Username, contextUser?.Id, commandContent);
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
        string lastFmUserName, ICommandContext context)
    {
        var embed = new EmbedBuilder();
        if (!recentScrobbles.Success || recentScrobbles.Content == null)
        {
            embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, context.Message.Content, context.User);
            context.LogCommandUsed(CommandResponse.LastFmError);
            await context.Channel.SendMessageAsync("", false, embed.Build());
            return true;
        }

        if (!recentScrobbles.Content.RecentTracks.Any())
        {
            embed.NoScrobblesFoundErrorResponse(lastFmUserName);
            context.LogCommandUsed(CommandResponse.NoScrobbles);
            await context.Channel.SendMessageAsync("", false, embed.Build(),
                components: NoScrobblesFoundComponents().Build());
            return true;
        }

        return false;
    }

    public static EmbedBuilder RecentScrobbleCallFailedBuilder(Response<RecentTrackList> recentScrobbles,
        string lastFmUserName)
    {
        var embed = new EmbedBuilder();
        if (recentScrobbles.Content?.RecentTracks == null || !recentScrobbles.Success)
        {
            embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, null);
            return embed;
        }

        if (!recentScrobbles.Content.RecentTracks.Any())
        {
            embed.NoScrobblesFoundErrorResponse(lastFmUserName);
            return embed;
        }

        return null;
    }

    public static ResponseModel RecentScrobbleCallFailedResponse(Response<RecentTrackList> recentScrobbles,
        string lastFmUserName)
    {
        var errorResponse = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        if (recentScrobbles.Content?.RecentTracks == null || !recentScrobbles.Success)
        {
            errorResponse.Embed.ErrorResponse(recentScrobbles.Error, recentScrobbles.Message, null);
            errorResponse.CommandResponse = CommandResponse.LastFmError;
            return errorResponse;
        }

        if (!recentScrobbles.Content.RecentTracks.Any())
        {
            errorResponse.Embed.NoScrobblesFoundErrorResponse(lastFmUserName);
            errorResponse.CommandResponse = CommandResponse.NoScrobbles;
            errorResponse.Components = NoScrobblesFoundComponents();
            return errorResponse;
        }

        return null;
    }

    public static (EmbedBuilder embedBuilder, bool showPurchaseButtons) HelpResponse(EmbedBuilder embed,
        CommandInfo commandInfo, string prfx, string userName)
    {
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithTitle($"Information about '{prfx}{commandInfo.Name}' for {userName}");

        if (!string.IsNullOrWhiteSpace(commandInfo.Summary))
        {
            embed.WithDescription(commandInfo.Summary.Replace("{{prfx}}", prfx));
        }

        var options = commandInfo.Attributes.OfType<OptionsAttribute>()
            .FirstOrDefault();
        if (options?.Options != null && options.Options.Any())
        {
            var optionsString = new StringBuilder();
            foreach (var option in options.Options)
            {
                optionsString.AppendLine($"- {option}");
            }

            embed.AddField("Options", optionsString.ToString());
        }

        var examples = commandInfo.Attributes.OfType<ExamplesAttribute>()
            .FirstOrDefault();
        if (examples?.Examples != null && examples.Examples.Any())
        {
            var examplesString = new StringBuilder();
            foreach (var example in examples.Examples)
            {
                examplesString.AppendLine($"`{prfx}{example}`");
            }

            embed.AddField("Examples", examplesString.ToString());
        }

        var aliases = commandInfo.Aliases.Where(a => a != commandInfo.Name).ToList();
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

            embed.AddField("Aliases", aliasesString.ToString());
        }

        var showPurchaseButtons = false;
        var supporterEnhanced = commandInfo.Attributes.OfType<SupporterEnhancedAttribute>()
            .FirstOrDefault();
        if (supporterEnhanced?.Explainer != null)
        {
            showPurchaseButtons = true;
            embed.AddField("⭐ Enhanced for .fmbot supporters", supporterEnhanced.Explainer);
        }

        var supporterExclusive = commandInfo.Attributes.OfType<SupporterExclusiveAttribute>()
            .FirstOrDefault();
        if (supporterExclusive?.Explainer != null)
        {
            showPurchaseButtons = true;
            embed.AddField("⭐ Exclusive for .fmbot supporters", supporterExclusive.Explainer);
        }

        return (embed, showPurchaseButtons);
    }

    public static ComponentBuilder PurchaseButtons(CommandInfo commandInfo)
    {
        return new ComponentBuilder()
            .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Primary,
                customId: InteractionConstants.SupporterLinks.GeneratePurchaseButtons(source: $"help-{commandInfo.Name}"));
    }
}
