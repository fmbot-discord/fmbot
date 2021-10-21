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

        Task<GuildUser> GetOrAddUserToGuild(Guild guild, IGuildUser discordGuildUser, User user);

        Task UpdateGuildUser(IGuildUser discordGuildUser, int userId, Guild guildId);

        Task UpdateGuildUserEvent(IGuildUser discordGuildUser);

        Task RemoveUserFromGuild(ulong discordUserId, ulong discordGuildId);

        Task<(int, int?)> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers);

        Task<IReadOnlyList<User>> GetUsersToFullyUpdate(IReadOnlyCollection<IGuildUser> discordGuildUsers);

        Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers);

        Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed);
    }
}
