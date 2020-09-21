using System;
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
            string defaultApiParameter = "7day",
            string defaultDescription = "Weekly")
        {
            var settingsModel = new TimeSettingsModel();

            // time period
            if (extraOptions.Contains("weekly") || extraOptions.Contains("week") || extraOptions.Contains("w"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
            }
            else if (extraOptions.Contains("monthly") || extraOptions.Contains("month") || extraOptions.Contains("m"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
            }
            else if (extraOptions.Contains("quarterly") || extraOptions.Contains("quarter") || extraOptions.Contains("q"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
            }
            else if (extraOptions.Contains("halfyearly") || extraOptions.Contains("half") || extraOptions.Contains("h"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Half;
                settingsModel.Description = "Half-yearly";
                settingsModel.UrlParameter = "date_preset=LAST_180_DAYS";
                settingsModel.ApiParameter = "6month";
            }
            else if (extraOptions.Contains("yearly") || extraOptions.Contains("year") || extraOptions.Contains("y"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.UrlParameter = "date_preset=LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
            }
            else if (extraOptions.Contains("overall") || extraOptions.Contains("alltime") || extraOptions.Contains("o") ||
                     extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "date_preset=ALL";
                settingsModel.ApiParameter = "overall";
            }
            else if (extraOptions.Contains("6day") ||extraOptions.Contains("6-day") || extraOptions.Contains("day6") || extraOptions.Contains("6d"))
            {
                var dateString = DateTime.Today.AddDays(-6).ToString("yyyy-M-dd");
                settingsModel.Description = "6-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 6;
            }
            else if (extraOptions.Contains("5day") || extraOptions.Contains("5-day") || extraOptions.Contains("day5") || extraOptions.Contains("5d"))
            {
                var dateString = DateTime.Today.AddDays(-5).ToString("yyyy-M-dd");
                settingsModel.Description = "5-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 5;
            }
            else if (extraOptions.Contains("4day") || extraOptions.Contains("4-day") || extraOptions.Contains("day4") || extraOptions.Contains("4d"))
            {
                var dateString = DateTime.Today.AddDays(-4).ToString("yyyy-M-dd");
                settingsModel.Description = "4-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 4;
            }
            else if (extraOptions.Contains("3day") ||extraOptions.Contains("3-day") || extraOptions.Contains("day3") || extraOptions.Contains("3d"))
            {
                var dateString = DateTime.Today.AddDays(-3).ToString("yyyy-M-dd");
                settingsModel.Description = "3-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 3;
            }
            else if (extraOptions.Contains("2day") || extraOptions.Contains("2-day") || extraOptions.Contains("day2") || extraOptions.Contains("2d"))
            {
                var dateString = DateTime.Today.AddDays(-2).ToString("yyyy-M-dd");
                settingsModel.Description = "2-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 2;
            }
            else if (extraOptions.Contains("1day") || extraOptions.Contains("1-day") || extraOptions.Contains("day1") || extraOptions.Contains("1d") || extraOptions.Contains("today"))
            {
                var dateString = DateTime.Today.AddDays(-1).ToString("yyyy-M-dd");
                settingsModel.Description = "1-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 1;
            }
            else if (extraOptions.Contains("overall") || extraOptions.Contains("alltime") || extraOptions.Contains("o") ||
                     extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "date_preset=ALL";
                settingsModel.ApiParameter = "overall";
            }
            else
            {
                settingsModel.LastStatsTimeSpan = defaultLastStatsTimeSpan;
                settingsModel.ChartTimePeriod = defaultChartTimePeriod;
                settingsModel.Description = defaultDescription;
                settingsModel.UrlParameter = defaultUrlParameter;
                settingsModel.ApiParameter = defaultApiParameter;
                settingsModel.UsePlays = false;
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
                DiscordUserId = context.User.Id,
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
