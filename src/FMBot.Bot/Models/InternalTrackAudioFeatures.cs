namespace FMBot.Bot.Models;

public class InternalTrackAudioFeatures
{
    public InternalTrackAudioFeatures()
    {
    }

    public InternalTrackAudioFeatures(float danceability,
        float energy,
        float speechiness,
        float acousticness,
        float instrumentalness,
        float valence,
        decimal tempo)
    {
        this.Danceability = danceability;
        this.Energy = energy;
        this.Speechiness = speechiness;
        this.Acousticness = acousticness;
        this.Instrumentalness = instrumentalness;
        this.Valence = valence;
        this.Tempo = tempo;
    }

    public float Danceability { get; set; }
    public float Energy { get; set; }
    public float Speechiness { get; set; }
    public float Acousticness { get; set; }
    public float Instrumentalness { get; set; }
    public float Valence { get; set; }
    public decimal Tempo { get; set; }
}
