using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using FMBot.Bot.Models;

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
            public static string FeaturedUserID = "";

        }
    }
}
