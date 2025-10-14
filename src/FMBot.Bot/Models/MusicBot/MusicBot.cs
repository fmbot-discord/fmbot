using System;
using System.Collections.Generic;
using NetCord.Gateway;

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
        new NeroMusicBot(),
        new TempoMusicBot(),
        new BleedMusicBot(),
        new UzoxMusicBot(),
        new ListenMusicBot(),
        new FlaviMusicBot(),
        new MakiMusicBot(),
        new EaraMusicBot()
    };

    protected MusicBot(string name, bool possiblyIncludesLinks = true, bool skipUploaderName = false, bool trackNameFirst = false)
    {
        this.Name = name;
        this.PossiblyIncludesLinks = possiblyIncludesLinks;
        this.SkipUploaderName = skipUploaderName;
        this.TrackNameFirst = trackNameFirst;
    }

    public bool IsAuthor(NetCord.User user)
    {
        return user?.Username?.StartsWith(this.Name, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public abstract bool ShouldIgnoreMessage(Message msg);

    public abstract string GetTrackQuery(Message msg);
}
