namespace FMBot.LastFM.Domain.Models;

public class AuthSessionResponse
{
    public Session Session { get; set; }
}

public class Session
{
    public int Subscriber { get; set; }

    public string Name { get; set; }

    public string Key { get; set; }
}
