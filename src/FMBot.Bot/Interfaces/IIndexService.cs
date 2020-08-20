using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IIndexService
    {
        void IndexGuild(IReadOnlyList<User> users);

        Task UpdateUser(User user);

        Task StoreGuildUsers(IGuild guild, IReadOnlyCollection<IGuildUser> guildUsers);

        Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> guildUsers);

        Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> guildUsers);
    }
}
