using FMBot.Domain.Enums;
using System;

namespace FMBot.Domain.Models;

public class ImportUser
{
    public int UserId { get; set; }

    public ulong DiscordUserId { get; set; }

    public string UserNameLastFM { get; set; }

    public DataSource DataSource { get; set; }

    public DateTime? LastImportPlay { get; set; }
}
