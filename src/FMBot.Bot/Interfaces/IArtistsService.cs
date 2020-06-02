using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IArtistsService
    {
        Task<IList<ArtistWithUser>> GetIndexedUsersForArtist(ICommandContext context, IReadOnlyList<User> guildUsers,
            string artistName);
        Task<int> GetArtistListenerCountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
        Task<int> GetArtistPlayCountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
        Task<double> GetArtistAverageListenerPlaycountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
        Task<IList<ListArtist>> GetTopArtistsForGuild(IReadOnlyCollection<IGuildUser> guildUsers);
    }
}
