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
using NetCord.Rest;
using NetCord.Services.Commands;
using Fergun.Interactive;

namespace FMBot.Bot.TextCommands;

public class DiscogsCommands(
    DiscogsBuilder discogsBuilder,
    IOptions<BotSettings> botSettings,
    UserService userService,
    DiscogsService discogsService,
    InteractiveService interactivity,
    SettingService settingService,
    IPrefixService prefixService)
    : BaseCommandModule(botSettings)
{
    private readonly DiscogsService _discogsService = discogsService;

    private InteractiveService Interactivity { get; } = interactivity;


    [Command("discogs")]
    [Summary("Connects your Discogs account.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [CommandCategories(CommandCategory.ThirdParty)]
    [UsernameSetRequired]
    public async Task DiscogsAsync([CommandParameter(Remainder = true)] string unusedValues = null)
    {
        var contextUser = await userService.GetUserWithDiscogs(this.Context.User.Id);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        if (contextUser.UserDiscogs == null)
        {
            if (this.Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue);

                serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
                await this.Context.Channel.SendMessageAsync(new MessageProperties().AddEmbeds(serverEmbed));
            }

            var response =
                discogsBuilder.DiscogsLoginGetLinkAsync(new ContextModel(this.Context, prfx, contextUser));
            var dmChannel = await this.Context.User.GetDMChannelAsync();
            await dmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        else
        {
            if (this.Context.Guild != null)
            {
                var serverEmbed = new EmbedProperties()
                    .WithColor(DiscordConstants.InformationColorBlue);

                serverEmbed.WithDescription("Check your DMs for a message to manage your connected Discogs account!");
                await this.Context.Channel.SendMessageAsync(new MessageProperties { Embeds = [serverEmbed] });
            }

            var response = DiscogsBuilder.DiscogsManage(new ContextModel(this.Context, prfx, contextUser));
            var manageDmChannel = await this.Context.User.GetDMChannelAsync();
            await manageDmChannel.SendMessageAsync(new MessageProperties
            {
                Embeds = [response.Embed],
                Components = [response.Components]
            });
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

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(searchValues, contextUser, this.Context);
        var collectionSettings = SettingService.SetDiscogsCollectionSettings(userSettings.NewSearchValue);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = await discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, collectionSettings, collectionSettings.NewSearchValue);

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

        var contextUser = await userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await settingService.GetUser(searchValues, contextUser, this.Context);
        var collectionSettings = SettingService.SetDiscogsCollectionSettings(userSettings.NewSearchValue);
        var prfx = prefixService.GetPrefix(this.Context.Guild?.Id);

        try
        {
            var response = await discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, prfx, contextUser), userSettings, collectionSettings, collectionSettings.NewSearchValue);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
