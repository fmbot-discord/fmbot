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

        Task<GuildUser> GetOrAddUserToGuild(Guild guild, IGuildUser guildUser, User user);

        Task UpdateUserName(GuildUser guildUser, IGuildUser discordGuildUser);

        Task UpdateUserNameWithoutGuildUser(IGuildUser discordGuildUser, User user);

        Task RemoveUserFromGuild(SocketGuildUser user);

        Task StoreGuildUsers(IGuild guild, IReadOnlyCollection<IGuildUser> guildUsers);

        Task<IReadOnlyList<User>> GetUsersToIndex(IReadOnlyCollection<IGuildUser> guildUsers);

        Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> guildUsers);

        Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed);
    }
}
