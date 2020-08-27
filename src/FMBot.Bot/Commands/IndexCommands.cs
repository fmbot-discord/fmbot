using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands
{
    public class IndexCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly GuildService _guildService;
        private readonly UserService _userService;
        private readonly IIndexService _indexService;
        private readonly IUpdateService _updateService;
        private readonly Logger.Logger _logger;

        public IndexCommands(Logger.Logger logger,
            IIndexService indexService,
            IUpdateService updateService,
            GuildService guildService,
            UserService userService)
        {
            this._logger = logger;
            this._indexService = indexService;
            this._updateService = updateService;
            this._guildService = guildService;
            this._userService = userService;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
        }

        [Command("index", RunMode = RunMode.Async)]
        [Summary("Indexes top artists, albums and tracks for every user in your server.")]
        public async Task IndexGuildAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            try
            {
                var guildUsers = await this.Context.Guild.GetUsersAsync();
                var users = await this._indexService.GetUsersToIndex(guildUsers);
                var indexedUserCount = await this._indexService.GetIndexedUsersCount(guildUsers);

                var guildOnCooldown =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);

                var guildRecentlyIndexed =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-TimeSpan.FromMinutes(3));

                if (guildRecentlyIndexed)
                {
                    await ReplyAsync("An index was recently started on this server. Please wait before running this command again.");
                    return;
                }
                if (users.Count == 0 && lastIndex != null)
                {
                    await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

                    var reply =
                        $"No new registered .fmbot members found on this server or all users have already been indexed in the last {Constants.GuildIndexCooldown.TotalHours} hours.\n" +
                        $"Stored guild users have been updated.";

                    if (guildOnCooldown)
                    {
                        var timeTillIndex = lastIndex.Value.Add(Constants.GuildIndexCooldown) - DateTime.UtcNow;
                        reply +=
                            $"\nAll users in this server can be updated again in {(int)timeTillIndex.TotalHours} hours and {timeTillIndex:mm} minutes";
                    }
                    await ReplyAsync(reply);
                    return;
                }
                if (users.Count == 0 && lastIndex == null)
                {
                    await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow.AddDays(-1));
                    await ReplyAsync("All users on this server have already been indexed or nobody is registered on .fmbot here.\n" +
                                     "The server has now been registered anyway, so you can start using the commands that require indexing.");
                }

                string usersString = "";
                if (guildOnCooldown)
                {
                    usersString = "new ";
                }

                if (users.Count == 1)
                {
                    usersString += "user";
                }
                else
                {
                    usersString += "users";
                }

                this._embed.WithTitle($"Added {users.Count} {usersString} to bot indexing queue");

                var expectedTime = TimeSpan.FromSeconds(2 * users.Count);
                var indexStartedReply =
                    $"Indexing stores users their all time top {Constants.ArtistsToIndex} artists. \n\n" +
                    $"`{users.Count}` new users or users with expired artists added to queue.";

                if (expectedTime.TotalMinutes >= 2)
                {
                    indexStartedReply += $" This will take approximately {(int)expectedTime.TotalMinutes} minutes.";
                }

                indexStartedReply += $"\n`{indexedUserCount}` users already indexed on this server.\n \n" +
                                     "*Note: You will currently not be alerted when the index is finished.*";

                this._embed.WithDescription(indexStartedReply);

                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this._logger.LogCommandUsed(this.Context.Guild?.Id, this.Context.Channel.Id, this.Context.User.Id,
                    this.Context.Message.Content);

                await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                this._indexService.IndexGuild(users);
            }
            catch (Exception e)
            {
                this._logger.LogError(e.Message, this.Context.Message.Content, this.Context.User.Username,
                    this.Context.Guild?.Name, this.Context.Guild?.Id);
                await ReplyAsync(
                    "Something went wrong while indexing users. Please let us know as this feature is in beta.");
                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);
            }
        }

        [Command("update", RunMode = RunMode.Async)]
        [Summary("Update user.")]
        [LoginRequired]
        public async Task UpdateUserAsync(string force = null)
        {
            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);


            if (force != null && (force.ToLower() == "f" || force.ToLower() == "force"))
            {
                await this._indexService.IndexUser(userSettings);
            }
            else
            {
                await this._updateService.UpdateUser(userSettings);
            }

            await ReplyAsync("You have been updated");
        }
    }
}
