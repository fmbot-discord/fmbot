using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Models.TemplateOptions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Builders;

public class TemplateBuilders
{
    private readonly TemplateService _templateService;
    private readonly UserService _userService;
    private readonly SettingService _settingService;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly GuildService _guildService;

    public TemplateBuilders(TemplateService templateService, UserService userService, SettingService settingService, IDataSourceFactory dataSourceFactory, GuildService guildService)
    {
        this._templateService = templateService;
        this._userService = userService;
        this._settingService = settingService;
        this._dataSourceFactory = dataSourceFactory;
        this._guildService = guildService;
    }

    public async Task<ResponseModel> TemplatePicker(
        ContextModel context,
        Guild guild = null)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var templates = await this._templateService.GetTemplates(context.ContextUser.UserId);

        var templateManagePicker = new SelectMenuBuilder()
            .WithPlaceholder("Select template to change")
            .WithCustomId(InteractionConstants.Template.ManagePicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var template in templates)
        {
            templateManagePicker.AddOption(new SelectMenuOptionBuilder(template.Name, $"{InteractionConstants.Template.ManagePicker}-{template.Id}"));
        }

        var templateGlobalPicker = new SelectMenuBuilder()
            .WithPlaceholder("Template you use globally")
            .WithCustomId(InteractionConstants.Template.SetGlobalDefaultPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        var templateGuildPicker = new SelectMenuBuilder()
            .WithPlaceholder("Template you use in this server")
            .WithCustomId(InteractionConstants.Template.SetGuildDefaultPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        response.Embed.WithTitle("Manage templates");
        response.Embed.WithDescription("Select the template you want to change, or pick which one you want used as a default.");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        response.Components = new ComponentBuilder()
            .WithSelectMenu(templateManagePicker)
            .WithButton("Create", InteractionConstants.Template.Create, ButtonStyle.Secondary)
            .WithButton("Import sharecode", InteractionConstants.Template.ImportCode, ButtonStyle.Secondary)
            .WithButton("Import script", InteractionConstants.Template.ImportScript, ButtonStyle.Secondary);

        return response;
    }

    public async Task<(ResponseModel response, ResponseModel extraResponse)> TemplateManage(
        ContextModel context,
        int templateId,
        Guild guild)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var template = await this._templateService.GetTemplate(templateId);

        var templateOptionPicker = new SelectMenuBuilder()
            .WithPlaceholder("Select embed option you want to change")
            .WithCustomId(InteractionConstants.Template.SetOptionPicker)
            .WithMinValues(1)
            .WithMaxValues(1);

        var exampleUserSettings = await this._settingService
            .GetOriginalContextUser(context.ContextUser.DiscordUserId, context.ContextUser.DiscordUserId, context.DiscordGuild, context.DiscordUser);
        var recentTracks =
            await this._dataSourceFactory.GetRecentTracksAsync(context.ContextUser.UserNameLastFM, useCache: true);

        if (GenericEmbedService.RecentScrobbleCallFailed(recentTracks))
        {
            return (GenericEmbedService.RecentScrobbleCallFailedResponse(recentTracks, context.ContextUser.UserNameLastFM), null);
        }

        guild ??= await this._guildService.GetGuildAsync(821660544581763093);
        var guildUsers = await this._guildService.GetGuildUsers(guild.DiscordGuildId);

        var fmEmbed = await this._userService.GetTemplateFmAsync(context.ContextUser.UserId, exampleUserSettings, recentTracks.Content.RecentTracks[0],
            recentTracks.Content.RecentTracks[1], context.ContextUser.TotalPlaycount ?? 100, guild, guildUsers);

        foreach (var option in ((EmbedOption[])Enum.GetValues(typeof(EmbedOption))))
        {
            var name = option.GetAttribute<OptionAttribute>().Name;
            var value = Enum.GetName(option);

            fmEmbed.Content.TryGetValue(option, out var description);

            templateOptionPicker.AddOption(new SelectMenuOptionBuilder(name, value, StringExtensions.TruncateLongString(description, 95) ?? "Not set"));
        }

        response.Components = new ComponentBuilder()
            .WithSelectMenu(templateOptionPicker)
            .WithButton("Rename", $"{InteractionConstants.Template.Rename}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Copy", $"{InteractionConstants.Template.Copy}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Script", $"{InteractionConstants.Template.ViewScript}-{template.Id}", ButtonStyle.Secondary)
            .WithButton("Variables", $"{InteractionConstants.Template.ViewVariables}", ButtonStyle.Secondary)
            .WithButton("Delete", $"{InteractionConstants.Template.Delete}-{template.Id}", ButtonStyle.Danger);

        response.Embed.WithAuthor($"Editing template '{template.Name}'");
        response.Embed.WithDescription($"Sharecode: `{template.ShareCode}`");
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        var extraResponse = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
            Embed = fmEmbed.EmbedBuilder
        };

        return (response, extraResponse);
    }

    public static ResponseModel GetTemplateVariables(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        var description = new StringBuilder();
        foreach (var option in TemplateOptions.Options.OrderBy(o => o.Variable))
        {
            description.AppendLine($"**`{option.Variable}`** - {option.Description}");
        }

        response.Embed.WithAuthor("Template variables");
        response.Embed.WithDescription(description.ToString());
        response.Embed.WithColor(DiscordConstants.InformationColorBlue);

        return response;
    }

    public static ResponseModel TemplatesSupporterRequired(ContextModel context)
    {
        if (SupporterService.IsSupporter(context.ContextUser.UserType))
        {
            return null;
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed
        };

        response.Embed.WithDescription($"Only supporters can configure templates and fully customize their fm commands.\n\n" +
                                       $"[Get supporter here]({Constants.GetSupporterDiscordLink}).");

        response.Components = new ComponentBuilder().WithButton(Constants.GetSupporterButton, style: ButtonStyle.Link,
            url: Constants.GetSupporterDiscordLink);

        response.Embed.WithColor(DiscordConstants.InformationColorBlue);
        response.CommandResponse = CommandResponse.SupporterRequired;

        return response;
    }
}
