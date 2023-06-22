using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Serilog;

namespace FMBot.Bot.TextCommands;

[Name("Indexing")]
public class IndexCommands : BaseCommandModule
{
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly IUpdateService _updateService;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;

    private static readonly List<DateTimeOffset> StackCooldownTimer = new();
    private static readonly List<SocketUser> StackCooldownTarget = new();

    public IndexCommands(
        GuildService guildService,
        IIndexService indexService,
        IPrefixService prefixService,
        IUpdateService updateService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        SupporterService supporterService) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._updateService = updateService;
        this._userService = userService;
        this._supporterService = supporterService;
    }

    [Command("refreshmembers", RunMode = RunMode.Async)]
    [Summary("Refreshes the cached member list that .fmbot has for your server.")]
    [Alias("i", "index", "refresh", "cachemembers", "refreshserver", "serverset")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task IndexGuildAsync()
    {
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

        this._embed.WithDescription("<a:loading:821676038102056991> Updating memberlist, this can take a while on larger servers...");
        var indexMessage = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

        try
        {
            var guildUsers = await this.Context.Guild.GetUsersAsync();

            Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
                guildUsers.Count, this.Context.Guild.Id, this.Context.Guild.Name);

            var usersToFullyUpdate = await this._indexService.GetUsersToFullyUpdate(guildUsers);
            var reply = new StringBuilder();

            var registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

            await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

            reply.AppendLine($"✅ Cached memberlist for server has been updated.");

            reply.AppendLine();
            reply.AppendLine($"This server has a total of {registeredUserCount} registered .fmbot members.");

            // TODO: add way to see role filtering stuff here

            await indexMessage.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription(reply.ToString())
                    .WithColor(DiscordConstants.SuccessColorGreen)
                    .Build();
            });

            this.Context.LogCommandUsed();

            if (usersToFullyUpdate != null && usersToFullyUpdate.Count != 0)
            {
                this._indexService.AddUsersToIndexQueue(usersToFullyUpdate);
            }
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
            await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);
        }
    }

    [Command("update", RunMode = RunMode.Async)]
    [Summary("Updates a users cached playcounts based on their recent plays\n\n" +
             "This command also has an option to completely refresh a users cache (`full`). This is recommended if you have edited your scrobble history.")]
    [Examples("update", "update full")]
    [Alias("u")]
    [GuildOnly]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    public async Task UpdateUserAsync(string force = null)
    {
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (force != null && (force.ToLower() == "f" || force.ToLower() == "-f" || force.ToLower() == "full" || force.ToLower() == "-force" || force.ToLower() == "force"))
        {
            if (PublicProperties.IssuesAtLastFm)
            {
                var issues = "";
                if (PublicProperties.IssuesAtLastFm && PublicProperties.IssuesReason != null)
                {
                    issues = "\n\n" +
                             "Note:\n" +
                             $"*\"{PublicProperties.IssuesReason}\"*";
                }

                await ReplyAsync(
                    $"Doing a full update is disabled temporarily while Last.fm is having issues. Please try again later.{issues}",
                    allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            if (contextUser.LastIndexed > DateTime.UtcNow.AddHours(-1))
            {
                await ReplyAsync(
                    "You can't do a full index too often. Please remember that this command should only be used in case you edited your scrobble history.\n" +
                    $"Experiencing issues with the normal update? Please contact us on the .fmbot support server. \n\n" +
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? Please use `{prfx}outofsync` for help.");
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
                $"<a:loading:821676038102056991> Fully rebuilding playcount cache for user {contextUser.UserNameLastFM}..." +
                $"\n\nThis can take a while. Note that you can only do a full update once a day.";

            if (contextUser.UserType != UserType.User)
            {
                indexDescription += "\n\n" +
                                    $"*Thanks for being an .fmbot {contextUser.UserType.ToString().ToLower()}. " +
                                    $"Your full Last.fm history will now be cached, so this command might take slightly longer...*";
            }

            this._embed.WithDescription(indexDescription);

            var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            var stats = await this._indexService.IndexUser(contextUser);

            var description = UserService.GetIndexCompletedUserStats(contextUser, stats);

            await message.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription(description.description)
                    .WithColor(DiscordConstants.SuccessColorGreen)
                    .Build(); 
                m.Components = description.promo
                    ? new ComponentBuilder()
                        .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: SupporterService.GetSupporterLink())
                        .Build()
                    : null;
            });
        }
        else
        {
            if (contextUser.LastUpdated > DateTime.UtcNow.AddMinutes(-1))
            {
                var recentlyUpdatedText =
                    $"Your cached playcounts have already been updated recently ({StringExtensions.GetTimeAgoShortString(contextUser.LastUpdated.Value)} ago). \n\n" +
                    $"Any commands that require updating will also update your playcount automatically.\n\n" +
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? Please use `{prfx}outofsync` for help.";

                this._embed.WithDescription(recentlyUpdatedText);
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                return;
            }

            if (contextUser.LastIndexed == null)
            {
                await ReplyAsync(
                    "Just logged in to the bot? Please wait a little bit and try again later, since the bot is still fetching all your Last.fm data.\n\n" +
                    $"If you keep getting this message, please try using `{prfx}update full` once.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            this._embed.WithDescription(
                $"<a:loading:821676038102056991> Fetching {contextUser.UserNameLastFM}'s latest scrobbles...");
            var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            var update = await this._updateService.UpdateUserAndGetRecentTracks(contextUser);

            var supporterPromo =
                await this._supporterService.GetPromotionalUpdateMessage(contextUser, prfx, this.Context.Client, this.Context.Guild?.Id);
            await message.ModifyAsync(m =>
            {
                if (update.Content.NewRecentTracksAmount == 0 && update.Content.RemovedRecentTracksAmount == 0)
                {
                    var updateDescription = new StringBuilder();
                    updateDescription.AppendLine($"No new scrobbles found on [your Last.fm profile]({Constants.LastFMUserUrl}{contextUser.UserNameLastFM}) since last update. ");
                    updateDescription.AppendLine();

                    if (contextUser.LastUpdated.HasValue)
                    {
                        var specifiedDateTime = DateTime.SpecifyKind(contextUser.LastUpdated.Value, DateTimeKind.Utc);
                        var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                        updateDescription.AppendLine($"Last update: <t:{dateValue}:R>.");
                    }

                    var latestScrobble = update.Content.RecentTracks.MaxBy(o => o.TimePlayed);
                    if (latestScrobble != null && latestScrobble.TimePlayed.HasValue)
                    {
                        var specifiedDateTime = DateTime.SpecifyKind(latestScrobble.TimePlayed.Value, DateTimeKind.Utc);
                        var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                        updateDescription.AppendLine($"Last scrobble: <t:{dateValue}:R>.");
                    }


                    updateDescription.AppendLine();
                    updateDescription.AppendLine($"Using Spotify and having problems with your music not being tracked or it lagging behind? Please use `{prfx}outofsync` for help.");

                    var newEmbed =
                        new EmbedBuilder()
                            .WithDescription(updateDescription.ToString())
                            .WithColor(DiscordConstants.SuccessColorGreen);

                    m.Embed = newEmbed.Build();
                }
                else
                {
                    string updatedDescription;

                    if (update.Content.RemovedRecentTracksAmount == 0)
                    {
                        updatedDescription = $"✅ Cached playcounts have been updated for {contextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)}.";
                    }
                    else
                    {
                        updatedDescription = $"✅ Cached playcounts have been updated for {contextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)} " +
                                             $"and {update.Content.RemovedRecentTracksAmount} removed {StringExtensions.GetScrobblesString(update.Content.RemovedRecentTracksAmount)}.";
                    }

                    if (supporterPromo != null)
                    {
                        updatedDescription += $"\n\n{supporterPromo}";
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
