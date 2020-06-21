using System.Collections.Generic;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IArtistsService
    {
        Task<IList<ArtistWithUser>> GetIndexedUsersForArtist(ICommandContext context, IReadOnlyList<User> guildUsers,
            string artistName);
        Task<int> GetArtistListenerCountForServer(IReadOnlyList<User> guildUsers, string artistName);
        Task<int> GetArtistPlayCountForServer(IReadOnlyList<User> guildUsers, string artistName);
        Task<double> GetArtistAverageListenerPlaycountForServer(IReadOnlyList<User> guildUsers, string artistName);
        Task<IList<ListArtist>> GetTopArtistsForGuild(IReadOnlyList<User> guildUsers);
    }
}
