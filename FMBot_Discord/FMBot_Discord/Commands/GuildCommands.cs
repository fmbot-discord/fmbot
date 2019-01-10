using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using FMBot.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotModules;

namespace FMBot.Bot.Commands
{
    public class GuildCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly TimerService _timer;

        private readonly UserService userService = new UserService();

        private readonly GuildService guildService = new GuildService();

        public GuildCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }


        #region Server Staff Only Commands

        [Command("fmserverset"), Summary("Sets the global FMBot settings for the server. - Server Staff only")]
        [Alias("fmserversetmode")]
        public async Task fmserversetAsync([Summary("The default mode you want to use.")] string chartType = "embedmini", [Summary("The default timeperiod you want to use.")] string chartTimePeriod = "monthly")
        {
            if (guildService.CheckIfDM(Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                return;
            }

            IGuildUser serverUser = (IGuildUser)Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator)
            {
                await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission or admins can use this command.");
                return;
            }
            if (chartType == "help")
            {
                await ReplyAsync("Sets the global default for you server. `.fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'` command.");
                return;
            }


            if (!Enum.TryParse(chartType, ignoreCase: true, out ChartType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }


            if (!Enum.TryParse(chartTimePeriod, ignoreCase: true, out ChartTimePeriod chartTimePeriodEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'weekly', 'monthly', 'yearly', or 'overall'.");
                return;
            }

            await guildService.ChangeGuildSettingAsync(Context.Guild, chartTimePeriodEnum, chartTypeEnum);


            
            await ReplyAsync("The .fmset default charttype for your server has been set to " + chartTypeEnum +  " with the time period " + chartTimePeriodEnum + ".");
        }

        #endregion

    }
}
