using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class InactiveUserLog
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string UserNameLastFM { get; set; }

    public ResponseStatus ResponseStatus { get; set; }

    public DateTime Created { get; set; }

    public User User { get; set; }
}
