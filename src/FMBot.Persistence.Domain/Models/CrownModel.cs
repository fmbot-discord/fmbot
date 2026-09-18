namespace FMBot.Persistence.Domain.Models;

public class CrownModel
{
    public UserCrown Crown { get; set; }

    public UserCrown PreviousCrown { get; set; }

    public string CrownResult { get; set; }
    public string CrownHtmlResult { get; set; }
    public bool Stolen { get; set; } = false;
    public bool Claimed { get; set; } = false;
}
