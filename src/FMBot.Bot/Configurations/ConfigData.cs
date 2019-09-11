using System.IO;
using Newtonsoft.Json;

namespace FMBot.Bot.Configurations
{
    public static class ConfigData
    {
        private const string ConfigFolder = "Configs";
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
            }
            else
            {
                var json = File.ReadAllText(ConfigFolder + "/" + ConfigFile);
                Data = JsonConvert.DeserializeObject<ConfigJson>(json);
            }
        }
    }
}