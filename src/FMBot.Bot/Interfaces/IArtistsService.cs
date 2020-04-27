using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IArtistsService
    {
        Task<IList<ArtistWithUser>> GetIndexedUsersForArtist(IReadOnlyCollection<IGuildUser> guildUsers, string artistName);
        Task<int> GetArtistListenerCountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
        Task<int> GetArtistPlayCountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
        Task<double> GetArtistAverageListenerPlaycountForServer(IEnumerable<IGuildUser> guildUsers, string artistName);
    }
}
