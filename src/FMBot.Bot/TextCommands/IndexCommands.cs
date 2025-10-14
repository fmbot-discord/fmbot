using System;
using System.Text;
using System.Threading.Tasks;

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
    private readonly UserService _userService;
    private readonly UserBuilder _userBuilder;

    private InteractiveService Interactivity { get; }

    public IndexCommands(
        GuildService guildService,
        IndexService indexService,
        IPrefixService prefixService,
        UserService userService,
        IOptions<BotSettings> botSettings,
        UserBuilder userBuilder,
        InteractiveService interactivity) : base(botSettings)
    {
        this._guildService = guildService;
        this._indexService = indexService;
        this._prefixService = prefixService;
        this._userService = userService;
        this._userBuilder = userBuilder;
        this.Interactivity = interactivity;
    }

    [Command("refreshmembers", RunMode = RunMode.Async)]
    [Summary("Refreshes the cached member list that .fmbot has for your server.")]
    [Alias("index", "refresh", "cachemembers", "refreshserver", "serverset", "refresh members")]
    [GuildOnly]
    [CommandCategories(CommandCategory.ServerSettings)]
    public async Task IndexGuildAsync()
    {
        var lastIndex = await this._guildService.GetGuildIndexTimestampAsync(this.Context.Guild);

        if (lastIndex > DateTime.UtcNow.AddMinutes(-1))
        {
            this._embed.WithColor(DiscordConstants.InformationColorBlue);
            this._embed.WithDescription("This server has already been updated in the last minute, please wait.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.Cooldown);
            return;
        }

        this._embed.WithDescription(
            "<a:loading:821676038102056991> Updating memberlist, this can take a while on larger servers...");
        var indexMessage = await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());

        try
        {
            await this._guildService.UpdateGuildIndexTimestampAsync(this.Context.Guild, DateTime.UtcNow);

            var guildUsers = await this.Context.Guild.GetUsersAsync();

            Log.Information("Downloaded {guildUserCount} users for guild {guildId} / {guildName} from Discord",
                guildUsers.Count, this.Context.Guild.Id, this.Context.Guild.Name);

            var reply = new StringBuilder();
            var registeredUserCount = await this._indexService.StoreGuildUsers(this.Context.Guild, guildUsers);

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

            if (this.Context.Guild is SocketGuild socketGuild)
            {
                socketGuild.PurgeUserCache();
            }
            this.Context.LogCommandUsed();

            // if (usersToFullyUpdate != null && usersToFullyUpdate.Count != 0)
            // {
            //     this._indexService.AddUsersToIndexQueue(usersToFullyUpdate);
            // }
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
    [SupporterEnhanced("Supporters get their lifetime data cached in the bot, so all the commands that rely on this have the most complete data")]
    [Alias("u")]
    public async Task UpdateAsync([Remainder] string options = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
        var updateType = SettingService.GetUpdateType(options, SupporterService.IsSupporter(contextUser.UserType));

        if (!updateType.optionPicked)
        {
            var initialResponse = UserBuilder.UpdatePlaysInit(new ContextModel(this.Context, prfx, contextUser));
            var message = await this.Context.SendResponse(this.Interactivity,initialResponse);

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
            var message = await this.Context.SendResponse(this.Interactivity,initialResponse);

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
