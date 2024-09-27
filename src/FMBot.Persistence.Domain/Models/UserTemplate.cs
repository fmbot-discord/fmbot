namespace FMBot.Persistence.Domain.Models;

public class UserTemplate : Template
{
    public int UserId { get; set; }

    public bool GlobalDefault { get; set; }
}
