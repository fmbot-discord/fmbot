using System.Threading.Tasks;
using Fergun.Interactive;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class AdminSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; }

    public AdminSlashCommands(InteractiveService interactivity)
    {
        this.Interactivity = interactivity;
    }
}
