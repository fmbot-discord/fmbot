namespace FMBot.Persistence.Domain.Models;

public class DiscogsFormatDescriptions
{
    public int Id { get; set; }
    public int ReleaseId { get; set; }

    public string Description { get; set; }

    public DiscogsRelease DiscogsRelease { get; set; }
}
