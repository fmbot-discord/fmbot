using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;

namespace FMBot.Bot.Configurations
{
    public static class ConfigData
    {
        private const string ConfigFolder = "configs";
        private const string ConfigFile = "ConfigData.json";

        public static ConfigJson Data { get; }

        /// <summary>
        /// Loads all the <see cref="ConfigData"/> needed to start the bot.
        /// </summary>
        static ConfigData()
        {
            if (!Directory.Exists(ConfigFolder)) Directory.CreateDirectory(ConfigFolder);

            if (!File.Exists(ConfigFolder + "/" + ConfigFile))
            {
                Data = new ConfigJson();
                var json = JsonConvert.SerializeObject(Data, Formatting.Indented);
                File.WriteAllText(ConfigFolder + "/" + ConfigFile, json);

                Console.WriteLine($"Created new bot configuration file. Please set your API keys in {ConfigFolder}/{ConfigFile} before running the bot again. \n \n" +
                                  "Exiting in 10 seconds...");

                Thread.Sleep(10000);
                Environment.Exit(0);
            }
            else
            {
                var json = File.ReadAllText(ConfigFolder + "/" + ConfigFile);
                Data = JsonConvert.DeserializeObject<ConfigJson>(json);
            }
        }
    }
}