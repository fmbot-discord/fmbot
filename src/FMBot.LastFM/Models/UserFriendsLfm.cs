using System.Collections.Generic;
using System.Text.Json.Serialization;
using FMBot.LastFM.Converters;

namespace FMBot.LastFM.Models;

internal class UserFriendsLfmResponse
{
    [JsonPropertyName("friends")]
    public UserFriendsLfm Friends { get; set; }
}

internal class UserFriendsLfm
{
    [JsonPropertyName("@attr")]
    public AttributesLfm AttributesLfm { get; set; }

    [JsonPropertyName("user")]
    [JsonConverter(typeof(UserFriendListConverter))]
    public List<UserLfm> User { get; set; }
}
