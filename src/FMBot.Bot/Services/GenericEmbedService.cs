using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.LastFM.Domain.Types;
using Serilog;

namespace FMBot.Bot.Services;

public static class GenericEmbedService
{
    public static void UsernameNotSetErrorResponse(this EmbedBuilder embed, string prfx, string name)
    {
        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login") ? $"</login:{PublicProperties.SlashCommands["login"]}>" : "`/login`";
        embed.WithDescription($"Hi {name}, welcome to .fmbot. \n" +
                              $"To use this bot you first need to add your Last.fm account.\n\n" +
                              $"Please use the {loginCommand} command. The bot will then send you a link so you can connect your Last.fm account.");

        embed.WithUrl($"{Constants.DocsUrl}/commands/");

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    public static void RateLimitedResponse(this EmbedBuilder embed)
    {
        embed.WithDescription($"Sorry, you're being ratelimited. Please slow down a bit with using commands.");

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
        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login") ? $"</login:{PublicProperties.SlashCommands["login"]}>" : "`/login`";
        embed.WithDescription("While you have set your username, you haven't connected .fmbot to your Last.fm account yet, which is required for the command you're trying to use.\n" +
                              $"Please use the {loginCommand} command to receive a link to connect your Last.fm account.");

        embed.WithUrl($"{Constants.DocsUrl}/commands/");

        embed.WithColor(DiscordConstants.WarningColorOrange);
    }

    private static void NoScrobblesFoundErrorResponse(this EmbedBuilder embed, string userName)
    {
        embed.WithDescription($"The Last.fm user `{userName}` has no scrobbles/artists/albums/tracks on [their Last.fm profile]({Constants.LastFMUserUrl}{userName}) yet.\n\n" +
                              $"Just signed up for last.fm and added your account in the bot? Make sure you properly [track your plays](https://www.last.fm/about/trackmymusic) " +
                              $"and your [Last.fm profile]({Constants.LastFMUserUrl}{userName}) is showing the music that you're listening to. \n" +
                              $"Usually it takes a few minutes before Last.fm starts working with Spotify after connecting.\n\n" +
                              $"Please note that .fmbot is **not** affiliated with Last.fm or Spotify.");

        embed.WithColor(DiscordConstants.InformationColorBlue);
    }

    public static void ErrorResponse(this EmbedBuilder embed, ResponseStatus? responseStatus, string message, string commandContent, IUser contextUser = null, string expectedResultType = null)
    {
        embed.WithTitle("Problem while contacting Last.fm");

        var issues = "";
        if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
        {
            issues = "\n\n" +
                     "Note:\n" +
                     $"*\"{PublicProperties.IssuesReason}\"*";
        }

        var loginCommand = PublicProperties.SlashCommands.ContainsKey("login") ? $"</login:{PublicProperties.SlashCommands["login"]}>" : "`/login`";

        switch (responseStatus)
        {
            case ResponseStatus.Failure:
                embed.WithDescription("Can't retrieve data because Last.fm returned an error. Please try again later. \n" +
                                      $"Please note that .fmbot isn't affiliated with Last.fm.{issues}");
                break;
            case ResponseStatus.LoginRequired:
                embed.WithDescription("Can't retrieve data because your recent tracks are marked as private in your [Last.fm privacy settings](https://www.last.fm/settings/privacy).\n\n" +
                                      $"You can either change this setting or authorize .fmbot to access your private scrobbles with {loginCommand}.\n\n" +
                                      $"Please note that .fmbot isn't affiliated with Last.fm.{issues}");
                break;
            case ResponseStatus.BadAuth:
                embed.WithDescription("Can't retrieve data because your Last.fm session is expired, invalid or Last.fm is having issues.\n" +
                                      $"Please try a re-login to the bot with {loginCommand}.{issues}");
                break;
            case ResponseStatus.SessionExpired:
                embed.WithDescription("Can't retrieve data because your Last.fm session is expired or invalid.\n" +
                                      $"Please re-login to the bot with {loginCommand}.{issues}");
                break;
            case ResponseStatus.MissingParameters:
                if (expectedResultType != null)
                {
                    embed.Title = null;
                    embed.WithDescription($"Sorry, Last.fm did not return an {expectedResultType} for the name you searched for.");
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
        Log.Information("Last.fm returned error: {message} | {responseStatus} | {discordUserName} / {discordUserId} | {messageContent}", message, responseStatus, contextUser?.Username, contextUser?.Id, commandContent);
    }

    public static bool RecentScrobbleCallFailed(Response<RecentTrackList> recentScrobbles)
    {
        if (!recentScrobbles.Success || recentScrobbles.Content == null || !recentScrobbles.Content.RecentTracks.Any())
        {
            return true;
        }

        return false;
    }

    public static async Task<bool> RecentScrobbleCallFailedReply(Response<RecentTrackList> recentScrobbles, string lastFmUserName, ICommandContext context)
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
            await context.Channel.SendMessageAsync("", false, embed.Build());
            return true;
        }

        return false;
    }

    public static EmbedBuilder RecentScrobbleCallFailedBuilder(Response<RecentTrackList> recentScrobbles, string lastFmUserName)
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

    public static void HelpResponse(this EmbedBuilder embed, CommandInfo commandInfo, string prfx, string userName)
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
    }
}
