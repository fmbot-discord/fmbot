using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class AiPrompt
{
    public int Id { get; set; }
    public PromptType Type { get; set; }
    public int Version { get; set; }
    public string Language { get; set; }
    public string Prompt { get; set; }
    public string FreeModel { get; set; }
    public string PremiumModel { get; set; }

    public string UltraModel { get; set; }
}
