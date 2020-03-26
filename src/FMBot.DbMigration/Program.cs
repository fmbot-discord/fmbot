using FMBot.Data.Entities;
using System;
using System.IO;
using System.Linq;
using FMBot.Data;

namespace FMBot.DbMigration
{
    class Program
    {
        public static FMBotDbContext db = new FMBotDbContext();

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("No arguments defined. Please input the folder path you wish to migrate.");
                Console.ReadKey();
                return;
            }

            Console.WriteLine("Migrating...");

            DirectoryInfo d = new DirectoryInfo(args[0]);//Assuming Test is your Folder
            FileInfo[] Files = d.GetFiles("*.txt"); //Getting Text files

            var users = db.Users.ToList();

            foreach (FileInfo file in Files)
            {
                try
                {
                    string line1 = File.ReadLines(file.FullName).First();

                    if (!users.Select(s => s.UserNameLastFM).Contains(line1))
                    {
                        User User = new User
                        {
                            DiscordUserID = file.Name,
                            UserNameLastFM = line1,
                            ChartTimePeriod = ChartTimePeriod.Monthly
                        };

                        db.Users.Add(User);

                        Console.WriteLine("Added user " + User.UserNameLastFM);
                    }
                    else
                    {
                        Console.WriteLine("Skipped user " + line1 + " (already exists)");
                    }
                }
                catch { }
            }

            db.SaveChanges();

            Console.WriteLine("Done! Press any key to stop...");
            Console.ReadKey();
        }
    }
}
