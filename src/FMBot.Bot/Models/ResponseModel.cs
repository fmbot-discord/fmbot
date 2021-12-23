using Discord;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class ResponseModel
{
    public ResponseModel()
    {
        this.CommandResponse = CommandResponse.Ok;
    }

    public ResponseType ResponseType { get; set; }

    public Embed Embed { get; set; }

    public string Text { get; set; }

    public CommandResponse CommandResponse { get; set; }
}

