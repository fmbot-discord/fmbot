using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IGuildService
    {
        bool CheckIfDM(ICommandContext context);
        Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, ulong id);
        Task<GuildPermissions> CheckSufficientPermissionsAsync(ICommandContext context);
        Task<IGuildUser> FindUserFromGuildAsync(ICommandContext context, string searchValue);
        Task<List<UserExportModel>> FindAllUsersFromGuildAsync(ICommandContext context);
        Task ChangeGuildSettingAsync(IGuild guild, ChartTimePeriod chartTimePeriod, FmEmbedType fmEmbedType);
        Task SetGuildReactionsAsync(IGuild guild, string[] reactions);
        Task SetGuildPrefixAsync(IGuild guild, string prefix);
        Task<string[]> GetDisabledCommandsForGuild(IGuild guild);
        Task<string[]> AddDisabledCommandAsync(IGuild guild, string command);
        Task<string[]> RemoveDisabledCommandAsync(IGuild guild, string command);
        Task<DateTime?> GetGuildIndexTimestampAsync(IGuild guild);
        Task UpdateGuildIndexTimestampAsync(IGuild guild, DateTime? timestamp = null);
        bool ValidateReactions(string[] emoteString);
        Task AddReactionsAsync(IUserMessage message, IGuild guild);
        Task AddGuildAsync(SocketGuild guild);
        Task<bool> GuildExistsAsync(SocketGuild guild);
        Task<int> GetTotalGuildCountAsync();
    }
}
