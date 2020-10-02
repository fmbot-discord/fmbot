using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
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

        private readonly IPrefixService _prefixService;
        private readonly IDisabledCommandService _disabledCommandService;

        private readonly CommandService _commands;

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public GuildCommands(IPrefixService prefixService,
            GuildService guildService,
            CommandService commands,
            IDisabledCommandService disabledCommandService)
        {
            this._prefixService = prefixService;
            this._guildService = guildService;
            this._commands = commands;
            this._disabledCommandService = disabledCommandService;
            this._adminService = new AdminService();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
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

        [Command("export", RunMode = RunMode.Async)]
        [Summary("Gets Last.FM usernames from your server members in json format.")]
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
                                        "Reminder that you can always ping the bot followed by your command. \n" +
                                        $"The [.fmbot docs]({Constants.DocsUrl}) will still have the `.fm` prefix everywhere. " +
                                        $"Custom prefixes are still in the testing phase so please note that some error messages and other places might not show your prefix yet.\n\n" +
                                        $"To remove the custom prefix, do `{prefix}prefix remove`");

            await ReplyAsync("", false, this._embed.Build()).ConfigureAwait(false);
            this.Context.LogCommandUsed();
        }


        /// <summary>
        /// Changes the prefix for the server.
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
                var description = "";
                if (disabledCommands != null)
                {
                    description += "Currently disabled commands in this server:\n";
                    foreach (var disabledCommand in disabledCommands)
                    {
                        description += $"- {disabledCommand}\n";
                    }
                }
                else
                {
                    description = "This server currently has all commands enabled. \n" +
                                  "To disable a command, enter the command name like this: `.fmtogglecommand chart`";
                }

                this._embed.WithDescription(description);
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
