using System;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Domain.Attributes;
using FMBot.Domain.Models;

namespace FMBot.Bot.SlashCommands;

public class AdminSlashCommands : InteractionModuleBase
{
    private readonly AdminService _adminService;
    private readonly CensorService _censorService;

    public AdminSlashCommands(AdminService adminService, CensorService censorService)
    {
        this._adminService = adminService;
        this._censorService = censorService;
    }


    [ComponentInteraction(InteractionConstants.CensorTypes)]
    public async Task SetCensoredArtist(string censoredId, string[] inputs)
    {
        var embed = new EmbedBuilder();

        var id = int.Parse(censoredId);

        var censoredMusic = await this._censorService.GetForId(id);

        if (!await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
        {
            return;
        }

        foreach (var option in Enum.GetNames(typeof(CensorType)))
        {
            if (Enum.TryParse(option, out CensorType flag))
            {
                if (inputs.Any(a => a == option))
                {
                    censoredMusic.CensorType |= flag;
                }
                else
                {
                    censoredMusic.CensorType &= ~flag;
                }
            }
        }

        censoredMusic = await this._censorService.SetCensorType(censoredMusic, censoredMusic.CensorType);

        var description = new StringBuilder();

        if (censoredMusic.Artist)
        {
            description.AppendLine($"Artist: `{censoredMusic.ArtistName}`");
        }
        else
        {
            description.AppendLine($"Album: `{censoredMusic.AlbumName}` by `{censoredMusic.ArtistName}`");
        }

        description.AppendLine();
        description.AppendLine("Censored music entry has been updated to:");

        foreach (var flag in censoredMusic.CensorType.GetUniqueFlags())
        {
            if (censoredMusic.CensorType.HasFlag(flag))
            {
                var name = flag.GetAttribute<OptionAttribute>().Name;
                description.AppendLine($"- **{name}**");
            }
        }

        embed.WithDescription(description.ToString());
        embed.WithColor(DiscordConstants.InformationColorBlue);
        await RespondAsync(embed: embed.Build(), ephemeral: true);
    }
}
