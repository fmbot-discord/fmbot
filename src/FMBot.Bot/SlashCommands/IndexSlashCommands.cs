using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.Guild;
using Serilog;
using System.Text;
using FMBot.Bot.Builders;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord.Services.ApplicationCommands;
using NetCord;
using NetCord.Rest;
using Fergun.Interactive;

namespace FMBot.Bot.SlashCommands;

public class IndexSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
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
        await RespondAsync(InteractionCallback.DeferredMessage());

        try
        {
            var embed = new EmbedProperties();
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);
            if (guild.LastIndexed > DateTime.UtcNow.AddMinutes(-1))
            {
                embed.WithColor(DiscordConstants.InformationColorBlue);
                embed.WithDescription("This server has already been updated in the last minute, please wait.");
                await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                    .WithEmbeds([embed]));
                this.Context.LogCommandUsed(CommandResponse.Cooldown);
                return;
            }

            await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

            var guildUsers = new List<GuildUser>();
            await foreach (var user in this.Context.Guild.GetUsersAsync())
            {
                guildUsers.Add(user);
            }

            Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
                guildUsers.Count, this.Context.Guild.Id, this.Context.Guild.Name);

            var reply = new StringBuilder();

            var registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

            reply.AppendLine($"✅ Cached memberlist for server has been updated.");

            reply.AppendLine();
            reply.AppendLine($"This server has a total of {registeredUserCount} registered .fmbot members.");

            // TODO, show premium server role counts

            embed.WithColor(DiscordConstants.InformationColorBlue);
            embed.WithDescription(reply.ToString());

            await this.Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
                .WithEmbeds([embed]));

            // if (this.Context.Guild is Guild socketGuild)
            // {
            //     socketGuild.PurgeUserCache();
            // }

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }

    [SlashCommand("update", "Update .fmbot's cache manually with your latest Last.fm data", Contexts = [InteractionContextType.BotDMChannel, InteractionContextType.DMChannel,     InteractionContextType.Guild], IntegrationTypes = [ApplicationIntegrationType.UserInstall, ApplicationIntegrationType.GuildInstall])]
    [UsernameSetRequired]
    public async Task UpdateAsync(
        [SlashCommandParameter(Name = "type", Description = "Select what you want to update")]
        UpdateType updateTypeInput = UpdateType.RecentPlays)
    {
        var contextUser = await this._userService.GetUserWithFriendsAsync(this.Context.User);
        var updateType = SettingService.GetUpdateType(Enum.GetName(updateTypeInput), SupporterService.IsSupporter(contextUser.UserType));

        if (updateTypeInput == UpdateType.RecentPlays || updateType.updateType.HasFlag(UpdateType.RecentPlays))
        {
            var initialResponse = UserBuilder.UpdatePlaysInit(new ContextModel(this.Context, contextUser));
            await this.Context.SendResponse(this.Interactivity, initialResponse);

            var updatedResponse = await this._userBuilder.UpdatePlays(new ContextModel(this.Context, contextUser));
            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [updatedResponse.Embed];
                e.Components = [updatedResponse.Components];
            });
            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
        else
        {
            var initialResponse =
                this._userBuilder.UpdateOptionsInit(new ContextModel(this.Context, contextUser), updateType.updateType,
                    updateType.description);
            await this.Context.SendResponse(this.Interactivity, initialResponse);

            if (initialResponse.CommandResponse != CommandResponse.Ok)
            {
                this.Context.LogCommandUsed(initialResponse.CommandResponse);
                return;
            }

            var updatedResponse =
                await this._userBuilder.UpdateOptions(new ContextModel(this.Context, contextUser),
                    updateType.updateType);
            await this.Context.Interaction.ModifyResponseAsync(e =>
            {
                e.Embeds = [updatedResponse.Embed];
                e.Components = [updatedResponse.Components];
            });
            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
    }
}
