
namespace FMBot.Bot.Models;


public enum RecapPage
{
    [Option("Overview")]
    Overview = 1,

    [Option("Top artists")]
    TopArtists = 2,

    [Option("Top albums")]
    TopAlbums = 3,

    [Option("Top tracks")]
    TopTracks = 4,

    [Option("Top genres")]
    TopGenres = 5,

    [Option("Top countries")]
    TopCountries = 6,

    [Option("Artist chart")]
    ArtistChart = 7,

    [Option("Album chart")]
    AlbumChart = 8,

    [Option("Discoveries ⭐")]
    Discoveries = 10,

    [Option("Listening time ⭐")]
    ListeningTime = 11,

    [Option("Bot stats - Overview")]
    BotStats = 20,

    [Option("Bot stats - Commands")]
    BotStatsCommands = 21,

    [Option("Bot stats - Shown artists")]
    BotStatsArtists = 22,

    [Option("Bot stats - Games")]
    Games = 23,
}
