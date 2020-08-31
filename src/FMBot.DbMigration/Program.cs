using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.DbMigration.OldDatabase.Entities;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Friend = FMBot.Persistence.Domain.Models;

namespace FMBot.DbMigration
{
    static class Program
    {
        private static readonly FMBotDbContext NewDatabaseContext = new FMBotDbContext("Host=localhost;Port=5433;Username=postgres;Password=password;Database=fmbot;Command Timeout=15;Timeout=30;Persist Security Info=True");
        private static readonly LocalDbDbContext OldDatabaseContext = new LocalDbDbContext();

        private static async Task Main()
        {
            Console.WriteLine("Welcome to the migration tool for migrating the .fmbot database tool from localdb to PostgreSQL \n" +
                              "This tool requires you to have ran .fmbot with PostgreSQL at least once. \n\n" +
                              "Options: \n" +
                              "1. Migrate all data to new db \n" +
                              "2. Migrate only users and friends \n" +
                              "3. Migrate only guilds \n" +
                              "4. Clear new database \n" +
                              "5. Fix key values (use this if you have issues inserting new records) \n" +
                              "6. Migrate artists aliases to seperate records \n" +
                              "Select an option to continue...");

            var key = Console.ReadKey().Key.ToString();
            var saveToDatabase = true;

            if (key == "D1")
            {
                await CopyUsers();
                await CopyGuilds();
            }
            else if (key == "D2")
            {
                await CopyUsers();
            }
            else if (key == "D3")
            {
                await CopyGuilds();
            }
            else if (key == "D4")
            {
                await ClearNewDatabase();
                saveToDatabase = false;
            }
            else if (key == "D6")
            {
                await MigrateArtistsToRecords();
                saveToDatabase = true;
            }
            else if (key == "D5")
            {
                await FixKeyValues();
                await Task.Delay(10000);
                Environment.Exit(1);
            }
            else
            {
                Console.WriteLine("\nKey not recognized");
                await Task.Delay(10000);
                Environment.Exit(1);
            }

            if (saveToDatabase)
            {
                Console.WriteLine($"Saving changes to database...");
                try
                {
                    await NewDatabaseContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Something went wrong!");
                    Console.WriteLine(e);
                    if (e.InnerException != null)
                    {
                        Console.WriteLine(e.InnerException);
                    }
                    await Task.Delay(10000);
                    throw;
                }
            }

            await FixKeyValues();

            Console.WriteLine("Done! Press any key to quit program.");
            Console.ReadKey();
        }

        private static async Task CopyUsers()
        {
            Console.WriteLine("Starting user migration...");

            Console.WriteLine("Retrieving users from old database");
            var oldUsers = await OldDatabaseContext.Users
                .Include(i => i.FriendsUser)
                .ToListAsync();
            Console.WriteLine($"Old database has {oldUsers.Count} users");

            Console.WriteLine("Retrieving users from new database");
            var newUsers = await NewDatabaseContext.Users.ToListAsync();
            Console.WriteLine($"New database has {newUsers.Count} users");

            var usersToInsert = oldUsers
                .Where(w => !w.DiscordUserID.Contains(newUsers.Select(s => s.DiscordUserId).ToString()) &&
                            !w.UserID.ToString().Contains(newUsers.Select(s => s.UserId).ToString()))
                .ToList();

            Console.WriteLine($"Inserting {usersToInsert.Count} users that do not yet exist into the new database");
            //await NewDatabaseContext.Users.AddRangeAsync(
            //    usersToInsert
            //        .Select(s => new Friend.User
            //        {
            //            Blacklisted = s.Blacklisted,
            //            ChartTimePeriod = s.ChartTimePeriod,
            //            FmEmbedType = s.FmEmbedType,
            //            DiscordUserId = ulong.Parse(s.DiscordUserID),
            //            Featured = s.Featured,
            //            LastGeneratedChartDateTimeUtc = s.LastGeneratedChartDateTimeUtc,
            //            TitlesEnabled = s.TitlesEnabled,
            //            UserNameLastFM = s.UserNameLastFM,
            //            UserType = s.UserType,
            //            UserId = s.UserID,
            //            Friends = s.FriendsUser.Select(se => new Friend.Friend
            //            {
            //                UserId = se.UserID,
            //                LastFMUserName = se.LastFMUserName,
            //                FriendUserId = se.FriendUserID,
            //            }).ToList()
            //        }));

            Console.WriteLine("Users have been inserted.");
        }

