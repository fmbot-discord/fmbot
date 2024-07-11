using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models.Modals;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Flags;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.SlashCommands;

public class AdminSlashCommands : InteractionModuleBase
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;
    private readonly AlbumService _albumService;
    private readonly ArtistsService _artistService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly AliasService _aliasService;
    private readonly PlayService _playService;
    private readonly FriendsService _friendsService;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;
    private readonly IIndexService _indexService;

    public AdminSlashCommands(AdminService adminService, CensorService censorService, AlbumService albumService, ArtistsService artistService, IDataSourceFactory dataSourceFactory, AliasService aliasService, PlayService playService, FriendsService friendsService, UserService userService, SupporterService supporterService, IIndexService indexService)
    {
        this._adminService = adminService;
        this._censorService = censorService;
        this._albumService = albumService;
        this._artistService = artistService;
        this._dataSourceFactory = dataSourceFactory;
        this._aliasService = aliasService;
        this._playService = playService;
        this._friendsService = friendsService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._indexService = indexService;
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.CensorTypes)]
    public async Task SetCensoredArtist(string censoredId, string[] inputs)
    {
        var embed = new EmbedBuilder();

        var id = int.Parse(censoredId);
        var censoredMusic = await this._censorService.GetForId(id);

        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        foreach (var option in Enum.GetNames(typeof(CensorType)))
        {
            if (Enum.TryParse(option, out CensorType flag))
            {
                if (inputs.Any(a => a == option))
                {
                    censoredMusic.CensorType |= flag;
                }
                else
                {
                    censoredMusic.CensorType &= ~flag;
                }
            }
        }

        censoredMusic = await this._censorService.SetCensorType(censoredMusic, censoredMusic.CensorType);

        var description = new StringBuilder();

        if (censoredMusic.Artist)
        {
            description.AppendLine($"Artist: `{censoredMusic.ArtistName}`");
        }
        else
        {
            description.AppendLine($"Album: `{censoredMusic.AlbumName}` by `{censoredMusic.ArtistName}`");
        }

        description.AppendLine();
        description.AppendLine("Censored music entry has been updated to:");

        foreach (var flag in censoredMusic.CensorType.GetUniqueFlags())
        {
            if (censoredMusic.CensorType.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithFooter($"Adjusted by {this.Context.Interaction.User.Username}");
        await RespondAsync(embed: embed.Build());
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ArtistAlias)]
    public async Task SetArtistAliasOptions(string censoredId, string[] inputs)
    {
        var embed = new EmbedBuilder();

        var id = int.Parse(censoredId);
        var artistAlias = await this._aliasService.GetArtistAliasForId(id);

        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        foreach (var option in Enum.GetNames(typeof(AliasOption)))
        {
            if (Enum.TryParse(option, out AliasOption flag))
            {
                if (inputs.Any(a => a == option))
                {
                    artistAlias.Options |= flag;
                }
                else
                {
                    artistAlias.Options &= ~flag;
                }
            }
        }

        artistAlias = await this._aliasService.SetAliasOptions(artistAlias, artistAlias.Options);

        var description = new StringBuilder();

        description.AppendLine($"Artist: `{artistAlias.Artist.Name}`");
        description.AppendLine($"Alias: `{artistAlias.Alias}`");

        description.AppendLine();
        description.AppendLine("Artist alias has been updated to:");

        foreach (var flag in artistAlias.Options.GetUniqueFlags())
        {
            if (artistAlias.Options.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        this._aliasService.RemoveCache();

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        embed.WithFooter($"Adjusted by {this.Context.Interaction.User.Username}");
        await RespondAsync(embed: embed.Build());
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.GlobalWhoKnowsReport)]
    public async Task ReportGlobalWhoKnowsButton()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportGlobalWhoKnowsModal>(InteractionConstants.ModerationCommands.GlobalWhoKnowsReportModal);
    }

    [ModalInteraction(InteractionConstants.ModerationCommands.GlobalWhoKnowsReportModal)]
    public async Task ReportGlobalWhoKnowsButton(ReportGlobalWhoKnowsModal modal)
    {
        var positiveResponse = $"Thanks, your report for the user `{modal.UserNameLastFM}` has been received. \n" +
                               $"You will currently not be notified if your report is processed.";

        var existingBottedUser = await this._adminService.GetBottedUserAsync(modal.UserNameLastFM);
        if (existingBottedUser is { BanActive: true })
        {
            await RespondAsync(positiveResponse, ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var existingReport = await this._adminService.UserReportAlreadyExists(modal.UserNameLastFM);
        if (existingReport)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the user you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync(positiveResponse, ephemeral: true);

        var report =
            await this._adminService.CreateBottedUserReportAsync(this.Context.User.Id, modal.UserNameLastFM, modal.Note);
        await this._adminService.PostReport(report);
    }

    [ComponentInteraction("gwk-report-ban-*")]
    public async Task GwkReportBan(string reportId)
    {
        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        await this.Context.Interaction.RespondWithModalAsync<ReportGlobalWhoKnowsBanModal>($"gwk-report-ban-confirmed-{reportId}-{message.Id}");
    }

    [ComponentInteraction("gwk-report-deny-*")]
    public async Task GwkReportDeny(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync("Error, report was already processed.", ephemeral: true);
            return;
        }

        await this._adminService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync("Report response processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ModalInteraction("gwk-report-ban-confirmed-*-*")]
    public async Task GwkReportBanConfirmed(string reportId, string messageId, ReportGlobalWhoKnowsBanModal modal)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._adminService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            await RespondAsync("Error, report was already processed.", ephemeral: true);
            return;
        }

        await this._adminService.UpdateReport(report, ReportStatus.AcceptedWithComment, this.Context.User.Id);

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(report.UserNameLastFM);
        DateTimeOffset? age = null;
        if (userInfo != null && userInfo.Subscriber)
        {
            age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        }

        var result = await this._adminService.AddBottedUserAsync(report.UserNameLastFM, modal.Note, age?.DateTime);

        if (result)
        {
            await RespondAsync("Report response processed, thank you.", ephemeral: true);
        }
        else
        {
            await RespondAsync($"Something went wrong. Try checking with `.checkbotted {report.UserNameLastFM}`", ephemeral: true);
        }

        var parsedMessageId = ulong.Parse(messageId);
        var msg = await this.Context.Channel.GetMessageAsync(parsedMessageId);

        if (msg is not IUserMessage message)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Banned by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("gwk-filtered-user-to-ban-*")]
    public async Task GwkReportBanConfirmed(string filteredUserId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(filteredUserId);
        var filteredUser = await this._adminService.GetFilteredUserForIdAsync(id);

        var userInfo = await this._dataSourceFactory.GetLfmUserInfoAsync(filteredUser.UserNameLastFm);
        DateTimeOffset? age = null;
        if (userInfo != null && userInfo.Subscriber)
        {
            age = DateTimeOffset.FromUnixTimeSeconds(userInfo.RegisteredUnix);
        }

        var filterInfo = WhoKnowsFilterService.FilteredUserReason(filteredUser);

        var result = await this._adminService.AddBottedUserAsync(filteredUser.UserNameLastFm, filterInfo.ToString(), age?.DateTime);

        if (result)
        {
            await RespondAsync($"Converted filter for `{filteredUser.UserNameLastFm}` into gwk ban, thank you.", ephemeral: true);
        }
        else
        {
            await RespondAsync($"Something went wrong. Try checking again", ephemeral: true);
        }

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Converted to ban by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportAlbum)]
    public async Task ReportAlbum()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportAlbumModal>(InteractionConstants.ModerationCommands.ReportAlbumModal);
    }

    [ModalInteraction(InteractionConstants.ModerationCommands.ReportAlbumModal)]
    public async Task ReportAlbumButton(ReportAlbumModal modal)
    {
        var existingCensor = await this._censorService.AlbumResult(modal.AlbumName, modal.ArtistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync($"Thanks for your report, but the album cover you reported has already been marked as NSFW or censored.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var album = await this._albumService.GetAlbumFromDatabase(modal.ArtistName, modal.AlbumName);

        if (album == null)
        {
            await RespondAsync($"The album you tried to report does not exist in the .fmbot database (`{modal.AlbumName}` by `{modal.ArtistName}`). Someone needs to run the `album` command on it first.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.AlbumReportAlreadyExists(modal.ArtistName, modal.AlbumName);
        if (alreadyExists)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the album you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync($"Thanks, your report for the album `{modal.AlbumName}` by `{modal.ArtistName}` has been received.\n" +
                           $"You will currently not be notified if your report is processed.", ephemeral: true);

        var report = await this._censorService.CreateAlbumReport(this.Context.User.Id, modal.AlbumName, modal.ArtistName, modal.Note, album);
        await this._censorService.PostReport(report);
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.ReportArtist)]
    public async Task ReportArtist()
    {
        await this.Context.Interaction.RespondWithModalAsync<ReportArtistModal>(InteractionConstants.ModerationCommands.ReportArtistModal);
    }

    [ModalInteraction(InteractionConstants.ModerationCommands.ReportArtistModal)]
    public async Task ReportArtistButton(ReportArtistModal modal)
    {
        var existingCensor = await this._censorService.ArtistResult(modal.ArtistName);
        if (existingCensor != CensorService.CensorResult.Safe)
        {
            await RespondAsync($"Thanks for your report, but the artist image you reported has already been marked as NSFW or censored.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Censored);
            return;
        }

        var artist = await this._artistService.GetArtistFromDatabase(modal.ArtistName);

        if (artist == null)
        {
            await RespondAsync($"The artist you tried to report does not exist in the .fmbot database (`{modal.ArtistName}`). Someone needs to run the `artist` command on them first.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var alreadyExists = await this._censorService.ArtistReportAlreadyExists(modal.ArtistName);
        if (alreadyExists)
        {
            await RespondAsync($"Thanks, but there is already a pending report for the artist image you reported.", ephemeral: true);
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        await RespondAsync($"Thanks, your report for the artist `{modal.ArtistName}` has been received.\n" +
                           $"You will currently not be notified if your report is processed.", ephemeral: true);

        var report = await this._censorService.CreateArtistReport(this.Context.User.Id, modal.ArtistName, modal.Note, artist);
        await this._censorService.PostReport(report);
    }

    [ComponentInteraction("censor-report-mark-nsfw-*")]
    public async Task MarkReportNsfw(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        if (report.IsArtist)
        {
            var existing = await this._censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This artist already exists in the censor db, use `.checkartist` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddArtist(report.ArtistName, CensorType.ArtistImageNsfw);
        }
        else
        {
            var existing = await this._censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This album already exists in the censor db, use `.checkalbum` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverNsfw);
        }

        await this._censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync("Report response processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Marked NSFW by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("censor-report-mark-censored-*")]
    public async Task MarkReportCensored(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        if (report.IsArtist)
        {
            var existing = await this._censorService.GetCurrentArtist(report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This artist already exists in the censor db, use `.checkartist` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddArtist(report.ArtistName, CensorType.ArtistImageCensored);
        }
        else
        {
            var existing = await this._censorService.GetCurrentAlbum(report.AlbumName, report.ArtistName);
            if (existing != null)
            {
                await RespondAsync("This album already exists in the censor db, use `.checkalbum` instead!", ephemeral: true);
                return;
            }

            await this._censorService.AddAlbum(report.AlbumName, report.ArtistName, CensorType.AlbumCoverCensored);
        }

        await this._censorService.UpdateReport(report, ReportStatus.Accepted, this.Context.User.Id);

        await RespondAsync("Report processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Marked Censored by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Success);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction("censor-report-deny-*")]
    public async Task MarkReportDenied(string reportId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        var id = int.Parse(reportId);
        var report = await this._censorService.GetReportForId(id);

        if (report.ReportStatus != ReportStatus.Pending)
        {
            return;
        }

        await this._censorService.UpdateReport(report, ReportStatus.Denied, this.Context.User.Id);

        await RespondAsync("Report processed, thank you.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Denied by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction(InteractionConstants.ModerationCommands.MoveUserData)]
    public async Task MoveUserData(string oldUserId, string newUserId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        await RespondAsync("Moving data...", ephemeral: true);
        await this.Context.DisableInteractionButtons();

        try
        {
            await this._playService.MoveData(int.Parse(oldUserId), int.Parse(newUserId));

            await FollowupAsync("Moving data completed.", ephemeral: true);

            var message = (this.Context.Interaction as SocketMessageComponent)?.Message;

            if (message == null)
            {
                return;
            }

            var components =
                new ComponentBuilder().WithButton($"Moved by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
            await message.ModifyAsync(m => m.Components = components.Build());
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [ComponentInteraction($"admin-delete-user-*")]
    public async Task DeleteUser(string userId)
    {
        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            return;
        }

        await RespondAsync("Deleting user...", ephemeral: true);
        await this.Context.DisableInteractionButtons();

        await this._friendsService.RemoveAllFriendsAsync(int.Parse(userId));
        await this._friendsService.RemoveUserFromOtherFriendsAsync(int.Parse(userId));

        await this._userService.DeleteUser(int.Parse(userId));

        await FollowupAsync("Deleting user completed.", ephemeral: true);

        var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
        if (message == null)
        {
            return;
        }

        var components =
            new ComponentBuilder().WithButton($"Deleted by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
        await message.ModifyAsync(m => m.Components = components.Build());
    }

    [ComponentInteraction($"admin-add-oc-supporter-*-*")]
    public async Task AddSupporterAsync(string user = null, string openCollectiveId = null)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            await RespondAsync("Adding supporter...", ephemeral: true);

            if (!ulong.TryParse(user, out var discordUserId))
            {
                await RespondAsync("Invalid Discord ID", ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            _ = this.Context.Channel.TriggerTypingAsync();
            var userSettings = await this._userService.GetUserWithDiscogs(discordUserId);

            if (userSettings == null)
            {
                await RespondAsync("Couldn't find the Discord user, maybe they don't have an fmbot account", ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }
            if (userSettings.UserType != UserType.User && !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                await RespondAsync("Can only add this to users who have the `user` usertype, so no existing supporters. Maybe ask frik for help.", ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var openCollectiveSupporter = await this._supporterService.GetOpenCollectiveSupporter(openCollectiveId);
            if (openCollectiveSupporter == null)
            {
                await RespondAsync("OpenCollective user not found (this is not supposed to happen)", ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var existingSupporters = await this._supporterService.GetAllSupporters();
            if (existingSupporters
                    .Where(w => w.OpenCollectiveId != null)
                    .FirstOrDefault(f => f.OpenCollectiveId.ToLower() == openCollectiveId.ToLower()) != null)
            {
                await RespondAsync("OpenCollective user already connected to a different user", ephemeral: true);
                this.Context.LogCommandUsed(CommandResponse.NotFound);
                return;
            }

            var supporter = await this._supporterService.AddOpenCollectiveSupporter(userSettings.DiscordUserId, openCollectiveSupporter);

            await this._supporterService.ModifyGuildRole(userSettings.DiscordUserId);

            var embed = new EmbedBuilder();
            embed.WithTitle("Added new supporter");
            var description = new StringBuilder();
            description.AppendLine($"User id: {user} | <@{user}>\n" +
                                   $"Name: **{supporter.Name}**\n" +
                                   $"Subscription type: `{Enum.GetName(supporter.SubscriptionType.GetValueOrDefault())}`");

            description.AppendLine();
            description.AppendLine("✅ Full update started");

            embed.WithFooter("Name changes go through OpenCollective and apply within 24h");

            var discordUser = await this.Context.Client.GetUserAsync(discordUserId);
            if (discordUser != null)
            {
                await SupporterService.SendSupporterWelcomeMessage(discordUser, userSettings.UserDiscogs != null, supporter);

                description.AppendLine("✅ Thank you dm sent");
            }
            else
            {
                description.AppendLine("❌ Did not send thank you dm");
            }

            embed.WithDescription(description.ToString());

            await RespondAsync(embed: embed.Build());
            this.Context.LogCommandUsed();

            var message = (this.Context.Interaction as SocketMessageComponent)?.Message;
            if (message == null)
            {
                return;
            }

            var components =
                new ComponentBuilder().WithButton($"Added by {this.Context.Interaction.User.Username}", customId: "1", url: null, disabled: true, style: ButtonStyle.Danger);
            await message.ModifyAsync(m => m.Components = components.Build());

            _ = await this._indexService.IndexUser(userSettings);
        }
        else
        {
            await ReplyAsync(Constants.FmbotStaffOnly);
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }
}
