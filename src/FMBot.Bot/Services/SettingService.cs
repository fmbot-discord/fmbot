using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Models;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class SettingService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public SettingService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public static TimeSettingsModel GetTimePeriod(
            string extraOptions,
            ChartTimePeriod defaultTimePeriod = ChartTimePeriod.Weekly
            )
        {
            var settingsModel = new TimeSettingsModel();
            var customTimePeriod = true;

            extraOptions ??= "";

            // time period
            if (extraOptions.Contains("weekly") ||
                extraOptions.Contains("week") ||
                extraOptions.Contains("w") ||
                extraOptions.Contains("7d"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
                settingsModel.PlayDays = 7;
            }
            else if (extraOptions.Contains("monthly") ||
                     extraOptions.Contains("month") ||
                     extraOptions.Contains("m") ||
                     extraOptions.Contains("1m") ||
                     extraOptions.Contains("30d"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
                settingsModel.PlayDays = 30;
            }
            else if (extraOptions.Contains("quarterly") ||
                     extraOptions.Contains("quarter") ||
                     extraOptions.Contains("q") ||
                     extraOptions.Contains("3m") ||
                     extraOptions.Contains("90d"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
                settingsModel.PlayDays = 90;
            }
            else if (extraOptions.Contains("halfyearly") ||
                     extraOptions.Contains("half") ||
                     extraOptions.Contains("h") ||
                     extraOptions.Contains("6m") ||
                     extraOptions.Contains("180d"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Half;
                settingsModel.Description = "Half-yearly";
                settingsModel.UrlParameter = "date_preset=LAST_180_DAYS";
                settingsModel.ApiParameter = "6month";
                settingsModel.PlayDays = 180;
            }
            else if (extraOptions.Contains("yearly") ||
                     extraOptions.Contains("year") ||
                     extraOptions.Contains("y") ||
                     extraOptions.Contains("1y") ||
                     extraOptions.Contains("12m") ||
                     extraOptions.Contains("365d"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.UrlParameter = "date_preset=LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
                settingsModel.PlayDays = 365;
            }
            else if (extraOptions.Contains("overall") ||
                     extraOptions.Contains("alltime") ||
                     extraOptions.Contains("o") ||
                     extraOptions.Contains("at") ||
                     extraOptions.Contains("a"))
            {
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "date_preset=ALL";
                settingsModel.ApiParameter = "overall";
            }
            else if (extraOptions.Contains("6day") ||
                     extraOptions.Contains("6-day") ||
                     extraOptions.Contains("day6") ||
                     extraOptions.Contains("6d"))
            {
                var dateString = DateTime.Today.AddDays(-6).ToString("yyyy-M-dd");
                settingsModel.Description = "6-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 6;
            }
            else if (extraOptions.Contains("5day") ||
                     extraOptions.Contains("5-day") ||
                     extraOptions.Contains("day5") ||
                     extraOptions.Contains("5d"))
            {
                var dateString = DateTime.Today.AddDays(-5).ToString("yyyy-M-dd");
                settingsModel.Description = "5-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 5;
            }
            else if (extraOptions.Contains("4day") ||
                     extraOptions.Contains("4-day") ||
                     extraOptions.Contains("day4") ||
                     extraOptions.Contains("4d"))
            {
                var dateString = DateTime.Today.AddDays(-4).ToString("yyyy-M-dd");
                settingsModel.Description = "4-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 4;
            }
            else if (extraOptions.Contains("3day") ||
                     extraOptions.Contains("3-day") ||
                     extraOptions.Contains("day3") ||
                     extraOptions.Contains("3d"))
            {
                var dateString = DateTime.Today.AddDays(-3).ToString("yyyy-M-dd");
                settingsModel.Description = "3-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 3;
            }
            else if (extraOptions.Contains("2day") ||
                     extraOptions.Contains("2-day") ||
                     extraOptions.Contains("day2") ||
                     extraOptions.Contains("2d"))
            {
                var dateString = DateTime.Today.AddDays(-2).ToString("yyyy-M-dd");
                settingsModel.Description = "2-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 2;
            }
            else if (extraOptions.Contains("1day") ||
                     extraOptions.Contains("1-day") ||
                     extraOptions.Contains("day1") ||
                     extraOptions.Contains("1d") ||
                     extraOptions.Contains("today"))
            {
                var dateString = DateTime.Today.AddDays(-1).ToString("yyyy-M-dd");
                settingsModel.Description = "1-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 1;
            }
            else
            {
                customTimePeriod = false;
            }

            if (!customTimePeriod)
            {
                if (defaultTimePeriod == ChartTimePeriod.AllTime)
                {
                    settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                    settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                    settingsModel.Description = "Overall";
                    settingsModel.UrlParameter = "date_preset=ALL";
                    settingsModel.ApiParameter = "overall";
                }
                else
                {
                    settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                    settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                    settingsModel.Description = "Weekly";
                    settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                    settingsModel.ApiParameter = "7day";
                    settingsModel.PlayDays = 7;
                }
            }

            return settingsModel;
        }

        public async Task<UserSettingsModel> GetUser(
            string extraOptions,
            User user,
            ICommandContext context)
        {
            var settingsModel = new UserSettingsModel
            {
                DifferentUser = false,
                UserNameLastFm = user.UserNameLastFM,
                DiscordUserId = context.User.Id,
                UserId = user.UserId
            };

            if (extraOptions == null)
            {
                return settingsModel;
            }

            var options = extraOptions.Split(' ');

            foreach (var option in options)
            {
                var otherUser = await GetUserFromString(option);

                if (otherUser != null)
                {
                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = otherUser.DiscordUserId;
                    settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                }
            }

            return settingsModel;
        }

        public async Task<UserSettingsModel> GetFmBotUser(
            string extraOptions,
            User currentUser)
        {
            var settingsModel = new UserSettingsModel
            {
                DifferentUser = false,
                UserNameLastFm = currentUser.UserNameLastFM,
                UserId = currentUser.UserId
            };

            if (extraOptions == null)
            {
                return settingsModel;
            }

            var options = extraOptions.Split(' ');

            foreach (var option in options)
            {
                var otherUser = await GetUserFromString(option);

                if (otherUser != null)
                {
                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = otherUser.DiscordUserId;
                    settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                }
            }

            return settingsModel;
        }

        public async Task<User> GetDifferentUser(string searchValue)
        {
            var otherUser = await GetUserFromString(searchValue);

            if (otherUser == null)
            {
                await using var db = this._contextFactory.CreateDbContext();
                return await db.Users
                    .AsQueryable()
                    .OrderByDescending(o => o.LastUsed)
                    .FirstOrDefaultAsync(f => f.UserNameLastFM.ToLower() == searchValue.ToLower());
            }

            return otherUser;
        }

        public async Task<User> GetUserFromString(string value)
        {
            if (!value.Contains("<@") && value.Length != 18)
            {
                return null;
            }

            var id = value.Trim('@', '!', '<', '>');

            if (!ulong.TryParse(id, out var discordUserId))
            {
                return null;
            }

            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
        }

        public static int GetAmount(
            string extraOptions,
            int amount = 8,
            int maxAmount = 15)
        {
            if (extraOptions == null)
            {
                return amount;
            }

            var options = extraOptions.Split(' ');
            foreach (var option in options)
            {
                if (int.TryParse(option, out var result))
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

        public static long GetGoalAmount(
            string extraOptions,
            long currentPlaycount)
        {
            var goalAmount = 100;
            var ownGoalSet = false;

            if (extraOptions != null)
            {
                var options = extraOptions.Split(' ');

                foreach (var option in options)
                {
                    if (int.TryParse(option, out var result))
                    {
                        if (result > currentPlaycount)
                        {
                            goalAmount = result;
                            ownGoalSet = true;
                        }
                    }
                }
            }


            if (!ownGoalSet)
            {
                int[] breakPoints =
                {
                    100,
                    1000,
                    5000,
                    10000,
                    25000,
                    50000,
                    100000,
                    150000,
                    200000,
                    250000,
                    300000,
                    350000,
                    400000,
                    450000,
                    500000,
                    1000000,
                    2000000,
                    5000000
                };

                foreach (var breakPoint in breakPoints)
                {
                    if (currentPlaycount < breakPoint)
                    {
                        goalAmount = breakPoint;
                        break;
                    }
                }
            }

            return goalAmount;
        }

        public static GuildRankingSettings SetGuildRankingSettings(GuildRankingSettings guildRankingSettings, string[] extraOptions)
        {
            var setGuildRankingSettings = guildRankingSettings;

            if (extraOptions.Contains("w") || extraOptions.Contains("week") || extraOptions.Contains("weekly"))
            {
                setGuildRankingSettings.ChartTimePeriod = ChartTimePeriod.Weekly;
            }
            if (extraOptions.Contains("a") || extraOptions.Contains("at") || extraOptions.Contains("alltime"))
            {
                setGuildRankingSettings.ChartTimePeriod = ChartTimePeriod.AllTime;
            }
            if (extraOptions.Contains("p") || extraOptions.Contains("pc") || extraOptions.Contains("playcount") || extraOptions.Contains("plays"))
            {
                setGuildRankingSettings.OrderType = OrderType.Playcount;
            }
            if (extraOptions.Contains("l") || extraOptions.Contains("lc") || extraOptions.Contains("listenercount") || extraOptions.Contains("listeners"))
            {
                setGuildRankingSettings.OrderType = OrderType.Listeners;
            }

            return setGuildRankingSettings;
        }
    }
}
