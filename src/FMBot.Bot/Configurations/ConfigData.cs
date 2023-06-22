using System;
using System.IO;
using System.Threading;
using FMBot.Domain.Models;
using Newtonsoft.Json;

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
                    MainInstance = true,
                    FeaturedMaster = true
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
                Genius = new GeniusConfig(),
                Spotify = new SpotifyConfig(),
                Discogs = new DiscogsConfig(),
                Environment = "local"
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
        }
    }
}
