using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Commands
{
    [Name("Indexing")]
    public class IndexCommands : ModuleBase
    {
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly UserService _userService;

        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public IndexCommands(
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                IUpdateService updateService,
                UserService userService
            )
        {
            this._guildService = guildService;
            this._indexService = indexService;
            this._prefixService = prefixService;
            this._updateService = updateService;
            this._userService = userService;

            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }

        [Command("index", RunMode = RunMode.Async)]
        [Summary("Indexes top artists, albums and tracks for every user in your server.")]
        [Alias("i")]
        [GuildOnly]
        public async Task IndexGuildAsync()
        {
            _ = this.Context.Channel.TriggerTypingAsync();

            var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

            this._embed.WithDescription("<a:loading:821676038102056991> Server index started, this can take a while on larger servers...");
            var indexMessage = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            try
            {
                var guildUsers = await this.Context.Guild.GetUsersAsync();
                Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
                    guildUsers.Count, this.Context.Guild.Id, this.Context.Guild.Name);

                var usersToFullyUpdate = await this._indexService.GetUsersToFullyUpdate(guildUsers);
                int registeredUserCount;

                if (usersToFullyUpdate != null && usersToFullyUpdate.Count == 0 && lastIndex != null)
                {
                    registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

                    var reply = $"✅ Server index has been updated.\n\n" +
                                $"This server has a total {registeredUserCount} registered .fmbot members.";

                    await indexMessage.ModifyAsync(m =>
                    {
                        m.Embed = new EmbedBuilder()
                            .WithDescription(reply)
                            .WithColor(DiscordConstants.SuccessColorGreen)
                            .Build();
                    });

                    this.Context.LogCommandUsed();
                    return;
                }
                if (usersToFullyUpdate == null || usersToFullyUpdate.Count == 0 && lastIndex == null)
                {
                    registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                    await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow.AddDays(-1));
                    var reply =
                        "✅ Server has been indexed successfully.\n\n" +
                        $"This server has a total {registeredUserCount} registered .fmbot members.";

                    await indexMessage.ModifyAsync(m =>
                    {
                        m.Embed = new EmbedBuilder()
                            .WithDescription(reply)
                            .WithColor(DiscordConstants.SuccessColorGreen)
                            .Build();
                    });

                    this.Context.LogCommandUsed();
                    return;
                }

                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild);

                registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);
                this._indexService.AddUsersToIndexQueue(usersToFullyUpdate);

                await indexMessage.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription($"✅ Server index has been updated.\n\n" +
                                         $"This server has a total {registeredUserCount} registered .fmbot members.")
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                });

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync(
                    "Something went wrong while indexing users. Please report this issue.");
                await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);
            }
        }

        [Command("update", RunMode = RunMode.Async)]
        [Summary("Update user.")]
        [UsernameSetRequired]
        [Alias("u")]
        [GuildOnly]
        public async Task UpdateUserAsync(string force = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            if (force == "help")
            {
                this._embed.WithTitle($"{prfx}update");
                this._embed.WithDescription($"Updates your playcount cache on your latest scrobbles.\n" +
                                            $"Add `full` to fully update your account in case you edited your scrobble history.\n" +
                                            $"Note that updating also happens automatically.");

                this._embed.AddField("Examples",
                    $"`{prfx}update\n" +
                    $"`{prfx}update full`");

                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Help);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);

            if (force != null && (force.ToLower() == "f" || force.ToLower() == "-f" || force.ToLower() == "full" || force.ToLower() == "-force" || force.ToLower() == "force"))
            {
                if (PublicProperties.IssuesAtLastFm)
                {
                    await ReplyAsync(
                        "Doing a full update is disabled temporarily while Last.fm is having issues. Please try again later.");
                    this.Context.LogCommandUsed(CommandResponse.Disabled);
                    return;
                }

                if (userSettings.LastIndexed > DateTime.UtcNow.AddHours(-10))
                {
                    await ReplyAsync(
                        "You can't do a full index too often. Please remember that this command should only be used in case you edited your scrobble history.\n" +
                        "Experiencing issues with the normal update? Please contact us on the .fmbot support server.");
                    this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    return;
                }

                var msg = this.Context.Message as SocketUserMessage;
                if (StackCooldownTarget.Contains(this.Context.Message.Author))
                {
                    if (StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)].AddSeconds(30) >= DateTimeOffset.Now)
                    {
                        var secondsLeft = (int)(StackCooldownTimer[
                                StackCooldownTarget.IndexOf(this.Context.Message.Author as SocketGuildUser)]
                            .AddSeconds(31) - DateTimeOffset.Now).TotalSeconds;
                        if (secondsLeft <= 25)
                        {
                            await ReplyAsync("You recently started a full update. Please wait before starting a new one.");
                            this.Context.LogCommandUsed(CommandResponse.Cooldown);
                        }

                        return;
                    }

                    StackCooldownTimer[StackCooldownTarget.IndexOf(msg.Author)] = DateTimeOffset.Now;
                }
                else
                {
                    StackCooldownTarget.Add(msg.Author);
                    StackCooldownTimer.Add(DateTimeOffset.Now);
                }

                var indexDescription =
                    $"<a:loading:821676038102056991> Fully rebuilding playcount cache for user {userSettings.UserNameLastFM}..." +
                    $"\n\nThis can take a while. Please don't fully update too often, if you have any issues with the normal update feel free to let us know.";

                if (userSettings.UserType != UserType.User)
                {
                    indexDescription += "\n\n" +
                                        $"*As a thank you for being an .fmbot {userSettings.UserType.ToString().ToLower()} the bot will index the top 25k of your artists/albums/tracks (instead of top 4k/5k/6k).*";
                }

                this._embed.WithDescription(indexDescription);

                var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                await this._indexService.IndexUser(userSettings);

                await message.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription($"✅ {userSettings.UserNameLastFM} has been fully updated.")
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                });
            }
            else
            {
                if (userSettings.LastUpdated > DateTime.UtcNow.AddMinutes(-3))
                {
                    var recentlyUpdatedText =
                        $"Your cached playcounts have already been updated recently ({StringExtensions.GetTimeAgoShortString(userSettings.LastUpdated.Value)} ago). " +
                        $"Note that this also happens automatically for most commands.";

                    await ReplyAsync(recentlyUpdatedText);

                    this.Context.LogCommandUsed(CommandResponse.Cooldown);
                    return;
                }

                if (userSettings.LastIndexed == null)
                {
                    await ReplyAsync(
                        "Please do a full update first. (`.fmupdate full`)");
                    this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                    return;
                }

                this._embed.WithDescription(
                    $"<a:loading:821676038102056991> Updating user {userSettings.UserNameLastFM}...");
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

                        m.Embed = newEmbed.Build();
                    }
                    else
                    {
                        var updatedDescription =
                            $"✅ Cached playcounts have been updated for {userSettings.UserNameLastFM} based on {scrobblesUsed} new {StringExtensions.GetScrobblesString(scrobblesUsed)}.";

                        var rnd = new Random();
                        if (rnd.Next(0, 4) == 1)
                        {
                            updatedDescription +=
                                $"\n\n" +
                                $"Please note that updating only updates your cached playcounts and that users are also automatically updated.\n" +
                                $"Updating will not fix Spotify connection issues to Last.fm, especially since .fmbot is not affiliated with Last.fm. [*More info here..*](https://fmbot.xyz/faq/#commands-are-showing-the-wrong-songs-its-not-showing-what-i-listen-to-on-spotify)";
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
