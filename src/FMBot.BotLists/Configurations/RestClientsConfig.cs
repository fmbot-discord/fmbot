using System.IO;
using Bot.BotLists.Structs;
using Newtonsoft.Json;

namespace Bot.BotLists.Configurations
{
    public static class RestClientsConfig
    {
        /// <summary>The folder name where the <see cref="ConfigFile"/> is located.</summary>
        private const string ConfigFolder = "Configs";

        /// <summary>The file name of the config file.</summary>
        private const string ConfigFile = "BotListsConfig.json";

        public static TokenConfig TokenConfig;

        /// <summary>
        /// Load the token config file.
        /// </summary>
        static RestClientsConfig()
        {
            if (!Directory.Exists(ConfigFolder))
                Directory.CreateDirectory(ConfigFolder);

            if (!File.Exists(ConfigFolder + "/" + ConfigFile))
            {
                TokenConfig = new TokenConfig();
                var json = JsonConvert.SerializeObject(TokenConfig, Formatting.Indented);
                File.WriteAllText(ConfigFolder + "/" + ConfigFile, json);
            }
            else
            {
                var json = File.ReadAllText(ConfigFolder + "/" + ConfigFile);
                TokenConfig = JsonConvert.DeserializeObject<TokenConfig>(json);
            }
        }
    }
}