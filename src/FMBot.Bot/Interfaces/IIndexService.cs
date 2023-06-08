using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces;

public interface IIndexService
{
    void AddUsersToIndexQueue(IReadOnlyList<User> users);

    Task<IndexedUserStats> IndexUser(User user);

    Task<GuildUser> GetOrAddUserToGuild(
        IDictionary<int, FullGuildUser> guildUsers,
        Guild guild,
        IGuildUser discordGuildUser,
        User user);

    Task AddGuildUserToDatabase(GuildUser guildUserToAdd);

    Task UpdateGuildUser(IGuildUser discordGuildUser, int userId, Guild guildId);

    Task AddOrUpdateGuildUser(IGuildUser discordGuildUser);

    Task RemoveUserFromGuild(ulong discordUserId, ulong discordGuildId);

    Task<DateTime?> AddUserRegisteredLfmDate(int userId);

    Task<int> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<IReadOnlyList<User>> GetUsersToFullyUpdate(IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed);
}
