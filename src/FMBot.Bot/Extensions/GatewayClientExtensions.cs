using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Extensions;

public static class GatewayClientExtensions
{
    extension(ShardedGatewayClient client)
    {
        public CurrentUser GetCurrentUser()
        {
            return client.FirstOrDefault()?.Cache.User;
        }

        public ulong? GetCurrentUserId()
        {
            return client.GetCurrentUser()?.Id;
        }
    }

    public static async Task<User> GetUserAsync(this GatewayClient client, ulong userId, ulong? guildId = null)
    {
        if (guildId.HasValue)
        {
            var guild = client.Cache.Guilds.GetValueOrDefault(guildId.Value);
            if (guild?.Users?.TryGetValue(userId, out var cachedUser) == true)
            {
                return cachedUser;
            }
        }

        return await client.Rest.GetUserAsync(userId);
    }

    public static Task<User> GetUserAsync(this ApplicationCommandContext context, ulong userId)
    {
        return context.Client.GetUserAsync(userId, context.Guild?.Id);
    }

    public static Task<User> GetUserAsync(this CommandContext context, ulong userId)
    {
        return context.Client.GetUserAsync(userId, context.Guild?.Id);
    }

    public static Task<User> GetUserAsync(this ComponentInteractionContext context, ulong userId)
    {
        return context.Client.GetUserAsync(userId, context.Guild?.Id);
    }

    public static async Task<RestGuild> GetGuildAsync(this GatewayClient client, ulong guildId)
    {
        if (client.Cache.Guilds.TryGetValue(guildId, out var cachedGuild))
        {
            return cachedGuild;
        }

        return await client.Rest.GetGuildAsync(guildId);
    }

    extension(ShardedGatewayClient client)
    {
        public async Task<RestGuild> GetGuildAsync(ulong guildId)
        {
            foreach (var shard in client)
            {
                if (shard.Cache.Guilds.TryGetValue(guildId, out var cachedGuild))
                {
                    return cachedGuild;
                }
            }

            return await client.Rest.GetGuildAsync(guildId);
        }

        public async Task<User> GetUserAsync(ulong userId)
        {
            foreach (var shard in client)
            {
                foreach (var guild in shard.Cache.Guilds.Values)
                {
                    if (guild.Users?.TryGetValue(userId, out var cachedUser) == true)
                    {
                        return cachedUser;
                    }
                }
            }

            return await client.Rest.GetUserAsync(userId);
        }
    }
}
