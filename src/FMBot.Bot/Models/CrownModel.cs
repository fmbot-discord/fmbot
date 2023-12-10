using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models;

public class CrownModel
{
    public UserCrown Crown { get; set; }

    public string CrownResult { get; set; }
    public string CrownHtmlResult { get; set; }
    public bool Stolen { get; set; } = false;
}
