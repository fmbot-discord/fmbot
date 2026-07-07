using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace FMBot.Persistence.EntityFrameWork
{
    public class FMBotDbContextFactory : IDesignTimeDbContextFactory<FMBotDbContext>
    {
        public FMBotDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Database:ConnectionString"] =
                        "Host=localhost;Port=5432;Username=postgres;Password=password;Database=fmbot-local;Command Timeout=60;Timeout=60;Persist Security Info=True"
                })
                .AddJsonFile(GetBotConfigPath(), true, false)
                .AddEnvironmentVariables()
                .Build();

            return new FMBotDbContext(configuration);
        }

        private static string GetBotConfigPath([CallerFilePath] string sourceFilePath = "")
        {
            return Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(sourceFilePath), "..", "FMBot.Bot", "configs", "config.json"));
        }
    }
}
