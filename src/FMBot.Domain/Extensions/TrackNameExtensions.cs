namespace FMBot.Domain.Extensions;

public static class TrackNameExtensions
{
    public static string SanitizeTrackNameForComparison(string trackName)
    {
        trackName = trackName.ToLower();
        trackName = trackName.Replace("(", "");
        trackName = trackName.Replace(")", "");
        trackName = trackName.Replace("-", "");
        trackName = trackName.Replace("'", "");
        trackName = trackName.Replace(" ", "");
        trackName = trackName.Replace("[", "");
        trackName = trackName.Replace("]", "");

        return trackName;
    }
}
