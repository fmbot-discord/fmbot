using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands
{
    [Summary("Server Staff Only")]
    public class GuildCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly GuildService _guildService;
        private readonly SettingService _settingService;

        private readonly IPrefixService _prefixService;
        private readonly IDisabledCommandService _disabledCommandService;

        private readonly CommandService _commands;

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public GuildCommands(IPrefixService prefixService,
            GuildService guildService,
            CommandService commands,
            AdminService adminService,
            IDisabledCommandService disabledCommandService,
            SettingService settingService)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._commands = commands;
            this._disabledCommandService = disabledCommandService;
            this._settingService = settingService;
            this._adminService = adminService;
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("serverset", RunMode = RunMode.Async)]
        [Summary("Sets the global FMBot settings for the server.")]
        [Alias("serversetmode")]
        public async Task SetServerAsync([Summary("The default mode you want to use.")]
            string chartType = "embedmini", [Summary("The default timeperiod you want to use.")]
            string chartTimePeriod = "monthly")
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (chartType == "help")
            {
                await ReplyAsync(
                    "Sets the global default for your server. `.fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'` command.");
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }


            if (!Enum.TryParse(chartType, true, out FmEmbedType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }


            if (!Enum.TryParse(chartTimePeriod, true, out ChartTimePeriod chartTimePeriodEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'weekly', 'monthly', 'yearly', or 'overall'.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, chartTimePeriodEnum, chartTypeEnum);

            await ReplyAsync("The .fmset default chart type for your server has been set to " + chartTypeEnum +
                             " with the time period " + chartTimePeriodEnum + ".");
            this.Context.LogCommandUsed();
        }

        [Command("serverreactions", RunMode = RunMode.Async)]
        [Summary("Sets reactions for some server commands.")]
        [Alias("serversetreactions")]
        public async Task SetGuildReactionsAsync(params string[] emotes)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (emotes.Count() > 3)
            {
                await ReplyAsync("Sorry, max amount emote reactions you can set is 3!");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (emotes.Length == 0)
            {
                await this._guildService.SetGuildReactionsAsync(this.Context.Guild, null);
                await ReplyAsync(
                    "Removed all server reactions!");
                this.Context.LogCommandUsed();
                return;
            }

            if (!this._guildService.ValidateReactions(emotes))
            {
                await ReplyAsync(
                    "Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                    "Please check if you have a space between every emote.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetGuildReactionsAsync(this.Context.Guild, emotes);

            var message = await ReplyAsync("Emote reactions have been set! \n" +
                                           "Please check if all reactions have been applied to this message correctly. If not, you might have used an emote from a different server.");
            await this._guildService.AddReactionsAsync(message, this.Context.Guild);
            this.Context.LogCommandUsed();
        }

        [Command("togglesupportermessages", RunMode = RunMode.Async)]
        [Summary("Sets reactions for some server commands.")]
        [Alias("togglesupporter", "togglesupporters", "togglesupport")]
        public async Task ToggleSupportMessagesAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var messagesDisabled = await this._guildService.ToggleSupporterMessagesAsync(this.Context.Guild);

            if (messagesDisabled == true)
            {
                await ReplyAsync(".fmbot supporter messages have been disabled. Supporters are still visible in `.fmsupporters`, but they will not be shown in `.fmchart` or other commands anymore.");
            }
            else
            {
                await ReplyAsync($".fmbot supporter messages have been re-enabled. These have a 1 in {Constants.SupporterMessageChance} chance of showing up on certain commands.");
            }

            this.Context.LogCommandUsed();
        }

        [Command("activitythreshold", RunMode = RunMode.Async)]
        [Summary("Sets amount of days to filter out users for inactivity")]
        [Alias("setactivitythreshold", "setthreshold")]
        public async Task SetWhoKnowsThresholdAsync([Remainder] string days = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            if (lastIndex == null)
            {
                await ReplyAsync("This server hasn't been indexed yet.\n" +
                                 $"Please run `.fmindex` to index this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrWhiteSpace(days))
            {
                await this._guildService.SetWhoKnowsThresholdDaysAsync(this.Context.Guild, null);

                await ReplyAsync("All registered users in this server will now be visible again in server-wide commands.");
                this.Context.LogCommandUsed();
                return;
            }

            var maxAmountOfDays = (DateTime.UtcNow - new DateTime(2020, 11, 4)).Days;

            if (int.TryParse(days, out var result))
            {
                if (result <= 1 || result > maxAmountOfDays)
                {
                    await ReplyAsync(
                        $"Please pick a value between 2 and {maxAmountOfDays} days.\n" +
                        $".fmbot only started storing user activity from November 4th 2020. It is not possible to set the activity filter before this date.\n");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }
            }
            else
            {
                await ReplyAsync("Please enter a valid amount of days. \n" +
                                 "All users that have not used .fmbot before that will be filtered from server wide commands.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetWhoKnowsThresholdDaysAsync(this.Context.Guild, result);

            await ReplyAsync($"Activity threshold has been set for this server.\n" +
                             $"All users that have not used .fmbot in the last {result} days are now filtered from all server-wide commands.");
            this.Context.LogCommandUsed();
        }

        [Command("block", RunMode = RunMode.Async)]
        [Summary("Block a user from appearing in server-wide commands")]
        [Alias("blockuser", "ban", "banuser")]
        public async Task GuildBlockUserAsync([Remainder] string user = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (user == null)
            {
                await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to block on your server.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var userToBlock = await this._settingService.GetDifferentUser(user);

            if (userToBlock == null)
            {
                await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Select(s => s.UserId).Contains(userToBlock.UserId))
            {
                await ReplyAsync("The user you're trying to block has already been blocked on this server.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userBlocked = await this._guildService.BlockGuildUserAsync(this.Context.Guild, userToBlock.UserId);

            if (userBlocked)
            {
                this._embed.WithTitle("Added blocked user");
                this._embed.WithDescription($"Discord user id: `{userToBlock.DiscordUserId}` (<@{userToBlock.DiscordUserId}>)\n" +
                                            $"Last.fm username: `{userToBlock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToBlock.UserId}`");

                this._embed.WithFooter($"See all blocked users with {prfx}blockedusers");

                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Something went wrong while attempting to block user, please contact .fmbot staff.");
                this.Context.LogCommandUsed(CommandResponse.Error);
            }
        }

        [Command("unblock", RunMode = RunMode.Async)]
        [Summary("Remove block from a user from appearing in server-wide commands")]
        [Alias("unblockuser", "unban", "unbanuser", "removeblock", "removeban")]
        public async Task GuildUnBlockUserAsync([Remainder] string user = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (user == null)
            {
                await ReplyAsync("Please mention a user, enter a discord id or enter a Last.fm username to unblock on your server.");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var userToUnblock = await this._settingService.GetDifferentUser(user);

            if (userToUnblock == null)
            {
                await ReplyAsync("User not found. Are you sure they are registered in .fmbot?");
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            if (guild.GuildBlockedUsers == null || !guild.GuildBlockedUsers.Select(s => s.UserId).Contains(userToUnblock.UserId))
            {
                await ReplyAsync("The user you're trying to unblock was not blocked on this server.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var userUnblocked = await this._guildService.UnBlockGuildUserAsync(this.Context.Guild, userToUnblock.UserId);

            if (userUnblocked)
            {
                this._embed.WithTitle("Unblocked user");
                this._embed.WithDescription($"Discord user id: `{userToUnblock.DiscordUserId}` (<@{userToUnblock.DiscordUserId}>)\n" +
                                            $"Last.fm username: `{userToUnblock.UserNameLastFM}`\n" +
                                            $".fmbot id: `{userToUnblock.UserId}`");

                this._embed.WithFooter($"See other blocked users with {prfx}blockedusers");

                await ReplyAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Something went wrong while attempting to block user, please contact .fmbot staff.");
                this.Context.LogCommandUsed(CommandResponse.Error);
            }
        }

        [Command("blockedusers", RunMode = RunMode.Async)]
        [Summary("Block a user from appearing in server-wide commands")]
        [Alias("blocked", "banned", "bannedusers")]
        public async Task BlockedUsersAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild?.LastIndexed == null)
            {
                await ReplyAsync($"Please run `{prfx}index` first.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any())
            {
                this._embed.WithTitle("Blocked users");
                var blockedUsers = "";

                foreach (var blockedUser in guild.GuildBlockedUsers)
                {
                    blockedUsers +=
                        $" - `{blockedUser.User.DiscordUserId}` (<@{blockedUser.User.DiscordUserId}>) | Last.fm: `{blockedUser.User.UserNameLastFM}`\n";
                }

                this._embed.WithDescription(blockedUsers);
                this._embed.WithFooter($"To add: {prfx}block mention/user id/last.fm username\n" +
                                        $"To remove: {prfx}unblock mention/user id/last.fm username");
            }

            await ReplyAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("export", RunMode = RunMode.Async)]
        [Summary("Gets Last.fm usernames from your server members in json format.")]
        [Alias("getmembers", "exportmembers")]
        public async Task GetMembersAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            try
            {
                var serverUsers = await this._guildService.FindAllUsersFromGuildAsync(this.Context);

                if (serverUsers.Count == 0)
                {
                    await ReplyAsync("No members found on this server.");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var userJson = JsonSerializer.Serialize(serverUsers, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await this.Context.User.SendFileAsync(StringToStream(userJson),
                    $"users_{this.Context.Guild.Name}_UTC-{DateTime.UtcNow:u}.json");

                await ReplyAsync("Check your DMs!");
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while creating an export.");
            }

        }

        /// <summary>
        /// Changes the prefix for the server.
        /// </summary>
        /// <param name="prefix">The desired prefix.</param>
        [Command("prefix", RunMode = RunMode.Async)]
        public async Task SetPrefixAsync(string prefix = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrEmpty(prefix) || prefix.ToLower() == "remove" || prefix.ToLower() == "delete")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(this.Context.Guild.Id);
                await ReplyAsync("Removed prefix!");
                this.Context.LogCommandUsed();
                return;
            }
            if (prefix.ToLower() == ".fm")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(this.Context.Guild.Id);
                await ReplyAsync("Reset to default prefix `.fm`!");
                this.Context.LogCommandUsed();
                return;
            }

            if (prefix.Length > 20)
            {
                await ReplyAsync("Max prefix length is 20 characters...");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }
            if (prefix.Contains("*") || prefix.Contains("`") || prefix.Contains("~"))
            {
                await ReplyAsync("You can't have a custom prefix that contains ** * **or **`** or **~**");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, prefix);
            this._prefixService.StorePrefix(prefix, this.Context.Guild.Id);

            this._embed.WithTitle("Successfully added custom prefix!");
            this._embed.WithDescription("Examples:\n" +
                                        $"- `{prefix}fm`\n" +
                                        $"- `{prefix}chart 8x8 monthly`\n" +
                                        $"- `{prefix}whoknows` \n \n" +
                                        "Reminder that you can always mention the bot followed by your command. \n" +
                                        $"The [.fmbot docs]({Constants.DocsUrl}) will still have the `.fm` prefix everywhere. " +
                                        $"Custom prefixes are still in the testing phase so please note that some error messages and other places might not show your prefix yet.\n\n" +
                                        $"To remove the custom prefix, do `{prefix}prefix remove`");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }


        /// <summary>
        /// Toggles commands for a server
        /// </summary>
        [Command("togglecommand", RunMode = RunMode.Async)]
        [Alias("togglecommands", "toggle")]
        public async Task ToggleCommand(string command = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var disabledCommands = await this._guildService.GetDisabledCommandsForGuild(this.Context.Guild);

            if (string.IsNullOrEmpty(command))
            {
                var description = new StringBuilder();
                if (disabledCommands != null)
                {
                    description.AppendLine("Currently disabled commands in this server:");
                    foreach (var disabledCommand in disabledCommands)
                    {
                        description.AppendLine($"- {disabledCommand}");
                    }
                }
                else
                {
                    description.Append("This server currently has all commands enabled. \n" +
                                  "To disable a command, enter the command name like this: `.fmtogglecommand chart`");
                }

                this._embed.WithDescription(description.ToString());
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to toggle commands. Only users with the 'Ban Members' permission, server admins or FMBot admins disable/enable commands.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            var searchResult = this._commands.Search(command.ToLower());

            if (searchResult.Commands == null || command.ToLower() == "togglecommand")
            {
                this._embed.WithDescription("No commands found or command can't be disabled.\n" +
                                            "Remember to remove the `.fm` prefix.");
                await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            if (disabledCommands != null && disabledCommands.Contains(command.ToLower()))
            {
                var newDisabledCommands = await this._guildService.RemoveDisabledCommandAsync(this.Context.Guild, command.ToLower());

                this._disabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

                this._embed.WithDescription($"Re-enabled command `{command.ToLower()}` for this server.");
            }
            else
            {
                var newDisabledCommands = await this._guildService.AddDisabledCommandAsync(this.Context.Guild, command.ToLower());

                this._disabledCommandService.StoreDisabledCommands(newDisabledCommands, this.Context.Guild.Id);

                this._embed.WithDescription($"Disabled command `{command.ToLower()}` for this server.");
            }

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }

        private static Stream StringToStream(string str)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(str);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
