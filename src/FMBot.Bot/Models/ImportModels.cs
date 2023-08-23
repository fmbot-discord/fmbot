using System.Text.Json.Serialization;
using System;
using FMBot.Domain.Enums;

namespace FMBot.Bot.Models;

public class SpotifyEndSongImportModel
{
    [JsonPropertyName("ts")]
    public DateTime Ts { get; set; }

    //[JsonPropertyName("username")]
    //public string Username { get; set; }

    //[JsonPropertyName("platform")]
    //public string Platform { get; set; }

    [JsonPropertyName("ms_played")]
    public int MsPlayed { get; set; }

    //[JsonPropertyName("conn_country")]
    //public string ConnCountry { get; set; }

    //[JsonPropertyName("ip_addr_decrypted")]
    //public string IpAddressDecrypted { get; set; }

    //[JsonPropertyName("user_agent_decrypted")]
    //public string UserAgentDecrypted { get; set; }

    [JsonPropertyName("master_metadata_track_name")]
    public string MasterMetadataTrackName { get; set; }

    [JsonPropertyName("master_metadata_album_artist_name")]
    public string MasterMetadataAlbumArtistName { get; set; }

    [JsonPropertyName("master_metadata_album_album_name")]
    public string MasterMetadataAlbumAlbumName { get; set; }

    //[JsonPropertyName("spotify_track_uri")]
    //public string SpotifyTrackUri { get; set; }

    //[JsonPropertyName("episode_name")]
    //public object EpisodeName { get; set; }

    //[JsonPropertyName("episode_show_name")]
    //public object EpisodeShowName { get; set; }

    //[JsonPropertyName("spotify_episode_uri")]
    //public object SpotifyEpisodeUri { get; set; }

    //[JsonPropertyName("reason_start")]
    //public string ReasonStart { get; set; }

    //[JsonPropertyName("reason_end")]
    //public string ReasonEnd { get; set; }

    //[JsonPropertyName("shuffle")]
    //public bool Shuffle { get; set; }

    //[JsonPropertyName("skipped")]
    //public object Skipped { get; set; }

    //[JsonPropertyName("offline")]
    //public bool Offline { get; set; }

    //[JsonPropertyName("offline_timestamp")]
    //public object OfflineTimestamp { get; set; }

    //[JsonPropertyName("incognito_mode")]
    //public bool IncognitoMode { get; set; }
}

public enum ImportStatus
{
    Success,
    UnknownFailure,
    WrongPackageFailure
}
