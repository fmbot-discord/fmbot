using Discord.Interactions;

namespace FMBot.Bot.Models;


public enum RecapPage
{
    [ChoiceDisplay("Overview")]
    Overview = 1,

    [ChoiceDisplay("Top artists")]
    TopArtists = 2,

    [ChoiceDisplay("Top albums")]
    TopAlbums = 3,

    [ChoiceDisplay("Top tracks")]
    TopTracks = 4,

    [ChoiceDisplay("Top genres")]
    TopGenres = 5,

    [ChoiceDisplay("Top countries")]
    TopCountries = 6,

    [ChoiceDisplay("Artist chart")]
    ArtistChart = 7,

    [ChoiceDisplay("Album chart")]
    AlbumChart = 8,

    [ChoiceDisplay("Discoveries ⭐")]
    Discoveries = 10,

    [ChoiceDisplay("Listening time ⭐")]
    ListeningTime = 11,

    [ChoiceDisplay("Bot stats - Overview")]
    BotStats = 20,

    [ChoiceDisplay("Bot stats - Commands")]
    BotStatsCommands = 21,

    [ChoiceDisplay("Bot stats - Shown artists")]
    BotStatsArtists = 22,

    [ChoiceDisplay("Bot stats - Games")]
    Games = 23,
}
