using System;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.ThirdParty;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using NetCord.Services.Commands;
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

    [Command("discogs", "login discogs")]
    [Summary("Connects your Discogs account.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [CommandCategories(CommandCategory.ThirdParty)]
    [UsernameSetRequired]
    public async Task DiscogsAsync([CommandParameter(Remainder = true)] string unusedValues = null)
    {
        var contextUser = await this._userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser.UserDiscogs == null)
        {
            if (this.Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue);

                serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                await this.Context.Channel.SendMessageAsync("", false, serverEmbed.Build());
            }

            var response =
                this._discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, prfx, contextUser));
            await this.Context.User.SendMessageAsync("", false, response.Embed,
                components: response.Components);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        else
        {
            if (this.Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue);

                serverEmbed.WithDescription("Check your DMs for a message to manage your connected Discogs account!");
                await this.Context.Channel.SendMessageAsync("", embed: serverEmbed.Build());
            }

            var response = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, prfx, contextUser));
            await this.Context.User.SendMessageAsync("", false, response.Embed, components: response.Components);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
    }

    [Command("collection", "coll", "vinyl", "discogscollection")]
    [Summary("You or someone else their Discogs collection")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.ThirdParty)]
    public async Task CollectionAsync([CommandParameter(Remainder = true)] string searchValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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

    //[Command("whohas")]
    //[Summary("Shows who has the most Discogs merch of a certain artist in a server")]
    //[UsernameSetRequired]
    //[CommandCategories(CommandCategory.ThirdParty)]
    //[Alias("wh", "whohasvinyl")]
    public async Task WhoHasAsync([CommandParameter(Remainder = true)] string searchValues = null)
    {
        _ = this.Context.Channel?.TriggerTypingStateAsync()!;

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
