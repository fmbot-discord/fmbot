using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using FMBot.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotModules;

namespace FMBot.Bot.Commands
{
    [Summary("Server Staff Only")]
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

        [Command("fmserverset"), Summary("Sets the global FMBot settings for the server.")]
        [Alias("fmserversetmode")]
        public async Task fmserversetAsync([Summary("The default mode you want to use.")] string chartType = "embedmini", [Summary("The default timeperiod you want to use.")] string chartTimePeriod = "monthly")
        {
            if (guildService.CheckIfDM(Context))
            {
                await ReplyAsync("Command is not supported in DMs.").ConfigureAwait(false);
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
                await ReplyAsync("Sets the global default for your server. `.fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'` command.");
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



            await ReplyAsync("The .fmset default charttype for your server has been set to " + chartTypeEnum + " with the time period " + chartTimePeriodEnum + ".");
        }

        [Command("fmgetmembers"), Summary("Gets Last.FM usernames from your server members.")]
        public async Task fmGetMembersAsync()
        {
            if (guildService.CheckIfDM(Context))
            {
                await ReplyAsync("Command is not supported in DMs.").ConfigureAwait(false);
                return;
            }

            IGuildUser serverUser = (IGuildUser)Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator)
            {
                await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission or admins can use this command.");
                return;
            }

            Dictionary<string, string> serverUsers = await guildService.FindAllUsersFromGuildAsync(Context);

            if (serverUsers.Count == 0)
            {
                await ReplyAsync("No members found on this server.");
                return;
            }

            string reply = "The " + serverUsers.Count + " Last.FM users on this server are: \n";
            foreach (KeyValuePair<string, string> fmbotUser in serverUsers)
            {
                reply += fmbotUser.Key + " - " + fmbotUser.Value + "\n";

                if (reply.Length > 1950)
                {
                    await ReplyAsync(reply).ConfigureAwait(false);
                    reply = "";
                }

            }

            await ReplyAsync(reply).ConfigureAwait(false);
        }


        #endregion

    }
}
