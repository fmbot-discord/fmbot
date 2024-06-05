using System;
using System.Collections.Generic;

namespace FMBot.Bot.Models;

public class GameModel
{
    public int GameId { get; set; }
    public int StarterUserId { get; set; }
    
    public ulong? DiscordGuildId { get; set; }
    public ulong? DiscordChannelId { get; set; }
    public ulong? DiscordId { get; set; }
    public ulong? DiscordResponseId { get; set; }

    public GameType GameType { get; set; }

    public DateTime DateStarted { get; set; }
    public DateTime? DateEnded { get; set; }

    public List<GameAnswerModel> Answers { get; set; }
    public List<GameHintModel> Hints { get; set; }
    
    public int Reshuffles { get; set; }

    public string JumbledArtist { get; set; }
    
    public string CorrectAnswer { get; set; }
}

public class GameAnswerModel
{
    public int GameAnswerId { get; set; }
    public int GameId { get; set; }
    
    public ulong DiscordUserId { get; set; }

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
    Playcount = 1,
    Popularity = 2,
    Genre = 3,
    StartDate = 4,
    EndDate = 5,
    Disambiguation = 6,
    Type = 7,
    Country = 8
}

public class GameHintModel
{
    public GameHintModel(JumbleHintType type, string content)
    {
        this.Type = type;
        this.Content = content;
        this.HintShown = false;
    }

    public JumbleHintType Type { get; set; }
    public string Content { get; set; }
    public bool HintShown { get; set; }
    public int? Order { get; set; }
}
