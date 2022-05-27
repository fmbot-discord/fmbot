using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class StaticSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    public StaticSlashCommands(UserService userService)
    {
        this._userService = userService;
    }

    [SlashCommand("outofsync", "Shows info if your Last.fm isn't up to date with Spotify")]
    public async Task OutOfSyncAsync([Summary("private", "Show info privately?")]bool privateResponse = true)
    {
        var embed = new EmbedBuilder();

        embed.WithColor(DiscordConstants.InformationColorBlue);

        embed.WithTitle("Using Spotify and tracking is out of sync?");
        var embedDescription = new StringBuilder();

        embedDescription.AppendLine(".fmbot uses your Last.fm account for knowing what you listen to. ");
        embedDescription.AppendLine($"Unfortunately, Last.fm and Spotify sometimes have issues keeping up to date with your current song, which can cause `/fm` and other commands to lag behind the song you're currently listening to.");
        embedDescription.AppendLine();
        embedDescription.Append("First, **.fmbot is not affiliated with Last.fm**. Your music is tracked by Last.fm, and not by .fmbot. ");
        embedDescription.AppendLine("This means that this is a Last.fm issue and **not an .fmbot issue**. We can't fix it for you, but we can give you some tips that worked for others.");
        embedDescription.AppendLine();
        embedDescription.AppendLine("Some things you can try that usually work:");
        embedDescription.AppendLine(" - Restarting your Spotify application");
        embedDescription.AppendLine(" - Disconnecting and **reconnecting Spotify in [your Last.fm settings](https://www.last.fm/settings/applications)**");
        embedDescription.AppendLine();
        embedDescription.AppendLine("If the two options above don't work, check out **[the complete guide for this issue on the Last.fm support forums](https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184)**.");

        embed.WithDescription(embedDescription.ToString());

        var components = new ComponentBuilder()
            .WithButton("Last.fm settings", style: ButtonStyle.Link, url: "https://www.last.fm/settings/applications")
            .WithButton("Full guide", style: ButtonStyle.Link, url: "https://support.last.fm/t/spotify-has-stopped-scrobbling-what-can-i-do/3184");

        await RespondAsync(null, new[] { embed.Build() }, ephemeral: privateResponse);
        this.Context.LogCommandUsed();
    }
}