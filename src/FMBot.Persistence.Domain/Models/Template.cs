using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class Template
{
    public int Id { get; set; }

    public TemplateType Type { get; set; }

    public string Name { get; set; }

    public string Content { get; set; }

    public string ShareCode { get; set; }

    public string OriginalShareCode { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }
}
