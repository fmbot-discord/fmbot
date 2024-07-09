using FMBot.Domain.Models;
using System.Collections.Generic;
using System;

namespace FMBot.Persistence.Domain.Models;

public class JumbleSession
{
    public int JumbleSessionId { get; set; }
    public int StarterUserId { get; set; }

    public ulong? DiscordGuildId { get; set; }
    public ulong? DiscordChannelId { get; set; }
    public ulong? DiscordId { get; set; }
    public ulong? DiscordResponseId { get; set; }

    public JumbleType JumbleType { get; set; }

    public DateTime DateStarted { get; set; }
    public DateTime? DateEnded { get; set; }

    public List<JumbleSessionAnswer> Answers { get; set; }
    public List<JumbleSessionHint> Hints { get; set; }

    public int Reshuffles { get; set; }

    public string JumbledArtist { get; set; }

    public string CorrectAnswer { get; set; }

    public string ArtistName { get; set; }

    public string AlbumName { get; set; }

    public float? BlurLevel { get; set; }
}
