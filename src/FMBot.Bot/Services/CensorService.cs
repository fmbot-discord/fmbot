using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class CensorService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public CensorService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<bool> IsSafeForChannel(ICommandContext context, string albumName, string artistName, string url, EmbedBuilder embed = null)
        {
            if (!await AlbumIsSafe(albumName, artistName))
            {
                if (!await AlbumIsAllowedInNsfw(albumName, artistName))
                {
                    embed?.WithDescription("Sorry, this album or artist can't be posted due to discord ToS.\n" +
                                           $"You can view the [album cover here]({url}).");
                    return false;
                }
                if (context.Guild != null && !((SocketTextChannel)context.Channel).IsNsfw)
                {
                    embed?.WithDescription("Sorry, this album cover can only be posted in NSFW channels.\n" +
                                                $"You can mark this channel as NSFW or view the [album cover here]({url}).");
                    return false; 
                }

                return true;
            }

            return true;
        }

        public async Task<bool> AlbumIsSafe(string albumName, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();

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
                    .Where(w => !w.Artist && w.AlbumName != null)
                    .Select(s => s.AlbumName.ToLower())
                    .Contains(albumName.ToLower()))
            {
                return false;
            }

            return true;
        }
        
        public async Task<bool> AlbumIsAllowedInNsfw(string albumName, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();

            if (db.CensoredMusic
                    .AsQueryable()
                    .Where(w => w.Artist && w.SafeForCommands)
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower()))
            {
                return true;
            }

            if (db.CensoredMusic
                    .AsQueryable()
                    .Where(w => w.SafeForCommands)
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower())
                &&
                db.CensoredMusic
                    .AsQueryable()
                    .Where(w => !w.Artist && w.AlbumName != null && w.SafeForCommands)
                    .Select(s => s.AlbumName.ToLower())
                    .Contains(albumName.ToLower()))
            {
                return true;
            }

            return false;
        }

        public async Task AddCensoredAlbum(string albumName, string artistName)
        {
            await using var db = this._contextFactory.CreateDbContext();

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
            await using var db = this._contextFactory.CreateDbContext();

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
