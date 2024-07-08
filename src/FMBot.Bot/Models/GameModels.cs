namespace FMBot.Bot.Models;

public class JumbleUserStats
{
    public int TotalGamesPlayed { get; set; }
    public int GamesStarted { get; set; }
    public int GamesAnswered { get; set; }
    public int TotalAnswers { get; set; }
    public int GamesWon { get; set; }
    public decimal Winrate { get; set; }
    public decimal AvgHintsShown { get; set; }
    public decimal AvgAnsweringTime { get; set; }
    public decimal AvgCorrectAnsweringTime { get; set; }
    public decimal AvgAttemptsUntilCorrect { get; set; }
}
