using System.Threading.Tasks;
using Fergun.Interactive;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.SlashCommands;

public class TemplateSlashCommands : ApplicationCommandModule<ApplicationCommandContext>
{
    private InteractiveService Interactivity { get; }

    public TemplateSlashCommands(InteractiveService interactivity)
    {
        this.Interactivity = interactivity;
    }
}
