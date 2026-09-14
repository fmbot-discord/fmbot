using System.Collections.Generic;

namespace FMBot.Bot.Models;

public class ArtistHintContext
{
    public List<string> PopularAlbums { get; set; } = [];
    public List<ArtistHintTrack> PopularTracks { get; set; } = [];
}

public class ArtistHintTrack
{
    public string Name { get; set; }
    public string AlbumName { get; set; }
}

public enum JumbleStatsView
{
    User,
    Server
}

public class JumbleUserStats
{
    public int TotalGamesPlayed { get; set; }
    public int GamesStarted { get; set; }
    public int GamesAnswered { get; set; }
    public int TotalAnswers { get; set; }
    public int GamesWon { get; set; }
    public decimal WinRate { get; set; }
    public decimal AvgHintsShown { get; set; }
    public decimal AvgAnsweringTime { get; set; }
    public decimal AvgCorrectAnsweringTime { get; set; }
    public decimal AvgAttemptsUntilCorrect { get; set; }
}

public class JumbleGuildStats
{
    public int TotalGamesPlayed { get; set; }
    public int GamesSolved { get; set; }
    public int TotalAnswers { get; set; }
    public int TotalReshuffles { get; set; }
    
    public decimal AvgHintsShown { get; set; }
    public decimal AvgAnsweringTime { get; set; }
    public decimal AvgCorrectAnsweringTime { get; set; }
    public decimal AvgAttemptsUntilCorrect { get; set; }

    public List<JumbleGuildStatChannel> Channels { get; set; }
}

public class JumbleGuildStatChannel
{
    public ulong Id { get; set; }
    public int Count { get; set; }
}
