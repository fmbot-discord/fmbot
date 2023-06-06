using System.Threading.Tasks;
using System;
using Discord.Interactions;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Services.Guild;
using Serilog;
using System.Text;
using Discord;
using FMBot.Bot.Resources;

namespace FMBot.Bot.SlashCommands;

public class IndexSlashCommands : InteractionModuleBase
{
    private InteractiveService Interactivity { get; }
    private readonly GuildService _guildService;
    private readonly IIndexService _indexService;
    private readonly IPrefixService _prefixService;

    public IndexSlashCommands(InteractiveService interactivity, GuildService guildService, IIndexService indexService, IPrefixService prefixService)
    {
        this.Interactivity = interactivity;
        this._guildService = guildService;
        this._indexService = indexService;
        this._prefixService = prefixService;
    }

    [SlashCommand("refreshmembers", "Refreshes the cached member list that .fmbot has for your server")]
    [UsernameSetRequired]
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

            await FollowupAsync(null, new[] { embed.Build() });

            this.Context.LogCommandUsed();
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
