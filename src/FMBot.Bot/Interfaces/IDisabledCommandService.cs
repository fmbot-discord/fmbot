using System.Threading.Tasks;

namespace FMBot.Bot.Interfaces
{
    public interface IDisabledCommandService
    {
        /// <inheritdoc />
        void StoreDisabledCommands(string[] commands, ulong key);

        /// <inheritdoc />
        string[] GetDisabledCommands(ulong? key);

        /// <inheritdoc />
        void RemoveDisabledCommands(ulong key);

        /// <inheritdoc />
        Task LoadAllDisabledCommands();
    }
}
