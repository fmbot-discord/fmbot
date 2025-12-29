using System.Threading.Tasks;
using Fergun.Interactive;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class ImportSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; }

    public ImportSlashCommands(InteractiveService interactivity)
    {
        this.Interactivity = interactivity;
    }
}
