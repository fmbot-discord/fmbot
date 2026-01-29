using System;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class UserFmSetting
{
    public int UserId { get; set; }

    public FmEmbedType EmbedType { get; set; }

    public FmTextType? SmallTextType { get; set; }

    public FmAccentColor? AccentColor { get; set; }

    public string CustomColor { get; set; }

    public FmButton? Buttons { get; set; }

    public bool? PrivateButtonResponse { get; set; }

    public FmFooterOption FooterOptions { get; set; }

    public DateTime? Modified { get; set; }

    public User User { get; set; }
}
