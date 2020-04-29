using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;

namespace FMBot.Bot.Commands
{
    [Summary("Server Staff Only")]
    public class GuildCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly IGuildService _guildService;

        private readonly IPrefixService _prefixService;

        private readonly Logger.Logger _logger;

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public GuildCommands(IPrefixService prefixService, Logger.Logger logger, IGuildService guildService, FMBotDbContext db)
        {
            this._prefixService = prefixService;
            this._logger = logger;
            this._guildService = guildService;
            this._adminService = new AdminService(db);
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
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
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                return;
            }

            if (chartType == "help")
            {
                await ReplyAsync(
                    "Sets the global default for your server. `.fmserverset 'embedfull/embedmini/textfull/textmini' 'Weekly/Monthly/Yearly/AllTime'` command.");
                return;
            }


            if (!Enum.TryParse(chartType, true, out FmEmbedType chartTypeEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'embedmini', 'embedfull', 'textfull', or 'textmini'.");
                return;
            }


            if (!Enum.TryParse(chartTimePeriod, true, out ChartTimePeriod chartTimePeriodEnum))
            {
                await ReplyAsync("Invalid mode. Please use 'weekly', 'monthly', 'yearly', or 'overall'.");
                return;
            }

            await this._guildService.ChangeGuildSettingAsync(this.Context.Guild, chartTimePeriodEnum, chartTypeEnum);

            await ReplyAsync("The .fmset default chart type for your server has been set to " + chartTypeEnum +
                             " with the time period " + chartTimePeriodEnum + ".");
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id, this.Context.Message.Content);
        }

        [Command("serverreactions", RunMode = RunMode.Async)]
        [Summary("Sets reactions for some server commands.")]
        [Alias("serversetreactions")]
        public async Task SetGuildReactionsAsync(params string[] emotes)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                return;
            }

            if (emotes.Count() > 3)
            {
                await ReplyAsync("Sorry, max amount emote reactions you can set is 3!");
                return;
            }

            if (emotes.Length == 0)
            {
                await this._guildService.SetGuildReactionsAsync(this.Context.Guild, null);
                await ReplyAsync(
                    "Removed all server reactions!");
                return;
            }

            if (!this._guildService.ValidateReactions(emotes))
            {
                await ReplyAsync(
                    "Sorry, one or multiple of your reactions seems invalid. Please try again.\n" +
                    "Please check if you have a space between every emote.");
                return;
            }

            await this._guildService.SetGuildReactionsAsync(this.Context.Guild, emotes);

            var message = await ReplyAsync("Emote reactions have been set! \n" +
                                           "Please check if all reactions have been applied to this message correctly. If not, you might have used an emote from a different server.");
            await this._guildService.AddReactionsAsync(message, Context.Guild);
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id, this.Context.Message.Content);
        }

        [Command("export", RunMode = RunMode.Async)]
        [Summary("Gets Last.FM usernames from your server members in json format.")]
        [Alias("getmembers", "exportmembers")]
        public async Task GetMembersAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                return;
            }

            try
            {
                var serverUsers = await this._guildService.FindAllUsersFromGuildAsync(this.Context);

                if (serverUsers.Count == 0)
                {
                    await ReplyAsync("No members found on this server.");
                    return;
                }

                var userJson = JsonSerializer.Serialize(serverUsers, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                await this.Context.User.SendFileAsync(StringToStream(userJson),
                    $"users_{this.Context.Guild.Name}_UTC-{DateTime.UtcNow:u}.json");

            }
            catch (Exception e)
            {
                await ReplyAsync(
                    "Something went wrong while creating an export.");
            }

            await ReplyAsync("Check your DMs!");
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id, this.Context.Message.Content);
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
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                return;
            }

            if (string.IsNullOrEmpty(prefix) || prefix.ToLower() == "remove" || prefix.ToLower() == "delete")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(Context.Guild.Id);
                await ReplyAsync("Removed prefix!");
                return;
            }
            if (prefix.ToLower() == ".fm")
            {
                await this._guildService.SetGuildPrefixAsync(this.Context.Guild, null);
                this._prefixService.RemovePrefix(Context.Guild.Id);
                await ReplyAsync("Reset to default prefix `.fm`!");
                return;
            }

            if (prefix.Length > 20)
            {
                await ReplyAsync("Max prefix length is 20 characters...");
                return;
            }
            if (prefix.Contains("*") || prefix.Contains("`") || prefix.Contains("~"))
            {
                await ReplyAsync("You can't have a custom prefix that contains ** * **or **`** or **~**");
                return;
            }

            await this._guildService.SetGuildPrefixAsync(this.Context.Guild, prefix);
            this._prefixService.StorePrefix(prefix, Context.Guild.Id);

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
            this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id, this.Context.Message.Content);
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
