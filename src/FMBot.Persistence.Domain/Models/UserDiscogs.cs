using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models;

public class UserDiscogs
{
    public int UserId { get; set; }

    public int DiscogsId { get; set; }
    public string Username { get; set; }

    public string AccessToken { get; set; }
    public string AccessTokenSecret { get; set; }

    public string MinimumValue { get; set; }
    public string MedianValue { get; set; }
    public string MaximumValue { get; set; }

    public bool? HideValue { get; set; }

    public DateTime? LastUpdated { get; set; }
    public DateTime? ReleasesLastUpdated { get; set; }

    public User User { get; set; }
}
