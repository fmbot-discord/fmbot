using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Configurations;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Services
{
    public class CensorService
    {
        public async Task<bool> AlbumIsSafe(string albumName, string artistName)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

            if (db.CensoredMusic
                    .AsQueryable()
                    .Where(w => w.Artist)
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower()))
            {
                return false;
            }

            if (db.CensoredMusic
                    .AsQueryable()
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower())
                &&
                db.CensoredMusic
                    .AsQueryable()
                    .Select(s => s.AlbumName.ToLower())
                    .Contains(albumName.ToLower()))
            {
                return false;
            }

            return true;
        }

        public async Task AddCensoredAlbum(string albumName, string artistName)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

            await db.CensoredMusic.AddAsync(new CensoredMusic
            {
                AlbumName = albumName,
                ArtistName = artistName,
                Artist = false,
                SafeForCommands = false,
                SafeForFeatured = false
            });

            await db.SaveChangesAsync();
        }
        public async Task AddCensoredArtist(string artistName)
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

            await db.CensoredMusic.AddAsync(new CensoredMusic
            {
                ArtistName = artistName,
                Artist = true,
                SafeForCommands = false,
                SafeForFeatured = false
            });

            await db.SaveChangesAsync();
        }
    }
}