        private static async Task CopyGuilds()
        {
            Console.WriteLine("Starting guild migration...");

            Console.WriteLine("Retrieving guilds from old database");
            var oldGuilds = await OldDatabaseContext.Guilds.ToListAsync();
            Console.WriteLine($"Old database has {oldGuilds.Count} guilds");

            Console.WriteLine("Retrieving guilds from new database");
            var newGuilds = await NewDatabaseContext.Guilds.ToListAsync();
            Console.WriteLine($"New database has {newGuilds.Count} guilds");

            var guildsToInsert = oldGuilds
                .Where(w => !w.GuildID.ToString().Contains(newGuilds.Select(s => s.GuildId).ToString()))
                .ToList();

            //Console.WriteLine($"Inserting {guildsToInsert.Count} guilds that do not yet exist into the new database");
            //await NewDatabaseContext.Guilds.AddRangeAsync(
            //    guildsToInsert
            //        .Select(s => new Friend.Guild
            //        {
            //            ChartTimePeriod = s.ChartTimePeriod,
            //            FmEmbedType = s.FmEmbedType,
            //            TitlesEnabled = s.TitlesEnabled,
            //            Blacklisted = s.Blacklisted,
            //            DiscordGuildId = ulong.Parse(s.DiscordGuildID),
            //            EmoteReactions = s.EmoteReactions,
            //            Name = s.Name
            //        }));

            Console.WriteLine("Guilds have been inserted.");
        }

        private static async Task FixKeyValues()
        {
            await NewDatabaseContext.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('users', 'user_id'), (SELECT MAX(user_id) FROM users)+1);");
            Console.WriteLine("User key value has been fixed.");

            await NewDatabaseContext.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('friends', 'friend_id'), (SELECT MAX(friend_id) FROM friends)+1);");
            Console.WriteLine("Friend key value has been fixed.");

            await NewDatabaseContext.Database.ExecuteSqlRawAsync("SELECT pg_catalog.setval(pg_get_serial_sequence('guilds', 'guild_id'), (SELECT MAX(guild_id) FROM guilds)+1);");
            Console.WriteLine("Guild key value has been fixed.");
        }

        private static async Task ClearNewDatabase()
        {
            Console.WriteLine("\n" +
                              "Clearing new database will start in 5 seconds... \n" +
                              "Use ctrl + c to cancel");

            await Task.Delay(5000);

            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM friends;");
            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM users;");
            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM guilds;");

            Console.WriteLine("New database has been cleared");
        }

        private static async Task MigrateArtistsToRecords()
        {
            var artistsWithAliases = await NewDatabaseContext.Artists
                .Where(w => w.Aliases != null)
                .ToListAsync();

            Console.WriteLine($"Found {artistsWithAliases.Count} artists with aliases");

            var aliases = await NewDatabaseContext.ArtistAliases
                .ToListAsync();

            int aliasesAdded = 0;

            Console.WriteLine($"Found {aliases.Count} existing aliases");

            foreach (var artist in artistsWithAliases)
            {
                foreach (var alias in artist.Aliases)
                {
                    if (!aliases.Select(s => s.Alias.ToLower()).Contains(alias.ToLower()))
                    {
                        await NewDatabaseContext.ArtistAliases.AddAsync(new Friend.ArtistAlias
                        {
                            Alias = alias,
                            ArtistId = artist.Id,
                            CorrectsInScrobbles = true
                        });

                        aliasesAdded++;
                        Console.WriteLine($"Added alias {alias} for {artist.Name}");
                    }
                }
            }

            Console.WriteLine($"Added {aliasesAdded} aliases");
            await Task.Delay(5000);
        }
    }
}
