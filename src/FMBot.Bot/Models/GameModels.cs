using System;
using System.Collections.Generic;

namespace FMBot.Bot.Models;

public class GameModel
{
    public int GameId { get; set; }
    public int StarterUserId { get; set; }
    public ulong? DiscordGuildId { get; set; }

    public int HintCount { get; set; }

    public GameType GameType { get; set; }

    public DateTime DateStarted { get; set; }
    public DateTime? DateEnded { get; set; }

    public List<GameAnswerModel> Answers { get; set; }

    public string CorrectAnswer { get; set; }
}

public class GameAnswerModel
{
    public int GameAnswerId { get; set; }
    public int GameId { get; set; }
    public int UserId { get; set; }

    public bool Correct { get; set; }

    public string Answer { get; set; }
}

public enum GameType
{
    JumbleFirstWins = 1,
    JumbleGroup = 2
}

public enum JumbleHintType
{
    Reshuffle = 1,
    Description = 2
}
