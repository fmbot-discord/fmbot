using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fergun.Interactive;
using Fergun.Interactive.Pagination;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Bot.Services.Guild.Renderers;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord;
using NetCord.Rest;
using StringExtensions = FMBot.Bot.Extensions.StringExtensions;

namespace FMBot.Bot.Builders;

public class AutopostBuilders(AutopostService autopostService, GuildService guildService)
{
    public const string PremiumFeatureDescription =
        "**Server autoposts** automatically post recaps and top charts to channels or threads on a schedule. " +
        "Filter them to a role or artist, and open the full list from every post.";

    public async Task<ResponseModel> Overview(ContextModel context)
    {
        if (!PublicProperties.PremiumServers.ContainsKey(context.DiscordGuild.Id))
        {
            return PremiumSettingBuilder.PremiumServerRequired("autoposts", PremiumFeatureDescription);
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var guild = await guildService.GetGuildAsync(context.DiscordGuild.Id);
        var autoposts = await autopostService.GetAutopostsForGuild(guild.GuildId);

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Server autoposts");
        container.WithSeparator();

        var description = new StringBuilder();
        description.AppendLine(
            "Automatically posts server listening content on a schedule: recaps or top artist, album and track charts. " +
            "Autoposts can target channels and threads, be filtered to a role or artist, and include a button that opens the full chart.");

        if (autoposts.Count == 0)
        {
            description.AppendLine();
            description.AppendLine("⚠️ No autoposts set up yet. Add one below to get started.");
        }

        container.WithTextDisplay(description.ToString());

        foreach (var autopost in autoposts)
        {
            container.AddComponent(new ComponentSectionProperties(
                new ButtonProperties($"{InteractionConstants.Autopost.Edit}:{autopost.Id}", "Edit",
                    ButtonStyle.Secondary))
            {
                Components = [new TextDisplayProperties(GetAutopostSummary(autopost))]
            });
        }

        container.WithSeparator();

        var buttons = new ActionRowProperties();
        buttons.AddComponents(new ButtonProperties(InteractionConstants.Autopost.Add, "Add autopost",
            ButtonStyle.Primary)
        {
            Disabled = autoposts.Count >= AutopostService.MaxAutopostsPerGuild
        });
        container.WithActionRow(buttons);

        return response;
    }

    public static ResponseModel AddChannelPicker(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay("## Add autopost");
        container.WithSeparator();
        container.WithTextDisplay(
            "Pick the channel or thread this autopost should post in. Each channel can have one autopost.\n" +
            "After picking a channel you can configure the content type, schedule and filters.");

        var channelMenu = new ChannelMenuProperties(InteractionConstants.Autopost.AddChannel)
            .WithPlaceholder("Channel or thread to post in")
            .WithMinValues(0)
            .WithMaxValues(1)
            .WithChannelTypes([
                ChannelType.TextGuildChannel, ChannelType.AnnouncementGuildChannel,
                ChannelType.PublicGuildThread, ChannelType.PrivateGuildThread,
                ChannelType.AnnouncementGuildThread
            ]);

        container.AddComponent(channelMenu);

        var buttons = new ActionRowProperties();
        buttons.AddComponents(new ButtonProperties(InteractionConstants.Autopost.Overview, "Back",
            ButtonStyle.Secondary));
        container.WithActionRow(buttons);

        return response;
    }

    public async Task<ResponseModel> EditView(ContextModel context, int autopostId)
    {
        var autopost = await autopostService.GetAutopost(autopostId);

        if (autopost == null)
        {
            return await Overview(context);
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.ComponentsV2
        };

        var container = response.ComponentsContainer;
        container.WithAccentColor(DiscordConstants.InformationColorBlue);

        container.WithTextDisplay($"## Autopost in #{GetChannelName(context, autopost.ChannelId)}");
        container.WithSeparator();

        var supportsArtistFilter = autopost.ContentType is AutopostType.ServerAlbums or AutopostType.ServerTracks;
        var isChartType = autopost.ContentType != AutopostType.ServerRecap;
        var allTime = isChartType && autopost.TimePeriod == TimePeriod.AllTime;

        var nextPost = AutopostService.GetNextScheduledPost(autopost,
            DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));

        var description = new StringBuilder();
        description.AppendLine(autopost.Enabled
            ? $"✨ Posting {AutopostRendering.GetScheduleDisplay(autopost.Schedule).ToLower()} in <#{autopost.ChannelId}>, next post <t:{((DateTimeOffset)nextPost).ToUnixTimeSeconds()}:f>."
            : $"⏸️ Paused. Posts {AutopostRendering.GetScheduleDisplay(autopost.Schedule).ToLower()} in <#{autopost.ChannelId}> when resumed.");

        if (autopost.RoleIds is { Length: > 0 })
        {
            description.AppendLine(autopost.RoleIds.Length == 1
                ? $"Filtered to members with the {AutopostRendering.GetRoleMentions(autopost.RoleIds)} role."
                : $"Filtered to members with any of these roles: {AutopostRendering.GetRoleMentions(autopost.RoleIds)}.");
        }

        if (supportsArtistFilter && !string.IsNullOrWhiteSpace(autopost.ArtistFilter))
        {
            description.AppendLine($"Filtered to the artist **{StringExtensions.Sanitize(autopost.ArtistFilter)}**.");
        }

        if (autopost.LastPosted.HasValue)
        {
            description.AppendLine($"Last posted <t:{((DateTimeOffset)autopost.LastPosted.Value).ToUnixTimeSeconds()}:R>.");
        }

        container.WithTextDisplay(description.ToString());
        container.WithSeparator();

        var typeMenu = new StringMenuProperties($"{InteractionConstants.Autopost.SetType}:{autopost.Id}")
            .WithPlaceholder("Content type")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var type in Enum.GetValues<AutopostType>())
        {
            var option = type.GetAttribute<OptionAttribute>();
            typeMenu.AddOption(option.Name, Enum.GetName(type), description: option.Description,
                isDefault: autopost.ContentType == type);
        }

