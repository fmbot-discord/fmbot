using System;

namespace FMBot.Persistence.Domain.Models;

public class AiGeneration
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User User { get; set; }

    public int? TargetedUserId { get; set; }

    public string Prompt { get; set; }

    public string Output { get; set; }

    public string Model { get; set; }

    public int TotalTokens { get; set; }

    public DateTime DateGenerated { get; set; }
}
