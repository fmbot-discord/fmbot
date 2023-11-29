using System.Collections.Generic;
using System.Text.Json.Serialization;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Domain.Types;

public class Response<T>
{
    [JsonIgnore]
    public bool Success { get; set; }

    public ResponseStatus? Error { get; set; }

    public PlaySource? PlaySource { get; set; }

    public List<TopListObject> TopList { get; set; }

    public string Message { get; set; }

    public T Content { get; set; }
}
