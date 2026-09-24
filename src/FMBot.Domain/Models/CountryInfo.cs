using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class CountryInfo
{
    public string Name { get; set; }
    public string Code { get; set; }
    public string Emoji { get; set; }
    public string Image { get; set; }
    public List<string> Aliases { get; set; }
}
