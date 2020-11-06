using System;

namespace FMBot.Bot
{
    // TODO: get rid of this file
    public static class FMBotUtil
    {
        public static class GlobalVars
        {
            // TODO: Move this somewhere else
            private static readonly string BasePath = AppDomain.CurrentDomain.BaseDirectory;
            public static readonly string CacheFolder = BasePath + "cache/";
            public static string ImageFolder = BasePath + "resources/images/";
            public static string FontFolder = BasePath + "resources/fonts/";
        }
    }
}
