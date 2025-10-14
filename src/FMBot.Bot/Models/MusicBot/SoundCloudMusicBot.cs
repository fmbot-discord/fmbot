using System.Linq;

using Discord.WebSocket;
using NetCord.Gateway;

namespace FMBot.Bot.Models.MusicBot;

internal class SoundCloudMusicBot : MusicBot
{
    public SoundCloudMusicBot() : base("SoundCloud", true, true)
    {
    }

    public override bool ShouldIgnoreMessage(Message msg)
    {
        // Bot sends only single embed per request
        if (msg.Embeds.Count != 1)
        {
            return true;
        }

        var targetEmbed = msg.Embeds.First();

        if (targetEmbed.Title == null ||
            !targetEmbed.Title.Contains("Now Playing") ||
            string.IsNullOrEmpty(targetEmbed.Description))
        {
            return true;
        }

        return false;
    }

    public override string GetTrackQuery(Message msg)
    {
        return msg.Embeds.First().Description;
    }
}
