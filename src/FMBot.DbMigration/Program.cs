using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Data;
using FMBot.DbMigration.OldDatabase.Entities;
using Microsoft.EntityFrameworkCore;

namespace FMBot.DbMigration
{
    class Program
    {
        private static readonly FMBotDbContext NewDatabaseContext = new FMBotDbContext();
        private static readonly LocalDbDbContext OldDatabaseContext = new LocalDbDbContext();

        static async Task Main()
        {
            Console.WriteLine("Welcome to the migration tool for migrating the .fmbot database tool from localdb to PostgreSQL \n" +
                              "This tool requires you to have ran .fmbot with PostgreSQL at least once. \n\n" +
                              "Options: \n" +
                              "1. Migrate all data to new db \n" +
                              "2. Migrate only users \n" +
                              "3. Migrate only friends \n" +
                              "4. Migrate only guilds \n" +
                              "5. Clear new database \n" +
                              "Select an option to continue...");

            var key = Console.ReadKey().Key.ToString();
            var saveToDatabase = true;

            if (key == "D1")
            {
                CopyNewUsers();
                CopyFriends();
                CopyGuilds();
            }
            else if (key == "D2")
            {
                CopyNewUsers();
            }
            else if (key == "D3")
            {
                CopyFriends();
            }
            else if (key == "D4")
            {
                CopyGuilds();
            }
            else if (key == "D5")
            {
                await ClearNewDatabase();
                saveToDatabase = false;
            }
            else
            {
                Console.WriteLine("\nKey not recognized");
            }

            if (saveToDatabase)
            {
                Console.WriteLine($"Saving changes to database...");
                NewDatabaseContext.SaveChanges();
            }

            Console.WriteLine("Done! Press any key to quit program.");
            Console.ReadKey();
        }

        private static void CopyNewUsers()
        {
            Console.WriteLine("Starting user migration...");

            Console.WriteLine("Retrieving users from old database");
            var oldUsers = OldDatabaseContext.Users.ToList();
            Console.WriteLine($"Old database has {oldUsers.Count} users");

            Thread.Sleep(1000);

            Console.WriteLine("Retrieving users from new database");
            var newUsers = NewDatabaseContext.Users.ToList();
            Console.WriteLine($"New database has {newUsers.Count} users");

            Thread.Sleep(1000);

            var usersToInsert = oldUsers
                .Where(w => !w.DiscordUserID.Contains(newUsers.Select(s => s.DiscordUserID).ToString()) &&
                            !w.UserID.ToString().Contains(newUsers.Select(s => s.UserID).ToString()))
                .ToList();

            Thread.Sleep(1000);

            Console.WriteLine($"Inserting {usersToInsert.Count} users that do not yet exist into the new database");
            NewDatabaseContext.Users.AddRange(
                usersToInsert
                    .Select(s => new Data.Entities.User
                    {
                        Blacklisted = s.Blacklisted,
                        ChartTimePeriod = s.ChartTimePeriod,
                        ChartType = s.ChartType,
                        DiscordUserID = ulong.Parse(s.DiscordUserID),
                        Featured = s.Featured,
                        LastGeneratedChartDateTimeUtc = s.LastGeneratedChartDateTimeUtc,
                        TitlesEnabled = s.TitlesEnabled,
                        UserID = s.UserID,
                        UserNameLastFM = s.UserNameLastFM,
                        UserType = s.UserType
                    }));
            Console.WriteLine($"Users have been inserted.");
        }

        private static void CopyFriends()
        {
            Console.WriteLine("Starting friend migration...");

            Console.WriteLine("Retrieving friends from old database");
            var oldFriends = OldDatabaseContext.Friends.ToList();
            Console.WriteLine($"Old database has {oldFriends.Count} friends");

            Thread.Sleep(1000);

            Console.WriteLine("Retrieving friends from new database");
            var newFriends = NewDatabaseContext.Friends.ToList();
            Console.WriteLine($"New database has {newFriends.Count} friends");

            Thread.Sleep(1000);

            var friendsToInsert = oldFriends
                .Where(w => !w.FriendID.ToString().Contains(newFriends.Select(s => s.FriendID).ToString()))
                .ToList();

            Thread.Sleep(1000);

            Console.WriteLine($"Inserting {friendsToInsert.Count} friends that do not yet exist into the new database");
            NewDatabaseContext.Friends.AddRange(
                friendsToInsert
                    .Select(s => new Data.Entities.Friend
                    {
                        FriendID = s.FriendID,
                        FriendUserID = s.FriendUserID,
                        LastFMUserName = s.LastFMUserName
                    }));
            Console.WriteLine($"Friends have been inserted.");
        }

        private static void CopyGuilds()
        {
            Console.WriteLine("Starting guild migration...");

            Console.WriteLine("Retrieving guilds from old database");
            var oldGuilds = OldDatabaseContext.Guilds.ToList();
            Console.WriteLine($"Old database has {oldGuilds.Count} guilds");

            Thread.Sleep(1000);

            Console.WriteLine("Retrieving guilds from new database");
            var newGuilds = NewDatabaseContext.Guilds.ToList();
            Console.WriteLine($"New database has {newGuilds.Count} guilds");

            Thread.Sleep(1000);

            var guildsToInsert = oldGuilds
                .Where(w => !w.GuildID.ToString().Contains(newGuilds.Select(s => s.GuildID).ToString()))
                .ToList();

            Thread.Sleep(1000);

            Console.WriteLine($"Inserting {guildsToInsert.Count} guilds that do not yet exist into the new database");
            NewDatabaseContext.Guilds.AddRange(
                guildsToInsert
                    .Select(s => new Data.Entities.Guild
                    {
                        GuildID = s.GuildID,
                        ChartTimePeriod = s.ChartTimePeriod,
                        ChartType = s.ChartType,
                        TitlesEnabled = s.TitlesEnabled,
                        Blacklisted = s.Blacklisted,
                        DiscordGuildID = ulong.Parse(s.DiscordGuildID),
                        EmoteReactions = s.EmoteReactions,
                        Name = s.Name
                    }));
            Console.WriteLine($"Guilds have been inserted.");
        }

        private static async Task ClearNewDatabase()
        {
            Console.WriteLine("\n" +
                              "Clearing database will start in 5 seconds... \n" +
                              "Use ctrl + c to cancel");

            await Task.Delay(5000);

            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM public.\"Friends\";");
            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM public.\"Users\";");
            NewDatabaseContext.Database.ExecuteSqlRaw("DELETE FROM public.\"Guilds\";");

            Console.WriteLine($"New database has been cleared");
        }
    }
}
