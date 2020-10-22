using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;

namespace FMBot.Bot.Commands
{
    public class IndexCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private readonly GuildService _guildService;
        private readonly UserService _userService;
        private readonly IIndexService _indexService;
        private readonly IUpdateService _updateService;

        private readonly IPrefixService _prefixService;

        public IndexCommands(
            IIndexService indexService,
            IUpdateService updateService,
            GuildService guildService,
            UserService userService,
            IPrefixService prefixService)
        {
            this._indexService = indexService;
            this._updateService = updateService;
            this._guildService = guildService;
            this._userService = userService;
            this._prefixService = prefixService;
            this._embedFooter = new EmbedFooterBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFMColorRed);
        }

        [Command("index", RunMode = RunMode.Async)]
        [Summary("Indexes top artists, albums and tracks for every user in your server.")]
        [Alias("i")]
        public async Task IndexGuildAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            try
            {
                var guildUsers = await this.Context.Guild.GetUsersAsync();
                var users = await this._indexService.GetUsersToIndex(guildUsers);
                var indexedUserCount = await this._indexService.GetIndexedUsersCount(guildUsers);

                var guildRecentlyIndexed =
                    lastIndex != null && lastIndex > DateTime.UtcNow.Add(-TimeSpan.FromMinutes(60));

                if (guildRecentlyIndexed)
                {
                    await ReplyAsync("An index was recently started on this server. Please wait before running this command again.");
                    this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    return;
                }
                if (users.Count == 0 && lastIndex != null)
                {
                    await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

                    var reply =
                        "Stored guild users have been updated.\n" +
                        "No new registered .fmbot members found on this server or all users have already been indexed. To update your indexed artist/albums/tracks, use `.fmupdate` (also happens automatically).";

                    await ReplyAsync(reply);
                    this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    return;
                }
                if (users.Count == 0 && lastIndex == null)
                {
                    await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow.AddDays(-1));
                    await ReplyAsync("All users on this server have already been indexed or nobody is registered on .fmbot here.\n" +
                                     "The server has now been registered anyway, so you can start using the commands that require indexing.");
                    this.Context.LogCommandUsed();
                    return;
                }

                string usersString = "";
                if (users.Count == 1)
                {
                    usersString += "user";
                }
                else
                {
                    usersString += "users";
                }

                this._embed.WithTitle($"Added {users.Count} {usersString} to bot indexing queue");

                var expectedTime = TimeSpan.FromSeconds(8 * users.Count);
                var indexStartedReply =
                    $"Indexing stores which .fmbot members are on your server and stores their initial top artist, albums and tracks. " +
                    $"Updating these records happens automatically, but you can also use `.fmupdate` to update your own account.\n" +
                    $"Confused about how indexing has been changed? [Please read this.](https://fmbot.xyz/commands/whoknows/)\n\n" +
                    $"`{users.Count}` new users or users that have never been indexed added to index queue.";

                indexStartedReply += $"\n`{indexedUserCount}` users already indexed on this server.\n \n";

                if (expectedTime.TotalMinutes >= 1)
                {
                    indexStartedReply += $"**This will take approximately {(int)expectedTime.TotalMinutes} minutes. \n" +
                                         $"⚠️ Commands might display incomplete results until this process is done.**\n";
                }

                indexStartedReply += "*Note: You will currently not be alerted when the index is finished.*";

                this._embed.WithDescription(indexStartedReply);

                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild);

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                this.Context.LogCommandUsed();

                await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

                this._indexService.AddUsersToIndexQueue(users);
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while indexing users. Please let us know as this feature is in beta.");
                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);
            }
        }

        [Command("update", RunMode = RunMode.Async)]
        [Summary("Update user.")]
        [UsernameSetRequired]
        [Alias("u")]
        public async Task UpdateUserAsync(string force = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            if (force == "help")
            {
                this._embed.WithTitle($"{prfx}update");
                this._embed.WithDescription($"Updates your top artists/albums/genres based on your latest scrobbles.\n" +
                                            $"Add `full` to fully update your account in case you edited your scrobble history.\n" +
                                            $"Note that updating also happens automatically.");

                this._embed.AddField("Examples",
                    $"`{prfx}update\n" +
                    $"`{prfx}update full`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User, true);
            //if (userSettings.LastUpdated > DateTime.UtcNow.AddMinutes(-3))
            //{
            //    await ReplyAsync(
            //        "You have already been updated recently. Note that this also happens automatically.");
            //    this.Context.LogCommandUsed(CommandResponse.Cooldown);
            //    return;
            //}

            if (force != null && (force.ToLower() == "f" || force.ToLower() == "-f" || force.ToLower() == "full" || force.ToLower() == "-force" || force.ToLower() == "force"))
            {
                if (userSettings.LastUpdated < DateTime.UtcNow.AddDays(-2))
                {
                    await ReplyAsync(
                        "You can't do a full index too often. Please remember that this command should only be used in case you edited your scrobble history.\n" +
                        "Experiencing issues with the normal update? Please contact us on the .fmbot support server.");
                    this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    return;
                }

                this._embed.WithDescription($"<a:loading:749715170682470461> Fully indexing user {userSettings.UserNameLastFM}..." +
                                            $"\n\nThis can take a while. Please don't fully update too often, if you have any issues with the normal update feel free to let us know.");

                var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                await this._indexService.IndexUser(userSettings);

                await message.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription($"✅ {userSettings.UserNameLastFM} has been fully indexed.")
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                });
            }
            else
            {
                if (userSettings.LastIndexed == null)
                {
                    await ReplyAsync(
                        "You have to be indexed before you can update. (`.fmupdate full`)");
                    this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }

                this._embed.WithDescription($"<a:loading:749715170682470461> Updating user {userSettings.UserNameLastFM}...");
                var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                var scrobblesUsed = await this._updateService.UpdateUser(userSettings);

                await message.ModifyAsync(m =>
                {
                    if (scrobblesUsed == 0)
                    {
                        var newEmbed =
                            new EmbedBuilder()
                                .WithDescription("No new scrobbles found since last update")
                                .WithColor(DiscordConstants.SuccessColorGreen);

                        if (userSettings.LastUpdated.HasValue)
                        {
                            newEmbed.WithTimestamp(userSettings.LastUpdated.Value);
                            this._embedFooter.WithText("Last update");
                            newEmbed.WithFooter(this._embedFooter);
                        }
                        m.Embed =  newEmbed.Build();
                    }
                    else
                    {
                        var updatedDescription =
                            $"✅ {userSettings.UserNameLastFM} has been updated based on {scrobblesUsed} new scrobbles.";

                        var rnd = new Random();
                        if (rnd.Next(0, 4) == 1)
                        {
                            updatedDescription +=
                                $"\n\n" +
                                $"Please note that updates are only used for whoknows and that users are also automatically updated every 48 hours.\n" +
                                $"Other commands directly get their data from last.fm and are always up to date.";
                        }
    
                        m.Embed = new EmbedBuilder()
                            .WithDescription(updatedDescription)
                            .WithColor(DiscordConstants.SuccessColorGreen)
                            .Build();
                    }

                    
                });
            }

            this.Context.LogCommandUsed();
        }
    }
}
