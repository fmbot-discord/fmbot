using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using Newtonsoft.Json;
using Unosquare.Swan.Formatters;

namespace FMBot.Bot.Commands
{
    [Summary("Server Staff Only")]
    public class GuildCommands : ModuleBase
    {
        private readonly CommandService _service;
        private readonly TimerService _timer;

        private readonly AdminService adminService = new AdminService();

        private readonly GuildService guildService = new GuildService();

        public GuildCommands(CommandService service, TimerService timer)
        {
            _service = service;
            _timer = timer;
        }

        [Command("fmserverset"), Summary("Sets the global FMBot settings for the server.")]
        [Alias("fmserversetmode")]
        public async Task fmserversetAsync([Summary("The default mode you want to use.")] string chartType = "embedmini", [Summary("The default timeperiod you want to use.")] string chartTimePeriod = "monthly")
        {
            if (guildService.CheckIfDM(Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                return;
            }

            IGuildUser serverUser = (IGuildUser)Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator && !await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
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
                await ReplyAsync("Command is not supported in DMs.");
                return;
            }

            IGuildUser serverUser = (IGuildUser)Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator && !await adminService.HasCommandAccessAsync(Context.User, UserType.Admin))
            {
                await ReplyAsync("You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                return;
            }

            var serverUsers = await guildService.FindAllUsersFromGuildAsync(Context);

            if (serverUsers.Count == 0)
            {
                await ReplyAsync("No members found on this server.");
                return;
            }

            try
            {
                var userJson = System.Text.Json.JsonSerializer.Serialize(serverUsers, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await this.Context.User.SendFileAsync(ToStream(userJson), $"users_UTC-{DateTime.UtcNow.ToString("u")}.json");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            await ReplyAsync("Check your DMs!");
        }


        public static Stream ToStream(string str)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
