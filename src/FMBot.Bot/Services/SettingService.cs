using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public static class SettingService
    {
        public static TimeSettingsModel GetTimePeriod(
            string[] extraOptions,
            LastStatsTimeSpan defaultLastStatsTimeSpan = LastStatsTimeSpan.Week,
            ChartTimePeriod defaultChartTimePeriod = ChartTimePeriod.Weekly,
            string defaultUrlParameter = "LAST_7_DAYS",
            string defaultApiParameter = "7day")
        {
            var settingsModel = new TimeSettingsModel();

            // time period
            if (extraOptions.Contains("weekly") || extraOptions.Contains("week") || extraOptions.Contains("w"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.UrlParameter = "LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
            }
            else if (extraOptions.Contains("monthly") || extraOptions.Contains("month") || extraOptions.Contains("m"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
            }
            else if (extraOptions.Contains("quarterly") || extraOptions.Contains("quarter") || extraOptions.Contains("q"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.UrlParameter = "LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
            }
            else if (extraOptions.Contains("halfyearly") || extraOptions.Contains("half") || extraOptions.Contains("h"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Half;
                settingsModel.Description = "Half-yearly";
                settingsModel.UrlParameter = "LAST_180_DAYS";
                settingsModel.ApiParameter = "6month";
            }
            else if (extraOptions.Contains("yearly") || extraOptions.Contains("year") || extraOptions.Contains("y"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.UrlParameter = "LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
            }
            else if (extraOptions.Contains("overall") || extraOptions.Contains("alltime") || extraOptions.Contains("o") ||
                     extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "ALL";
                settingsModel.ApiParameter = "overall";
            }
            else
            {
                settingsModel.LastStatsTimeSpan = defaultLastStatsTimeSpan;
                settingsModel.ChartTimePeriod = defaultChartTimePeriod;
                settingsModel.Description = "";
                settingsModel.UrlParameter = defaultUrlParameter;
                settingsModel.ApiParameter = defaultApiParameter;
            }

            return settingsModel;
        }

        public static async Task<UserSettingsModel> GetUser(
            string[] extraOptions,
            string username,
            ICommandContext context)
        {
            var settingsModel = new UserSettingsModel
            {
                DifferentUser = false,
                UserNameLastFm = username,
                DiscordUserId = context.User.Id
            };

            foreach (var extraOption in extraOptions)
            {
                if (!extraOption.Contains("<@") && extraOption.Length != 18)
                {
                    continue;
                }

                var id = extraOption.Trim('@', '!', '<', '>');

                if (!ulong.TryParse(id, out var discordUserId))
                {
                    continue;
                }
                if (context.Guild == null)
                {
                    continue;
                }
                var guildUser = await context.Guild.GetUserAsync(discordUserId);

                if (guildUser == null)
                {
                    continue;
                }

                await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
                var user = await db.Users
                    .AsQueryable()
                    .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

                if (user == null)
                {
                    continue;
                }

                settingsModel.DifferentUser = true;
                settingsModel.DiscordUserId = discordUserId;
                settingsModel.UserNameLastFm = user.UserNameLastFM;
            }

            return settingsModel;
        }

        public static int GetAmount(
            string[] extraOptions,
            int amount = 8,
            int maxAmount = 16)
        {
            foreach (var extraOption in extraOptions)
            {
                if (int.TryParse(extraOption, out var result))
                {
                    if (result > 0 && result <= 100)
                    {
                        if (result > maxAmount)
                        {
                            return maxAmount;
                        }

                        return result;
                    }
                }
            }

            return amount;
        }
    }
}
