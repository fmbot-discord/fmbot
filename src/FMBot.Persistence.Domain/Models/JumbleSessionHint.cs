using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class JumbleSessionHint
{
    public JumbleSessionHint(JumbleHintType type, string content)
    {
        this.Type = type;
        this.Content = content;
        this.HintShown = false;
    }

    public int JumbleSessionHintId { get; set; }
    public int JumbleSessionId { get; set; }

    public JumbleHintType Type { get; set; }
    public string Content { get; set; }
    public bool HintShown { get; set; }
    public int? Order { get; set; }

    public JumbleSession JumbleSession { get; set; }
}
