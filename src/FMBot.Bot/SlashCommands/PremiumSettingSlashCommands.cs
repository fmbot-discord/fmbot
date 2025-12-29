using System.Threading.Tasks;
using Fergun.Interactive;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class PremiumSettingSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; }

    public PremiumSettingSlashCommands(InteractiveService interactivity)
    {
        this.Interactivity = interactivity;
    }
}
