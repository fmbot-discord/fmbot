using System.Text.Json.Serialization;
using FMBot.LastFM.Domain.Enums;

namespace FMBot.LastFM.Domain.Types;

public class Response<T>
{
    [JsonIgnore]
    public bool Success { get; set; }

    public ResponseStatus? Error { get; set; }

    public string Message { get; set; }

    public T Content { get; set; }
}
