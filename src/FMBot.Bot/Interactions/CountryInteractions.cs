using System;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Builders;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace FMBot.Bot.Interactions;

public class CountryInteractions(
    UserService userService,
    SettingService settingService,
    CountryBuilders countryBuilders)
    : ComponentInteractionModule<ComponentInteractionContext>
{
    [ComponentInteraction(InteractionConstants.CountryChartTheme)]
    public async Task CountryChartThemeAsync(params string[] inputs)
    {
        try
        {
            var stringMenuInteraction = (StringMenuInteraction)this.Context.Interaction;
            var splitInput = stringMenuInteraction.Data.SelectedValues[0].Split("-");

            if (splitInput.Length < 4 ||
                !Enum.TryParse(splitInput[0], out CountryChartTheme theme) ||
                !ulong.TryParse(splitInput[1], out var targetDiscordUserId))
            {
                return;
            }

            var timeDescription = string.Join("-", splitInput.Skip(3));

            await RespondAsync(InteractionCallback.DeferredModifyMessage);
            await this.Context.DisableButtonsAndMenus();

            var contextUser = await userService.GetUserWithDiscogs(targetDiscordUserId);
            var userSettings = await settingService.GetOriginalContextUser(targetDiscordUserId, this.Context.User.Id,
                this.Context.Guild, this.Context.User);

            var timeSettings = SettingService.GetTimePeriod(timeDescription,
                registeredLastFm: userSettings.RegisteredLastFm, timeZone: userSettings.TimeZone,
                defaultTimePeriod: TimePeriod.AllTime);

            var response = await countryBuilders.GetTopCountryChart(
                new ContextModel(this.Context, contextUser), userSettings, timeSettings, theme);

            await this.Context.UpdateInteractionEmbed(response, defer: false);

            if (response.Stream != null)
            {
                await response.Stream.DisposeAsync();
            }

            await this.Context.LogCommandUsedAsync(response, userService);
        }
        catch (Exception e)
        {
            await this.Context.HandleCommandException(e, userService);
        }
    }
}
