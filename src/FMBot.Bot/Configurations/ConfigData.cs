using System;
using System.IO;
using System.Threading;
using FMBot.Domain.Models;
using Newtonsoft.Json;
using Serilog;

namespace FMBot.Bot.Configurations;

public static class ConfigData
{
    private const string ConfigFolder = "configs";
    private const string ConfigFile = "config.json";

    public static BotSettings Data { get; }

    static ConfigData()
    {
        if (!Directory.Exists(ConfigFolder))
        {
            Directory.CreateDirectory(ConfigFolder);
        }

        if (!File.Exists(ConfigFolder + "/" + ConfigFile))
        {
            // Default config template
            Data = new BotSettings
            {
                Database = new DatabaseConfig
                {
                    ConnectionString = "Host=localhost;Port=5432;Username=postgres;Password=password;Database=fmbot;Command Timeout=60;Timeout=60;Persist Security Info=True"
                },
                Logging = new LoggingConfig
                {
                    SeqServerUrl = "http://localhost:5341"
                },
                Bot = new BotConfig
                {
                    Prefix = ".",
                    BaseServerId = 0000000000000,
                    FeaturedChannelId = 0000000000000,
                    FeaturedPreviewWebhookUrl = "CHANGE-ME-WEBHOOK-URL",
                    UseShardEnvConfig = false
                },
                Discord = new DiscordConfig
                {
                    BotUserId = 0000000000000,
                    ApplicationId = 0000000000000,
                    Token = "CHANGE-ME-DISCORD-TOKEN"
                },
                LastFm = new LastFmConfig
                {
                    PrivateKey = "CHANGE-ME-LASTFM-API-KEY",
                    PublicKey = "CHANGE-ME-LASTFM-API-KEY",
                    PublicKeySecret = "CHANGE-ME-LASTFM-API-SECRET",
                    UserUpdateFrequencyInHours = 24,
                    UserIndexFrequencyInDays = 120
                },
                Genius = new GeniusConfig
                {
                    AccessToken = string.Empty
                },
                Spotify = new SpotifyConfig
                {
                    Key = string.Empty,
                    Secret = string.Empty
                },
                Discogs = new DiscogsConfig
                {
                    Key = string.Empty,
                    Secret = string.Empty
                },
                Environment = "local",
                OpenAi = new OpenAiConfig
                {
                    Key = string.Empty,
                    RoastPrompt = "Write a roast about me using some of my top artists: ",
                    ComplimentPrompt = "Write a compliment about me using some of my top artists: ",
                }
            };

            var json = JsonConvert.SerializeObject(Data, Formatting.Indented);
            File.WriteAllText(ConfigFolder + "/" + ConfigFile, json);

            Console.WriteLine("Created new bot configuration file with default values. \n" +
                              $"Please set your API keys in {ConfigFolder}/{ConfigFile} before running the bot again. \n \n" +
                              "Exiting in 10 seconds...",
                ConsoleColor.Red);

            Thread.Sleep(10000);
            Environment.Exit(0);
        }
        else
        {
            var json = File.ReadAllText(ConfigFolder + "/" + ConfigFile);
            Data = JsonConvert.DeserializeObject<BotSettings>(json);

            if (Data.Bot.UseShardEnvConfig == true)
            {
                Log.Information("Config is using shard environment variables");

                Data.Shards = new ShardConfig
                {
                    MainInstance = Environment.GetEnvironmentVariable("SHARDS_MAIN_INSTANCE") == "true",
                    StartShard = Environment.GetEnvironmentVariable("SHARDS_FIRST_SHARD") != null
                        ? int.Parse(Environment.GetEnvironmentVariable("SHARDS_FIRST_SHARD"))
                        : null,
                    EndShard = Environment.GetEnvironmentVariable("SHARDS_LAST_SHARD") != null
                        ? int.Parse(Environment.GetEnvironmentVariable("SHARDS_LAST_SHARD"))
                        : null,
                    TotalShards = Environment.GetEnvironmentVariable("SHARDS_TOTAL_SHARDS") != null
                        ? int.Parse(Environment.GetEnvironmentVariable("SHARDS_TOTAL_SHARDS"))
                        : null,
                    InstanceName = Environment.GetEnvironmentVariable("INSTANCE_NAME")
                };

                Log.Information("Config initiated - MainInstance {mainInstance}", Data.Shards.MainInstance);
            }
        }
    }
}
