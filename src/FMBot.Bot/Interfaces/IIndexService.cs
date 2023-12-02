using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces;

public interface IIndexService
{
    void AddUsersToIndexQueue(IReadOnlyList<User> users);

    Task<IndexedUserStats> IndexUser(User user);

    Task<IndexedUserStats> ModularUpdate(User user, UpdateType updateType);

    bool IndexStarted(int userId);

    Task RecalculateTopLists(User user);

    Task<GuildUser> GetOrAddUserToGuild(
        IDictionary<int, FullGuildUser> guildUsers,
        Guild guild,
        IGuildUser discordGuildUser,
        User user);

    Task AddGuildUserToDatabase(GuildUser guildUserToAdd);

    Task UpdateGuildUser(IDictionary<int, FullGuildUser> fullGuildUsers, IGuildUser discordGuildUser, int userId,
        Guild guildId);

    Task AddOrUpdateGuildUser(IGuildUser discordGuildUser, bool checkIfRegistered = true);

    Task RemoveUserFromGuild(ulong discordUserId, ulong discordGuildId);

    Task<DateTime?> AddUserRegisteredLfmDate(int userId);

    Task<int> StoreGuildUsers(IGuild discordGuild, IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<IReadOnlyList<User>> GetUsersToFullyUpdate(IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<int> GetIndexedUsersCount(IReadOnlyCollection<IGuildUser> discordGuildUsers);

    Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastIndexed);
}
