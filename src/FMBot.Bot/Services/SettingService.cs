using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
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

        public static TimeSettingsModel GetTimePeriod(string options,
            ChartTimePeriod defaultTimePeriod = ChartTimePeriod.Weekly)
        {
            var settingsModel = new TimeSettingsModel();
            var customTimePeriod = true;

            options ??= "";
            settingsModel.NewSearchValue = options;

            var oneDay = new[] { "1-day", "1day", "day1", "1d", "today" };
            var twoDays = new[] { "2-day", "2day", "day2", "2d" };
            var threeDays = new[] { "3-day", "3day", "day3", "3d" };
            var fourDays = new[] { "4-day", "4day", "day4", "4d" };
            var fiveDays = new[] { "5-day", "5day", "day5", "5d" };
            var sixDays = new[] { "6-day", "6day", "day6", "6d" };
            var weekly = new[] { "weekly", "week", "w", "7d" };
            var monthly = new[] { "monthly", "month", "m", "1m", "30d" };
            var quarterly = new[] { "quarterly", "quarter", "q", "3m", "90d" };
            var halfYearly = new[] { "half-yearly", "halfyearly", "half", "h", "6m", "180d" };
            var yearly = new[] { "yearly", "year", "y", "12m", "365d", "1y" };
            var allTime = new[] { "overall", "alltime", "all-time", "a", "o", "at" };

            if (Contains(options, weekly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, weekly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Week;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Weekly;
                settingsModel.Description = "Weekly";
                settingsModel.UrlParameter = "date_preset=LAST_7_DAYS";
                settingsModel.ApiParameter = "7day";
                settingsModel.PlayDays = 7;
            }
            else if (Contains(options, monthly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, monthly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
                settingsModel.PlayDays = 30;
            }
            else if (Contains(options, monthly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, monthly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Month;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Monthly;
                settingsModel.Description = "Monthly";
                settingsModel.UrlParameter = "date_preset=LAST_30_DAYS";
                settingsModel.ApiParameter = "1month";
                settingsModel.PlayDays = 30;
            }
            else if (Contains(options, quarterly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, quarterly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Quarter;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Quarterly;
                settingsModel.Description = "Quarterly";
                settingsModel.UrlParameter = "date_preset=LAST_90_DAYS";
                settingsModel.ApiParameter = "3month";
                settingsModel.PlayDays = 90;
            }
            else if (Contains(options, halfYearly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, halfYearly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Half;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Half;
                settingsModel.Description = "Half-yearly";
                settingsModel.UrlParameter = "date_preset=LAST_180_DAYS";
                settingsModel.ApiParameter = "6month";
                settingsModel.PlayDays = 180;
            }
            else if (Contains(options, yearly))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, yearly);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Year;
                settingsModel.ChartTimePeriod = ChartTimePeriod.Yearly;
                settingsModel.Description = "Yearly";
                settingsModel.UrlParameter = "date_preset=LAST_365_DAYS";
                settingsModel.ApiParameter = "12month";
                settingsModel.PlayDays = 365;
            }
            else if (Contains(options, allTime))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, allTime);
                settingsModel.LastStatsTimeSpan = LastStatsTimeSpan.Overall;
                settingsModel.ChartTimePeriod = ChartTimePeriod.AllTime;
                settingsModel.Description = "Overall";
                settingsModel.UrlParameter = "date_preset=ALL";
                settingsModel.ApiParameter = "overall";
            }
            else if (Contains(options, sixDays))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, sixDays);
                var dateString = DateTime.Today.AddDays(-6).ToString("yyyy-M-dd");
                settingsModel.Description = "6-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 6;
            }
            else if (Contains(options, fiveDays))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, fiveDays);
                var dateString = DateTime.Today.AddDays(-5).ToString("yyyy-M-dd");
                settingsModel.Description = "5-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 5;
            }
            else if (Contains(options, fourDays))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, fourDays);
                var dateString = DateTime.Today.AddDays(-4).ToString("yyyy-M-dd");
                settingsModel.Description = "4-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 4;
            }
            else if (Contains(options, threeDays))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, threeDays);
                var dateString = DateTime.Today.AddDays(-3).ToString("yyyy-M-dd");
                settingsModel.Description = "3-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 3;
            }
            else if (Contains(options, twoDays))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, twoDays);
                var dateString = DateTime.Today.AddDays(-2).ToString("yyyy-M-dd");
                settingsModel.Description = "2-day";
                settingsModel.UrlParameter = $"from={dateString}";
                settingsModel.UsePlays = true;
                settingsModel.PlayDays = 2;
            }
            else if (Contains(options, oneDay))
            {
                settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, oneDay);
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

        public WhoKnowsSettings SetWhoKnowsSettings(WhoKnowsSettings currentWhoKnowsSettings, string extraOptions)
        {
            var whoKnowsSettings = currentWhoKnowsSettings;

            if (extraOptions == null)
            {
                return whoKnowsSettings;
            }

            var hidePrivateUsers = new[] {"hp", "hideprivate", "hideprivateusers"};
            if (Contains(extraOptions, hidePrivateUsers))
            {
                whoKnowsSettings.NewSearchValue = ContainsAndRemove(whoKnowsSettings.NewSearchValue, hidePrivateUsers);
                whoKnowsSettings.HidePrivateUsers = true;
            }

            return whoKnowsSettings;
        }

        public async Task<UserSettingsModel> GetUser(
            string extraOptions,
            User user,
            ICommandContext context,
            bool firstOptionIsLfmUsername = false)
        {
            string discordUserName;
            if (context.Guild != null)
            {
                var discordGuildUser = await context.Guild.GetUserAsync(user.DiscordUserId);
                discordUserName = discordGuildUser?.Nickname ?? context.User.Username;
            }
            else
            {
                discordUserName = context.User.Username;
            }

            var settingsModel = new UserSettingsModel
            {
                DifferentUser = false,
                UserNameLastFm = user.UserNameLastFM,
                SessionKeyLastFm = user.SessionKeyLastFm,
                DiscordUserId = context.User.Id,
                DiscordUserName = discordUserName,
                UserId = user.UserId,
                UserType = user.UserType,
                NewSearchValue = extraOptions
            };

            if (extraOptions == null)
            {
                return settingsModel;
            }

            var options = extraOptions.Split(' ');

            if (firstOptionIsLfmUsername)
            {
                var otherUser = await GetDifferentUser(options.First());

                if (otherUser != null)
                {
                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] { options.First() }, true);

                    settingsModel.DiscordUserName = otherUser.UserNameLastFM;
                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = otherUser.DiscordUserId;
                    settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                    settingsModel.SessionKeyLastFm = otherUser.SessionKeyLastFm;
                    settingsModel.UserType = otherUser.UserType;
                }
            }

            foreach (var option in options)
            {
                var otherUser = await GetUserFromString(option);

                if (otherUser != null)
                {
                    settingsModel.NewSearchValue = ContainsAndRemove(settingsModel.NewSearchValue, new[] {"<", "@","!", ">", otherUser.DiscordUserId.ToString(), otherUser.UserNameLastFM.ToLower()}, true);

                    if (context.Guild != null)
                    {
                        var discordGuildUser = await context.Guild.GetUserAsync(otherUser.DiscordUserId);
                        settingsModel.DiscordUserName = discordGuildUser != null ? discordGuildUser.Nickname ?? discordGuildUser.Username : otherUser.UserNameLastFM;
                    }
                    else
                    {
                        settingsModel.DiscordUserName = otherUser.UserNameLastFM;
                    }

                    settingsModel.DifferentUser = true;
                    settingsModel.DiscordUserId = otherUser.DiscordUserId;
                    settingsModel.UserNameLastFm = otherUser.UserNameLastFM;
                    settingsModel.UserType = otherUser.UserType;
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
            int maxAmount = 20)
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

        public static bool Contains(string extraOptions, string[] values)
        {
            var optionArray = extraOptions.Split(" ");

            foreach (var value in values)
            {
                foreach (var option in optionArray)
                {
                    if (option.ToLower().Equals(value.ToLower()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public static string ContainsAndRemove(string extraOptions, string[] values, bool alwaysReturnValue = false)
        {
            extraOptions = extraOptions.ToLower();
            var somethingFound = false;

            foreach (var value in values)
            {
                if (extraOptions.Contains(value.ToLower()))
                {
                    extraOptions = extraOptions.Replace(value.ToLower(), "");
                    somethingFound = true;
                }
            }

            if (somethingFound || alwaysReturnValue)
            {
                return extraOptions.TrimEnd();
            }

            return null;
        }
    }
}
