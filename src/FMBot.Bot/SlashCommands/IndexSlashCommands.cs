using System.Threading.Tasks;
using System;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.Guild;
using Serilog;
using System.Text;
using Discord;
using FMBot.Bot.Builders;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class IndexSlashCommands : InteractionModuleBase
{
    private InteractiveService Interactivity { get; }
    private readonly GuildService _guildService;
    private readonly IndexService _indexService;
    private readonly UserBuilder _userBuilder;
    private readonly UserService _userService;

    public IndexSlashCommands(InteractiveService interactivity, GuildService guildService, IndexService indexService,
        UserBuilder userBuilder, UserService userService)
    {
        this.Interactivity = interactivity;
        this._guildService = guildService;
        this._indexService = indexService;
        this._userBuilder = userBuilder;
        this._userService = userService;
    }

    [SlashCommand("refreshmembers", "Refreshes the cached member list that .fmbot has for your server")]
    public async Task RefreshMembersAsync()
    {
        _ = DeferAsync();

        try
        {
            var guildUsers = await this.Context.Guild.GetUsersAsync();

            Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
                guildUsers.Count, this.Context.Guild.Id, this.Context.Guild.Name);

            var reply = new StringBuilder();

            var registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

            await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

            reply.AppendLine($"✅ Cached memberlist for server has been updated.");

            reply.AppendLine();
            reply.AppendLine($"This server has a total of {registeredUserCount} registered .fmbot members.");

            // TODO, show premium server role counts

            var embed = new EmbedBuilder();
            embed.WithColor(DiscordConstants.InformationColorBlue);
            embed.WithDescription(reply.ToString());

            await FollowupAsync(null, [embed.Build()]);

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("update", "Update .fmbot's cache manually with your latest Last.fm data")]
    [UsernameSetRequired]
    public async Task UpdateAsync(
        [Summary("type", "Select what you want to update")]
        UpdateType updateTypeInput = UpdateType.RecentPlays)
    {
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);
        var updateType = SettingService.GetUpdateType(Enum.GetName(updateTypeInput));

        if (updateType.updateType.HasFlag(UpdateType.RecentPlays))
        {
            var initialResponse = this._userBuilder.UpdatePlaysInit(new ContextModel(this.Context, contextUser));
            await this.Context.Interaction.RespondAsync(embed: initialResponse.Embed.Build(),
                components: initialResponse.Components?.Build());

            var updatedResponse = await this._userBuilder.UpdatePlays(new ContextModel(this.Context, contextUser));
            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = updatedResponse.Embed.Build();
                e.Components = updatedResponse.Components?.Build();
            });
            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
        else
        {
            var initialResponse =
                this._userBuilder.UpdateOptionsInit(new ContextModel(this.Context, contextUser), updateType.updateType,
                    updateType.description);
            await this.Context.Interaction.RespondAsync(embed: initialResponse.Embed.Build(),
                components: initialResponse.Components?.Build());

            if (initialResponse.CommandResponse != CommandResponse.Ok)
            {
                this.Context.LogCommandUsed(initialResponse.CommandResponse);
                return;
            }

            var updatedResponse =
                await this._userBuilder.UpdateOptions(new ContextModel(this.Context, contextUser), updateType.updateType);
            await this.Context.Interaction.ModifyOriginalResponseAsync(e =>
            {
                e.Embed = updatedResponse.Embed.Build();
                e.Components = updatedResponse.Components?.Build();
            });
            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
    }
}
