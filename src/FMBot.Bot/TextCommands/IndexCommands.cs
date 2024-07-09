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
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
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
             "You can also update parts of your cached playcounts by using one of the options")]
    [Options("Full", "Plays", "Artists", "Albums", "Tracks")]
    [Examples("update", "update full")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.UserSettings)]
    [Alias("u")]
    public async Task UpdateAsync([Remainder] string options = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        var updateType = SettingService.GetUpdateType(options);

        if (!updateType.optionPicked)
        {
            this._embed.WithDescription(
                $"<a:loading:821676038102056991> Fetching **{contextUser.UserNameLastFM}**'s latest scrobbles...");
            var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            var update = await this._updateService.UpdateUserAndGetRecentTracks(contextUser);

            var updatePromo =
                await this._supporterService.GetPromotionalUpdateMessage(contextUser, prfx, this.Context.Client, this.Context.Guild?.Id);
            var upgradeButton = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink);

            await message.ModifyAsync(m =>
            {
                if (GenericEmbedService.RecentScrobbleCallFailed(update))
                {
                    m.Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(update, contextUser.UserNameLastFM).Build();
                    this.Context.LogCommandWithLastFmError(update.Error);
                    return;
                }

                var updatedDescription = new StringBuilder();

                if (update.Content.NewRecentTracksAmount == 0 && update.Content.RemovedRecentTracksAmount == 0)
                {
                    var previousUpdate = DateTime.SpecifyKind(contextUser.LastUpdated.Value, DateTimeKind.Utc);
                    var previousUpdateValue = ((DateTimeOffset)previousUpdate).ToUnixTimeSeconds();

                    updatedDescription.AppendLine($"Nothing new found on [your Last.fm profile]({LastfmUrlExtensions.GetUserUrl(contextUser.UserNameLastFM)}) since last update (<t:{previousUpdateValue}:R>)");

                    if (update.Content?.RecentTracks != null && update.Content.RecentTracks.Any())
                    {
                        if (!update.Content.RecentTracks.Any(a => a.NowPlaying))
                        {
                            var latestScrobble = update.Content.RecentTracks.MaxBy(o => o.TimePlayed);
                            if (latestScrobble != null && latestScrobble.TimePlayed.HasValue)
                            {
                                var specifiedDateTime = DateTime.SpecifyKind(latestScrobble.TimePlayed.Value, DateTimeKind.Utc);
                                var dateValue = ((DateTimeOffset)specifiedDateTime).ToUnixTimeSeconds();

                                updatedDescription.AppendLine();
                                updatedDescription.AppendLine($"Last scrobble: <t:{dateValue}:R>.");
                            }

                            updatedDescription.AppendLine();
                            updatedDescription.AppendLine($"Last.fm not keeping track of your Spotify properly? Try the instructions in `{prfx}outofsync` for help.");
                        }
                    }
                    else
                    {
                        if (updatePromo.message != null)
                        {
                            updatedDescription.AppendLine();
                            updatedDescription.AppendLine(updatePromo.message);
                            if (updatePromo.showUpgradeButton)
                            {
                                m.Components = upgradeButton.Build();
                            }
                        }
                    }

                    var newEmbed =
                        new EmbedBuilder()
                            .WithDescription(updatedDescription.ToString())
                            .WithColor(DiscordConstants.SuccessColorGreen);

                    m.Embed = newEmbed.Build();
                }
                else
                {
                    if (update.Content.RemovedRecentTracksAmount == 0)
                    {
                        updatedDescription.AppendLine($"✅ Cached playcounts have been updated for {contextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)}.");
                    }
                    else
                    {
                        updatedDescription.AppendLine($"✅ Cached playcounts have been updated for {contextUser.UserNameLastFM} based on {update.Content.NewRecentTracksAmount} new {StringExtensions.GetScrobblesString(update.Content.NewRecentTracksAmount)} " +
                                             $"and {update.Content.RemovedRecentTracksAmount} removed {StringExtensions.GetScrobblesString(update.Content.RemovedRecentTracksAmount)}.");
                    }

                    if (update.Content.NewRecentTracksAmount < 25)
                    {
                        var random = new Random().Next(0, 8);
                        if (random == 1)
                        {
                            updatedDescription.AppendLine();
                            updatedDescription.AppendLine("Note that .fmbot also updates you automatically once every 48 hours.");
                        }
                        if (random == 2)
                        {
                            updatedDescription.AppendLine();
                            updatedDescription.AppendLine("Any commands that require you to be updated will also update you automatically.");
                        }
                    }

                    if (updatePromo.message != null)
                    {
                        updatedDescription.AppendLine();
                        updatedDescription.AppendLine(updatePromo.message);
                        if (updatePromo.showUpgradeButton)
                        {
                            m.Components = upgradeButton.Build();
                        }
                    }

                    m.Embed = new EmbedBuilder()
                        .WithDescription(updatedDescription.ToString())
                        .WithColor(DiscordConstants.SuccessColorGreen)
                        .Build();
                }
            });

            this.Context.LogCommandUsed();
            return;
        }
        else
        {
            if (PublicProperties.IssuesAtLastFm)
            {
                var issueDescription = new StringBuilder();

                issueDescription.AppendLine(
                    "Doing an advanced update is disabled temporarily while Last.fm is having issues. Please try again later.");
                if (PublicProperties.IssuesReason != null)
                {
                    issueDescription.AppendLine();
                    issueDescription.AppendLine("Note:");
                    issueDescription.AppendLine($"*{PublicProperties.IssuesReason}*");
                }

                await ReplyAsync(issueDescription.ToString(), allowedMentions: AllowedMentions.None);
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var indexStarted = this._indexService.IndexStarted(contextUser.UserId);

            if (!indexStarted)
            {
                await ReplyAsync("An advanced update has recently been started for you. Please wait before starting a new one.");
                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                return;
            }

            if (contextUser.LastIndexed > DateTime.UtcNow.AddMinutes(-30))
            {
                await ReplyAsync(
                    "You can't do full updates too often. These are only meant to be used when your Last.fm history has been adjusted.\n\n" +
                    $"Using Spotify and having problems with your music not being tracked or it lagging behind? Please use `{prfx}outofsync` for help. Spotify sync issues can't be fixed inside of .fmbot.");
                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                return;
            }

            var indexDescription = new StringBuilder();
            indexDescription.AppendLine($"<a:loading:821676038102056991> Fetching Last.fm playcounts for user {contextUser.UserNameLastFM}...");
            indexDescription.AppendLine();
            indexDescription.AppendLine("The following playcount caches are being rebuilt:");
            indexDescription.AppendLine(updateType.description);

            if (contextUser.UserType != UserType.User)
            {
                indexDescription.AppendLine($"*Thanks for being an .fmbot {contextUser.UserType.ToString().ToLower()}. " +
                                            $"Your full Last.fm history will now be cached, so this command might take slightly longer...*");
            }

            this._embed.WithDescription(indexDescription.ToString());

            var message = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

            if (!updateType.updateType.HasFlag(UpdateType.Full) && !updateType.updateType.HasFlag(UpdateType.AllPlays))
            {
                var update = await this._updateService.UpdateUserAndGetRecentTracks(contextUser, bypassIndexPending: true);

                if (GenericEmbedService.RecentScrobbleCallFailed(update))
                {
                    await message.ModifyAsync(m => m.Embed = GenericEmbedService.RecentScrobbleCallFailedBuilder(update, contextUser.UserNameLastFM).Build());
                    this.Context.LogCommandWithLastFmError(update.Error);
                    return;
                }
            }

            var result = await this._indexService.ModularUpdate(contextUser, updateType.updateType);

            var description = UserService.GetIndexCompletedUserStats(contextUser, result);

            await message.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription(description.description)
                    .WithColor(result.UpdateError != true ? DiscordConstants.SuccessColorGreen : DiscordConstants.WarningColorOrange)
                    .Build();
                m.Components = description.promo
                    ? new ComponentBuilder()
                        .WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link, url: Constants.GetSupporterDiscordLink)
                        .Build()
                    : null;
            });

            this.Context.LogCommandUsed();
            return;
        }
    }
}
