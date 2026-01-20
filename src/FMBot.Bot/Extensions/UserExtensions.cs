using NetCord;

namespace FMBot.Bot.Extensions;

public static class UserExtensions
{
    public static string GetDisplayName(this User user)
    {
        return user.GlobalName ?? user.Username;
    }

    public static string GetDisplayName(this PartialGuildUser guildUser)
    {
        return guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username;
    }

    public static string GetDisplayName(this GuildUser guildUser)
    {
        return guildUser.Nickname ?? guildUser.GlobalName ?? guildUser.Username;
    }
}
