using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fergun.Interactive;
using FMBot.Bot.Attributes;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class GraphInteractions(
    UserService userService,
    GraphBuilders graphBuilders,
    InteractiveService interactivity)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.Graph.ArtistSelectMenu)]
    [UsernameSetRequired]
    public async Task ArtistSelectAsync()
    {
        try
        {
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedValues = stringMenuInteraction.Data.SelectedValues;

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var selectedArtists = new List<string>();
            int? userId = null;
            long? startUnixTs = null;
            long? endUnixTs = null;

            foreach (var value in selectedValues)
            {
                var parts = value.Split(':', 4);
                if (parts.Length < 4)
                {
                    continue;
                }

                userId ??= int.Parse(parts[0]);
                startUnixTs ??= long.Parse(parts[1]);
                endUnixTs ??= long.Parse(parts[2]);

                selectedArtists.Add(parts[3]);
            }

            if (userId == null || startUnixTs == null || endUnixTs == null || selectedArtists.Count == 0)
            {
                return;
            }

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var start = DateTimeOffset.FromUnixTimeSeconds(startUnixTs.Value).UtcDateTime;
            var end = DateTimeOffset.FromUnixTimeSeconds(endUnixTs.Value).UtcDateTime;
            var playDays = (int)(end - start).TotalDays;

            var timeSettings = new TimeSettingsModel
            {
                StartDateTime = start,
                EndDateTime = end,
                PlayDays = playDays,
                Description = GetTimeDescription(start, end, playDays),
                UsePlays = true
            };

            var userSettings = new UserSettingsModel
            {
                UserId = userId.Value,
                UserNameLastFm = contextUser.UserNameLastFM,
                DisplayName = contextUser.UserNameLastFM,
                UserType = contextUser.UserType,
                DiscordUserId = contextUser.DiscordUserId
            };

            var response = await graphBuilders.ArtistGrowthAsync(
                new ContextModel(this.Context, contextUser), userSettings, timeSettings, selectedArtists);

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    [ComponentInteraction(InteractionConstants.Graph.TypeSelectMenu)]
    [UsernameSetRequired]
    public async Task GraphTypeSelectAsync()
    {
        try
        {
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var selectedValue = stringMenuInteraction.Data.SelectedValues.FirstOrDefault();

            if (string.IsNullOrEmpty(selectedValue))
            {
                return;
            }

            var parts = selectedValue.Split(':', 4);
            if (parts.Length < 4)
            {
                return;
            }

            var userId = int.Parse(parts[0]);
            var startUnixTs = long.Parse(parts[1]);
            var endUnixTs = long.Parse(parts[2]);
            var graphType = (GraphType)int.Parse(parts[3]);

            await RespondAsync(InteractionCallback.DeferredModifyMessage);

            var contextUser = await userService.GetUserSettingsAsync(this.Context.User);

            var start = DateTimeOffset.FromUnixTimeSeconds(startUnixTs).UtcDateTime;
            var end = DateTimeOffset.FromUnixTimeSeconds(endUnixTs).UtcDateTime;
            var playDays = (int)(end - start).TotalDays;

            var timeSettings = new TimeSettingsModel
            {
                StartDateTime = start,
                EndDateTime = end,
                PlayDays = playDays,
                Description = GetTimeDescription(start, end, playDays),
                UsePlays = true
            };

            var userSettings = new UserSettingsModel
            {
                UserId = userId,
                UserNameLastFm = contextUser.UserNameLastFM,
                DisplayName = contextUser.UserNameLastFM,
                UserType = contextUser.UserType,
                DiscordUserId = contextUser.DiscordUserId
            };

            ResponseModel response;

            switch (graphType)
            {
                case GraphType.ArtistGrowth:
                default:
                    response = await graphBuilders.ArtistGrowthAsync(
                        new ContextModel(this.Context, contextUser), userSettings, timeSettings);
                    break;
            }

            await this.Context.UpdateInteractionEmbed(response, interactivity, false);
            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }

    private static string GetTimeDescription(DateTime start, DateTime end, int playDays)
    {
        if (playDays <= 7)
        {
            return "Weekly";
        }
        if (playDays <= 31)
        {
            return "Monthly";
        }
        if (playDays <= 93)
        {
            return "Quarterly";
        }
        if (playDays <= 183)
        {
            return "Half year";
        }
        if (playDays <= 366)
        {
            return "Yearly";
        }

        return "All time";
    }
}
