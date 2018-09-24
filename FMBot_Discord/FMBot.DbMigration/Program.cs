using FMBot.Data.Entities;
using System;
using System.IO;
using System.Linq;

namespace FMBot.DbMigration
{
    class Program
    {
        // Change this to whatever your folder is
        public static string Folder = "C:/Users/BitL/Desktop/db/users";
        public static FMBotDbContext db = new FMBotDbContext();

        static void Main(string[] args)
        {
            Console.WriteLine("hi");

            DirectoryInfo d = new DirectoryInfo(Folder);//Assuming Test is your Folder
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

                        Console.WriteLine("added user " + User.UserNameLastFM);
                    }
                    else
                    {
                        Console.WriteLine("skipped user " + line1 + " (already exists)");
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
