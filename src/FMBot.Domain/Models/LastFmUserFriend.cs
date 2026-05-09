using System;
using System.Collections.Generic;

namespace FMBot.Domain.Models;

public class LastFmUserFriendsList
{
    public long TotalAmount { get; set; }
    public long Page { get; set; }
    public long PerPage { get; set; }
    public long TotalPages { get; set; }

    public List<LastFmUserFriend> Friends { get; set; }
}

public class LastFmUserFriend
{
    public string UserName { get; set; }
    public string RealName { get; set; }

    public string Url { get; set; }
    public string Country { get; set; }
    public string ImageUrl { get; set; }

    public bool Subscriber { get; set; }
    public string Type { get; set; }

    public DateTime Registered { get; set; }
    public long RegisteredUnix { get; set; }
}
