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
using static System.Text.RegularExpressions.Regex;

namespace FMBot.Bot.TextCommands;

public class DiscogsCommands : BaseCommandModule
{
    private readonly DiscogsBuilder _discogsBuilder;
    private readonly UserService _userService;
    private readonly DiscogsService _discogsService;
    private readonly SettingService _settingService;

    private InteractiveService Interactivity { get; }


    public DiscogsCommands(DiscogsBuilder discogsBuilder,
        IOptions<BotSettings> botSettings,
        UserService userService,
        DiscogsService discogsService,
        InteractiveService interactivity,
        SettingService settingService) : base(botSettings)
    {
        this._discogsBuilder = discogsBuilder;
        this._userService = userService;
        this._discogsService = discogsService;
        this.Interactivity = interactivity;
        this._settingService = settingService;
    }

    [Command("discogs", RunMode = RunMode.Async)]
    [Summary("Connects your Discogs account.\n\n" +
             "Not receiving a DM? Please check if you have direct messages from server members enabled.")]
    [CommandCategories(CommandCategory.UserSettings)]
    [UsernameSetRequired]
    [Alias("login discogs")]
    public async Task DiscogsAsync([Remainder] string unusedValues = null)
    {
        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

        if (this.Context.Guild != null)
        {
            var serverEmbed = new EmbedBuilder()
                .WithColor(DiscordConstants.InformationColorBlue);

            serverEmbed.WithDescription("Check your DMs for a link to connect your Discogs account to .fmbot!");
            await this.Context.Channel.SendMessageAsync("", false, serverEmbed.Build());
        }

        var discogsAuth = await this._discogsService.GetDiscogsAuthLink();

        this._embed.WithDescription($"**[Click here to login to Discogs.]({discogsAuth.LoginUrl})**\n\n" +
                                    $"After authorizing .fmbot a code will be shown.\n" +
                                    $"**Copy the code and send it in this chat.**");
        this._embed.WithFooter($"Do not share the code outside of this DM conversation");
        this._embed.WithColor(DiscordConstants.InformationColorBlue);

        var dm = await this.Context.User.SendMessageAsync("", false, this._embed.Build());
        this._embed.Footer = null;

        var result = await this.Interactivity.NextMessageAsync(x => x.Channel.Id == dm.Channel.Id, timeout: TimeSpan.FromMinutes(15));

        if (!result.IsSuccess)
        {
            await this.Context.User.SendMessageAsync("Something went wrong while trying to connect your Discogs account.");
            this.Context.LogCommandUsed(CommandResponse.Error);
            return;
        }

        if (result.IsTimeout)
        {
            await dm.ModifyAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithDescription($"❌ Login failed.. link timed out.\n\n" +
                                        $"Re-run the `discogs` command to try again.")
                    .WithColor(DiscordConstants.WarningColorOrange)
                    .Build();
            });
            this.Context.LogCommandUsed(CommandResponse.NotFound);
            return;
        }

        if (result.Value?.Content == null || !IsMatch(result.Value.Content, @"^[a-zA-Z]+$") || result.Value.Content.Length != 10)
        {
            this._embed.WithDescription($"Login failed, incorrect input.\n\n" +
                                        $"Re-run the `discogs` command to try again.");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            await this.Context.User.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
            return;
        }

        var user = await this._discogsService.ConfirmDiscogsAuth(contextUser.UserId, discogsAuth, result.Value.Content);

        if (user.Identity != null)
        {
            await this._discogsService.StoreDiscogsAuth(contextUser.UserId, user.Auth, user.Identity);

            this._embed.WithDescription($"✅ Your Discogs account '[{user.Identity.Username}](https://www.discogs.com/user/{user.Identity.Username})' has been connected.\n" +
                                        $"Run the `.collection` command to view your collection.");
            await this.Context.User.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }
        else
        {
            this._embed.WithDescription($"Could not connect a Discogs account with provided code.\n\n" +
                                        $"Re-run the `discogs` command to try again.");
            this._embed.WithColor(DiscordConstants.WarningColorOrange);
            await this.Context.User.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed(CommandResponse.WrongInput);
        }
    }

    [Command("collection", RunMode = RunMode.Async)]
    [Summary("Shows your Discogs collection")]
    [UsernameSetRequired]
    [CommandCategories(CommandCategory.Other)]
    [Alias("coll", "vinyl", "discogscollection")]
    public async Task CollectionAsync([Remainder] string searchValues = null)
    {
        _ = this.Context.Channel.TriggerTypingAsync();

        var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
        var userSettings = await this._settingService.GetUser(searchValues, contextUser, this.Context);

        try
        {
            var response = await this._discogsBuilder.DiscogsCollectionAsync(new ContextModel(this.Context, "/", contextUser), userSettings, searchValues);

            await this.Context.SendResponse(this.Interactivity, response);
            this.Context.LogCommandUsed(response.CommandResponse);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e);
        }
    }
}
