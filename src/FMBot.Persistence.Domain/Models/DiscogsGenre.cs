namespace FMBot.Persistence.Domain.Models;

public class DiscogsGenre
{
    public int Id { get; set; }
    public int MasterId { get; set; }

    public string Description { get; set; }

    public DiscogsMaster DiscogsMaster { get; set; }
}
