using System.Threading.Tasks;

namespace FMBot.Bot.Interfaces
{
    public interface IGuildDisabledCommandService
    {
        void StoreDisabledCommands(string[] commands, ulong key);

        string[] GetDisabledCommands(ulong? key);

        void RemoveDisabledCommands(ulong key);

        Task LoadAllDisabledCommands();

        Task ReloadDisabledCommands(ulong discordGuildId);
    }
}