        container.AddComponent(typeMenu);

        var scheduleMenu = new StringMenuProperties($"{InteractionConstants.Autopost.SetSchedule}:{autopost.Id}")
            .WithPlaceholder("Schedule")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var schedule in Enum.GetValues<AutopostSchedule>())
        {
            scheduleMenu.AddOption(Enum.GetName(schedule), Enum.GetName(schedule),
                description: schedule == AutopostSchedule.Monthly
                    ? "Post on the 1st of every month"
                    : "Post every Monday",
                isDefault: autopost.Schedule == schedule);
        }

        container.AddComponent(scheduleMenu);

        if (isChartType)
        {
            var timePeriodMenu = new StringMenuProperties($"{InteractionConstants.Autopost.SetTimePeriod}:{autopost.Id}")
                .WithPlaceholder("Time range")
                .WithMinValues(1)
                .WithMaxValues(1);

            timePeriodMenu.AddOption("Current period", "period",
                description: "Chart covers the previous week or month, matching the schedule",
                isDefault: !allTime);
            timePeriodMenu.AddOption("All-time", "alltime",
                description: "Chart covers your server's all-time listening",
                isDefault: allTime);

            container.AddComponent(timePeriodMenu);
        }

        var roleMenu = new RoleMenuProperties($"{InteractionConstants.Autopost.SetRole}:{autopost.Id}")
            .WithPlaceholder("Filter to one or more roles (optional)")
            .WithMinValues(0)
            .WithMaxValues(25);

        container.AddComponent(roleMenu);

        var sizeMenu = new StringMenuProperties($"{InteractionConstants.Autopost.SetSize}:{autopost.Id}")
            .WithPlaceholder("Content size")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var size in Enum.GetValues<AutopostSize>())
        {
            sizeMenu.AddOption(Enum.GetName(size), Enum.GetName(size),
                description: size switch
                {
                    AutopostSize.Compact => "Top 5 per list",
                    AutopostSize.Detailed => "Top 20 per list",
                    _ => "Top 10 per list"
                },
                isDefault: autopost.ContentSize == size);
        }

        container.AddComponent(sizeMenu);
        container.WithSeparator();

        var buttons = new ActionRowProperties();
        buttons.AddComponents(new ButtonProperties($"{InteractionConstants.Autopost.PostNow}:{autopost.Id}",
            "Post now", ButtonStyle.Secondary));

        if (supportsArtistFilter)
        {
            buttons.AddComponents(new ButtonProperties($"{InteractionConstants.Autopost.SetArtist}:{autopost.Id}",
                "Artist filter", ButtonStyle.Secondary));
        }

        buttons.AddComponents(new ButtonProperties($"{InteractionConstants.Autopost.Toggle}:{autopost.Id}",
            autopost.Enabled ? "Pause" : "Resume", ButtonStyle.Secondary));
        buttons.AddComponents(new ButtonProperties($"{InteractionConstants.Autopost.Remove}:{autopost.Id}",
            "Remove", ButtonStyle.Danger));
        buttons.AddComponents(new ButtonProperties(InteractionConstants.Autopost.Overview,
            "Back", ButtonStyle.Secondary));

        container.WithActionRow(buttons);

        container.WithSeparator();
        container.WithTextDisplay(
            $"-# Last updated by {await UserService.GetNameAsync(context.DiscordGuild, context.DiscordUser)}");

        return response;
    }

    public async Task<ResponseModel> DeepDive(long runId, int sectionIndex = 0)
    {
        var run = await autopostService.GetRun(runId);

        if (run?.Snapshot == null || run.Snapshot.Sections.Count == 0)
        {
            var unavailable = new ResponseModel
            {
                ResponseType = ResponseType.Embed,
                CommandResponse = CommandResponse.NotFound
            };
            unavailable.Embed.WithDescription("The data behind this post is no longer available.");
            unavailable.Embed.WithColor(DiscordConstants.WarningColorOrange);
            return unavailable;
        }

        var previousRun = await autopostService.GetPreviousRun(run);
        var previousSnapshot = previousRun?.Snapshot;

        var sections = run.Snapshot.Sections;
        sectionIndex = Math.Clamp(sectionIndex, 0, sections.Count - 1);
        var section = sections[sectionIndex];

        var previousSection = section.EntityType != AutopostEntityType.NewRelease
            ? previousSnapshot?.Sections?.FirstOrDefault(f => f.EntityType == section.EntityType)
            : null;

        var pageDescriptions = new List<string>();
        foreach (var chunk in section.Entries.Chunk(15))
        {
            var page = new StringBuilder();

            foreach (var entry in chunk)
            {
                var line = AutopostRendering.FormatEntryLine(section.EntityType, entry);

                if (previousSection != null)
                {
                    var previousPosition = AutopostRendering.GetPreviousPosition(previousSection, entry);
                    page.AppendLine(StringService.GetBillboardLine(line, entry.Rank - 1, previousPosition).Text);
                }
                else
                {
                    page.AppendLine($"{entry.Rank}. {line}");
                }
            }

            pageDescriptions.Add(page.ToString());
        }

        StringMenuProperties sectionMenu = null;
        if (sections.Count > 1)
        {
            sectionMenu = new StringMenuProperties($"{InteractionConstants.Autopost.DeepDiveSection}:{run.Id}")
                .WithPlaceholder("Pick a list")
                .WithMinValues(1)
                .WithMaxValues(1);

            foreach (var (menuSection, index) in sections.Select((value, index) => (value, index)))
            {
                sectionMenu.AddOption(menuSection.Title, index.ToString(), isDefault: index == sectionIndex);
            }
        }

        var response = new ResponseModel
        {
            ResponseType = ResponseType.Paginator
        };

        var paginator = new ComponentPaginatorBuilder()
            .WithPageFactory(GeneratePage)
            .WithPageCount(Math.Max(1, pageDescriptions.Count))
            .WithActionOnTimeout(ActionOnStop.DisableInput);

        response.ComponentPaginator = paginator;
        return response;

        IPage GeneratePage(IComponentPaginator p)
        {
            var container = new ComponentContainerProperties();

            container.WithTextDisplay($"### {section.Title} (full list)");
            container.WithSeparator();

            var currentPage = pageDescriptions.ElementAtOrDefault(p.CurrentPageIndex);
            if (currentPage != null)
            {
                container.WithTextDisplay(currentPage.TrimEnd());
            }

            container.WithSeparator();
            container.WithTextDisplay(
                $"-# Posted <t:{((DateTimeOffset)run.PostedAt).ToUnixTimeSeconds()}:D> - Page {p.CurrentPageIndex + 1}/{Math.Max(1, pageDescriptions.Count)}");

            if (sectionMenu != null)
            {
                container.AddComponent(sectionMenu);
            }

            if (pageDescriptions.Count > 1)
            {
                container.WithActionRow(StringService.GetPaginationActionRow(p));
            }

            return new PageBuilder()
                .WithAllowedMentions(AllowedMentionsProperties.None)
                .WithMessageFlags(MessageFlags.IsComponentsV2 | MessageFlags.Ephemeral)
                .WithComponents([container])
                .Build();
        }
    }

    private static string GetAutopostSummary(GuildAutopost autopost)
    {
        var summary = new StringBuilder();
        summary.Append($"**<#{autopost.ChannelId}>** · {GetTypeName(autopost.ContentType)} · " +
                       $"{AutopostRendering.GetScheduleDisplay(autopost.Schedule)}");

        if (autopost.ContentType != AutopostType.ServerRecap && autopost.TimePeriod == TimePeriod.AllTime)
        {
            summary.Append(" · All-time");
        }

        if (autopost.RoleIds is { Length: > 0 })
        {
            summary.Append($" · {AutopostRendering.GetRoleMentions(autopost.RoleIds)}");
        }

        if (!string.IsNullOrWhiteSpace(autopost.ArtistFilter))
        {
            summary.Append($" · Artist `{StringExtensions.Sanitize(autopost.ArtistFilter)}`");
        }

        if (!autopost.Enabled)
        {
            summary.Append(" · ⏸️ Paused");
        }

        return summary.ToString();
    }

    private static string GetTypeName(AutopostType type)
    {
        return type.GetAttribute<OptionAttribute>().Name;
    }

    private static string GetChannelName(ContextModel context, ulong channelId)
    {
        if (context.DiscordGuild != null)
        {
            if (context.DiscordGuild.Channels.TryGetValue(channelId, out var channel))
            {
                return channel.Name;
            }

            if (context.DiscordGuild.ActiveThreads.TryGetValue(channelId, out var thread))
            {
                return thread.Name;
            }
        }

        return channelId.ToString();
    }
}
