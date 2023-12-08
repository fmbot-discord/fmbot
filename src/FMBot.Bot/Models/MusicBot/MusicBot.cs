using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Services.Guild;

namespace FMBot.Bot.Models.MusicBot;

public abstract class MusicBot
{
    public string Name { get; }
    public bool PossiblyIncludesLinks { get; }

    public bool SkipUploaderName { get; }

    public static IEnumerable<MusicBot> SupportedBots { get; } = new List<MusicBot>
    {
        new JockieMusicBot(),
        new CakeyBotMusicBot(),
        new SoundCloudMusicBot(),
        new RedMusicBot()
    };

    protected MusicBot(string name, bool possiblyIncludesLinks = true, bool skipUploaderName = false)
    {
        this.Name = name;
        this.PossiblyIncludesLinks = possiblyIncludesLinks;
        this.SkipUploaderName = skipUploaderName;
    }

    public virtual async Task<bool> IsAuthor(SocketUser user, ICommandContext context, GuildService guildService)
    {
        return user?.Username?.StartsWith(this.Name, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public abstract bool ShouldIgnoreMessage(IUserMessage msg);

    public abstract string GetTrackQuery(IUserMessage msg);
}
