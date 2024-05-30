using System;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

namespace FMBot.Bot.Models.MusicBot;

public abstract class MusicBot
{
    public string Name { get; }
    public bool PossiblyIncludesLinks { get; }

    public bool SkipUploaderName { get; }

    public bool TrackNameFirst { get; }

    public static IEnumerable<MusicBot> SupportedBots { get; } = new List<MusicBot>
    {
        new JockieMusicBot(),
        new CakeyBotMusicBot(),
        new SoundCloudMusicBot(),
        new GreenBotMusicBot(),
        new BettyMusicBot(),
        new TempoMusicBot(),
        new BleedMusicBot(),
        new UzoxMusicBot(),
        new ListenMusicBot()
    };

    protected MusicBot(string name, bool possiblyIncludesLinks = true, bool skipUploaderName = false, bool trackNameFirst = false)
    {
        this.Name = name;
        this.PossiblyIncludesLinks = possiblyIncludesLinks;
        this.SkipUploaderName = skipUploaderName;
        this.TrackNameFirst = trackNameFirst;
    }

    public bool IsAuthor(SocketUser user)
    {
        return user?.Username?.StartsWith(this.Name, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public abstract bool ShouldIgnoreMessage(IUserMessage msg);

    public abstract string GetTrackQuery(IUserMessage msg);
}
