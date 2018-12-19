using Discord;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Drawing;

namespace FMBot.Bot.Models
{
    public class LastFMModels
    {
        public class FMBotChart
        {
            public string time;
            public string LastFMName;
            public int max;
            public int rows;
            public List<Bitmap> images;
            public IUser DiscordUser;
            public DiscordSocketClient disclient;
            public int mode;
            public bool titles;
        }
    }
}
