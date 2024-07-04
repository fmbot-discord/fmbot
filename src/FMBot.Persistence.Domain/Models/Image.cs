using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class Image
{
    public int Id { get; set; }
    
    public ImageSource ImageSource { get; set; }

    public string Url { get; set; }
    public DateTime LastUpdated { get; set; }
    
    public int? Width { get; set; }
    public int? Height { get; set; }

    public string BgColor { get; set; }
    public string TextColor1 { get; set; }
    public string TextColor2 { get; set; }
    public string TextColor3 { get; set; }
    public string TextColor4 { get; set; }
}
