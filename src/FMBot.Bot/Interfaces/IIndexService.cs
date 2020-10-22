using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IIndexService
    {
        void AddUsersToIndexQueue(IReadOnlyList<User> users);

        Task IndexUser(User user);

        Task AddUserToGuild(IGuild guild, User user);

        Task RemoveUserFromGuild(SocketGuildUser user);

        Task StoreGuildUsers(IGuild guild, IReadOnlyCollection<IGuildUser> guildUsers);

        Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> guildUsers);

        Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> guildUsers);

        Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed);
    }
}
