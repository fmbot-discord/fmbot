using CsvHelper.Configuration.Attributes;

namespace FMBot.Domain.Models;

public class EurovisionContestantModel
{
    [Name("year")]
    public int Year { get; set; }

    [Name("to_country_id")]
    public string ToCountryId { get; set; }

    [Name("to_country")]
    public string ToCountry { get; set; }

    [Name("performer")]
    public string Performer { get; set; }

    [Name("song")]
    public string Song { get; set; }

    [Name("place_contest")]
    public int? PlaceContest { get; set; }

    [Name("sf_num")]
    public float? SfNum { get; set; }

    [Name("running_final")]
    public float? RunningFinal { get; set; }

    [Name("running_sf")]
    public float? RunningSf { get; set; }

    [Name("place_final")]
    public float? PlaceFinal { get; set; }

    [Name("points_final")]
    public float? PointsFinal { get; set; }

    [Name("place_sf")]
    public string PlaceSf { get; set; }

    [Name("points_sf")]
    public int? PointsSf { get; set; }

    [Name("points_tele_final")]
    public float? PointsTeleFinal { get; set; }

    [Name("points_jury_final")]
    public float? PointsJuryFinal { get; set; }

    [Name("points_tele_sf")]
    public float? PointsTeleSf { get; set; }

    [Name("points_jury_sf")]
    public float? PointsJurySf { get; set; }

    [Name("composers")]
    public string Composers { get; set; }

    [Name("lyricists")]
    public string Lyricists { get; set; }

    [Name("lyrics")]
    public string Lyrics { get; set; }

    [Name("youtube_url")]
    public string YoutubeUrl { get; set; }
}
