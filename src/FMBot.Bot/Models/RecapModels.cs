
using FMBot.Domain.Attributes;

namespace FMBot.Bot.Models;


public enum RecapPage
{
    [Option("Overview", localizationKey: "recap.pages.overview")]
    Overview = 1,

    [Option("Top artists", localizationKey: "recap.pages.topArtists")]
    TopArtists = 2,

    [Option("Top albums", localizationKey: "recap.pages.topAlbums")]
    TopAlbums = 3,

    [Option("Top tracks", localizationKey: "recap.pages.topTracks")]
    TopTracks = 4,

    [Option("Top genres", localizationKey: "recap.pages.topGenres")]
    TopGenres = 5,

    [Option("Top countries", localizationKey: "recap.pages.topCountries")]
    TopCountries = 6,

    [Option("Artist chart", localizationKey: "recap.pages.artistChart")]
    ArtistChart = 7,

    [Option("Album chart", localizationKey: "recap.pages.albumChart")]
    AlbumChart = 8,

    [Option("Discoveries ⭐", localizationKey: "recap.pages.discoveries")]
    Discoveries = 10,

    [Option("Listening time ⭐", localizationKey: "recap.pages.listeningTime")]
    ListeningTime = 11,

    [Option("Bot stats - Overview", localizationKey: "recap.pages.botStats")]
    BotStats = 20,

    [Option("Bot stats - Commands", localizationKey: "recap.pages.botStatsCommands")]
    BotStatsCommands = 21,

    [Option("Bot stats - Shown artists", localizationKey: "recap.pages.botStatsArtists")]
    BotStatsArtists = 22,

    [Option("Bot stats - Games", localizationKey: "recap.pages.games")]
    Games = 23,
}
