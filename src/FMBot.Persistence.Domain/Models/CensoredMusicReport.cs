using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class CensoredMusicReport
{
    public bool Artist { get; set; }

    public string ArtistName { get; set; }
    public string AlbumName { get; set; }

    public string ProvidedNote { get; set; }

    public ReportStatus ReportStatus { get; set; }

    public ulong ReportedByDiscordUserId { get; set; }
    public ulong ProcessedByDiscordUserId { get; set; }

    public DateTime? ReportedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
