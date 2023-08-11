using System.Text.Json.Serialization;
using FMBot.Domain.Enums;

namespace FMBot.Domain.Types;

public class Response<T>
{
    [JsonIgnore]
    public bool Success { get; set; }

    public ResponseStatus? Error { get; set; }

    public PlaySource? PlaySource { get; set; }

    public string Message { get; set; }

    public T Content { get; set; }
}
