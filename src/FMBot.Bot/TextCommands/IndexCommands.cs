using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
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
    private readonly IndexService _indexService;
    private readonly IPrefixService _prefixService;
    private readonly UpdateService _updateService;
    private readonly UserService _userService;
    private readonly SupporterService _supporterService;
    private readonly UserBuilder _userBuilder;


    public IndexCommands(
        GuildService guildService,
        IndexService indexService,
        IPrefixService prefixService,
        UpdateService updateService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        SupporterService supporterService,
        UserBuilder userBuilder) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._updateService = updateService;
        this._userService = userService;
        this._supporterService = supporterService;
        this._userBuilder = userBuilder;
    }

    [Command("refreshmembers", RunMode = RunMode.Async)]
    [Summary("Refreshes the cached member list that .fmbot has for your server.")]
    [Alias("i", "index", "refresh", "cachemembers", "refreshserver", "serverset")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task IndexGuildAsync()
    {
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

        this._embed.WithDescription(
            "<a:loading:821676038102056991> Updating memberlist, this can take a while on larger servers...");
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
            var initialResponse = this._userBuilder.UpdatePlaysInit(new ContextModel(this.Context, prfx, contextUser));
            var message = await this.Context.Channel.SendMessageAsync(embed: initialResponse.Embed.Build());

            var updatedResponse =
                await this._userBuilder.UpdatePlays(new ContextModel(this.Context, prfx, contextUser));

            await message.ModifyAsync(m =>
            {
                m.Embed = updatedResponse.Embed.Build();
                m.Components = updatedResponse.Components?.Build();
            });

            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
        else
        {
            var initialResponse = this._userBuilder.UpdateOptionsInit(new ContextModel(this.Context, prfx, contextUser),
                updateType.updateType, updateType.description);
            var message = await this.Context.Channel.SendMessageAsync(embed: initialResponse.Embed.Build());

            if (initialResponse.CommandResponse != CommandResponse.Ok)
            {
                this.Context.LogCommandUsed(initialResponse.CommandResponse);
                return;
            }

            var updatedResponse =
                await this._userBuilder.UpdateOptions(new ContextModel(this.Context, prfx, contextUser),
                    updateType.updateType);

            await message.ModifyAsync(m =>
            {
                m.Embed = updatedResponse.Embed.Build();
                m.Components = updatedResponse.Components?.Build();
            });

            this.Context.LogCommandUsed(updatedResponse.CommandResponse);
        }
    }
}
