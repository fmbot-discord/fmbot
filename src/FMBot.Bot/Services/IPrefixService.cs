using System.Threading.Tasks;

namespace FMBot.Bot.Services
{
    public interface IPrefixService
    {
        /// <inheritdoc />
        void StorePrefix(string prefix, ulong key);

        /// <inheritdoc />
        string GetPrefix(ulong? key);

        /// <inheritdoc />
        void RemovePrefix(ulong key);

        /// <inheritdoc />
        Task LoadAllPrefixes();
    }
}
