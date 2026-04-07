using System;
using System.Linq;
using NetCord;
using NetCord.Gateway;

namespace FMBot.Bot.Models.MusicBot;

internal class GreenBotMusicBot : MusicBot
{
    private const string NowPlaying = "Now Playing";
    public GreenBotMusicBot() : base("Green-bot", false)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        if (msg.Components.Count == 0)
        {
            return true;
        }

        var container = msg.Components.OfType<ComponentContainer>().FirstOrDefault();
        if (container is null)
        {
            return true;
        }

        var textDisplay = container.Components.OfType<TextDisplay>().FirstOrDefault();
        return textDisplay?.Content?.Contains(NowPlaying) != true;
    }

    /*
     * TextDisplay[0]: "### Now Playing"
     * ComponentSeparator[1]
     * TextDisplay[2]: "[**Breathe**](http://www.tidal.com/track/17981365)\nBy **The Prodigy**\n\nRequested by ..."
     */
    public override string GetTrackQuery(Message msg)
    {
        var container = msg.Components.OfType<ComponentContainer>().FirstOrDefault();
        if (container is null)
        {
            return string.Empty;
        }

        var trackText = container.Components.OfType<TextDisplay>()
            .FirstOrDefault(t => t.Content?.Contains("\nBy ") == true);

        if (trackText?.Content == null)
        {
            return string.Empty;
        }

        return ParseComponentsFormat(trackText.Content);
    }

    internal static string ParseComponentsFormat(string content)
    {
        var lines = content.Split('\n');
        if (lines.Length < 2)
        {
            return string.Empty;
        }

        // Track from "[**Breathe**](url)"
        var trackLine = lines[0];
        if (!trackLine.StartsWith('['))
        {
            return string.Empty;
        }

        var track = trackLine.Split('[', ']')[1].Replace("**", "");

        // Artist from "By **The Prodigy**"
        var artistLine = lines[1];
        if (!artistLine.StartsWith("By "))
        {
            return string.Empty;
        }

        var artist = artistLine[3..].Replace("**", "");

        return $"{artist} - {track}";
    }
}
