using System.Threading.Tasks;

namespace FMBot.Bot.Interfaces;

public interface IChannelDisabledCommandService
{
    void StoreDisabledCommands(string[] commands, ulong key);

    string[] GetDisabledCommands(ulong? key);

    void RemoveDisabledCommands(ulong key);

    Task LoadAllDisabledCommands();

    Task RemoveDisabledCommandsForGuild(ulong discordGuildId);

    Task ReloadDisabledCommands(ulong discordGuildId);
}
