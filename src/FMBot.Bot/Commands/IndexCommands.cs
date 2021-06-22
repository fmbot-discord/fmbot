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
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.Commands
{
    [Name("Indexing")]
    public class IndexCommands : BaseCommandModule
    {
        private readonly GuildService _guildService;
        private readonly IIndexService _indexService;
        private readonly IPrefixService _prefixService;
        private readonly IUpdateService _updateService;
        private readonly UserService _userService;

        private static readonly List<DateTimeOffset> StackCooldownTimer = new();
        private static readonly List<SocketUser> StackCooldownTarget = new();

        public IndexCommands(
                GuildService guildService,
                IIndexService indexService,
                IPrefixService prefixService,
                IUpdateService updateService,
                UserService userService,
                IOptions<BotSettings> botSettings) : base(botSettings)
        {
            this._guildService = guildService;
            this._indexService = indexService;
            this._prefixService = prefixService;
            this._updateService = updateService;
            this._userService = userService;
        }

        [Command("index", RunMode = RunMode.Async)]
        [Summary("Refreshed the cached member list that .fmbot has for your server.")]
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
                                $"This server has a total of {registeredUserCount} registered .fmbot members.";

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
                        $"This server has a total of {registeredUserCount} registered .fmbot members.";

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

                await indexMessage.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription($"✅ Server index has been updated.\n\n" +
                                         $"This server has a total of {registeredUserCount} registered .fmbot members.")
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                });

                this.Context.LogCommandUsed();

                this._indexService.AddUsersToIndexQueue(usersToFullyUpdate);
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
        [Summary("Updates a users cached playcounts based on their recent plays. \n\n" +
                 "This command also has an option to completely refresh a users cache (`full`). This is recommended if you have edited your scrobble history.")]
        [Examples("update", "update full")]
        [Alias("u")]
        [GuildOnly]
        [UsernameSetRequired]
        public async Task UpdateUserAsync(string force = null)
        {
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
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
                    $"\n\nThis can take a while. Note that you can only do a full update once a day.";

                if (userSettings.UserType != UserType.User)
                {
                    indexDescription += "\n\n" +
                                        $"*As a thank you for being an .fmbot {userSettings.UserType.ToString().ToLower()} the bot will index the top 25k of your artists/albums/tracks (instead of top 4k/5k/6k).*";
                }

                this._embed.WithDescription(indexDescription);

                var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

                await this._indexService.IndexUser(userSettings);

                var updatedDescription = $"✅ {userSettings.UserNameLastFM} has been fully updated.";

                var rnd = new Random();
                if (rnd.Next(0, 4) == 1 && userSettings.UserType == UserType.User)
                {
                    updatedDescription += "\n\n" +
                                          $"*Did you know that .fmbot stores the top 25k artists/albums/tracks instead of the top 4k/5k/6k for supporters? See {prfx}donate on how to become an .fmbot supporter.*";
                }

                await message.ModifyAsync(m =>
                {
                    m.Embed = new EmbedBuilder()
                        .WithDescription(updatedDescription)
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                });
            }
            else
            {
                if (userSettings.LastUpdated > DateTime.UtcNow.AddMinutes(-3))
                {
                    var recentlyUpdatedText =
                        $"Your cached playcounts have already been updated recently ({StringExtensions.GetTimeAgoShortString(userSettings.LastUpdated.Value)} ago). \n\n" +
                        $"Any commands that require updating will also update your playcount automatically.\n\n" +
                        $"Note that updating will not fix Spotify connection issues to Last.fm, especially since .fmbot is not affiliated with Last.fm. " +
                        $"[*More info here..*]({Constants.SpotifyStuckFaqDocsUrl})";

                    this._embed.WithDescription(recentlyUpdatedText);
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
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
                                .WithDescription("No new scrobbles found since last update\n\n" +
                                                 $"Note that updating will not fix Spotify connection issues to Last.fm, especially since .fmbot is not affiliated with Last.fm.  " +
                                                 $"[*More info here..*]({Constants.SpotifyStuckFaqDocsUrl})")
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
                                $"Any commands that require updating will also update your playcount automatically.\n\n" +
                                $"Note that updating will not fix Spotify connection issues to Last.fm, especially since .fmbot is not affiliated with Last.fm. " +
                                $"[*More info here..*]({Constants.SpotifyStuckFaqDocsUrl})";
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
