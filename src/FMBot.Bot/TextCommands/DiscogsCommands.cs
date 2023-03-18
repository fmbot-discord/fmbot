using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using static System.Text.RegularExpressions.Regex;

namespace FMBot.Bot.TextCommands;

public class DiscogsCommands : BaseCommandModule
{
    private readonly DiscogsBuilder _discogsBuilder;
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;
    private readonly SettingService _settingService;
    private readonly IPrefixService _prefixService;

    private InteractiveService Interactivity { get; }


    public DiscogsCommands(DiscogsBuilder discogsBuilder,
        IOptions<BotSettings> botSettings,
        UserService userService,
        DiscogsService discogsService,
        InteractiveService interactivity,
        SettingService settingService, IPrefixService prefixService) : base(botSettings)
    {
        this._discogsBuilder = discogsBuilder;
        this._userService = userService;
        this._discogsService = discogsService;
        this.Interactivity = interactivity;
        this._settingService = settingService;
        this._prefixService = prefixService;
    }

    [Command("discogs", RunMode = RunMode.Async)]
    [Summary("Connects your Discogs account.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [CommandCategories(CommandCategory.ThirdParty)]
    [UsernameSetRequired]
    [Alias("login discogs")]
    public async Task DiscogsAsync([Remainder] string unusedValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedBuilder()
                .WithColor(DiscordConstants.InformationColorBlue);

            serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
            await this.Context.Channel.SendMessageAsync("", false, serverEmbed.Build());
        }

        var response = await this._discogsBuilder.DiscogsLoginAsync(new ContextModel(this.Context, prfx, contextUser));
        this.Context.LogCommandUsed(response.CommandResponse);
    }

    [Command("collection", RunMode = RunMode.Async)]
    [Summary("Shows your Discogs collection")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    [Alias("coll", "vinyl", "discogscollection")]
    public async Task CollectionAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(searchValues, contextUser, this.Context);
        var collectionSettings = SettingService.SetDiscogsCollectionSettings(userSettings.NewSearchValue);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = await this._discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, collectionSettings, collectionSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
