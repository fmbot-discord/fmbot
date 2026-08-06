using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FMBot.Persistence.Repositories;
using Npgsql;

namespace FMBot.Tests;

[Category("SearchBenchmark")]
public partial class SearchBenchmarkTests
{
    private const string ConnectionStringVariable = "FMBOT_SEARCH_BENCHMARK_DB";

    private const double MinimumAlbumAccuracy = 0.88;
    private const double MinimumTrackAccuracy = 0.68;

    public record SearchCase(string Query, string ExpectedArtist, string ExpectedName)
    {
        public override string ToString() => this.Query;
    }

    private static readonly SearchCase[] AlbumCases =
    [
        new("thriller", "Michael Jackson", "Thriller"),
        new("rumours", "Fleetwood Mac", "Rumours"),
        new("igor", "Tyler, the Creator", "IGOR"),
        new("the wall", "Pink Floyd", "The Wall"),
        new("brat", "Charli XCX", "BRAT"),
        new("808s", "Kanye West", "808s & Heartbreak"),
        new("a love supreme", "John Coltrane", "A Love Supreme"),
        new("lemonade", "Beyoncé", "Lemonade"),
        new("reputation", "Taylor Swift", "Reputation"),
        new("homework", "Daft Punk", "Homework"),
        new("americana", "The Offspring", "Americana"),
        new("purple rain", "Prince", "Purple Rain"),
        new("back in black", "AC/DC", "Back in Black"),
        new("yeezus", "Kanye West", "Yeezus"),
        new("1989", "Taylor Swift", "1989"),
        new("folklore", "Taylor Swift", "folklore"),
        new("discovery", "Daft Punk", "Discovery"),
        new("blonde", "Frank Ocean", "Blonde"),
        new("channel orange", "Frank Ocean", "channel ORANGE"),
        new("graduation", "Kanye West", "Graduation"),
        new("in rainbows", "Radiohead", "In Rainbows"),
        new("ok computer", "Radiohead", "OK Computer"),
        new("kid a", "Radiohead", "Kid A"),
        new("random access memories", "Daft Punk", "Random Access Memories"),
        new("to pimp a butterfly", "Kendrick Lamar", "To Pimp a Butterfly"),
        new("cowboy carter", "Beyoncé", "COWBOY CARTER"),
        new("abbey road", "The Beatles", "Abbey Road"),
        new("nevermind", "Nirvana", "Nevermind"),

        new("spiderland", "Slint", "Spiderland"),
        new("loveless", "My Bloody Valentine", "Loveless"),
        new("in the aeroplane over the sea", "Neutral Milk Hotel", "In the Aeroplane Over the Sea"),
        new("the glow pt 2", "The Microphones", "The Glow, Pt. 2"),
        new("rings around the world", "Super Furry Animals", "Rings Around the World"),
        new("bee thousand", "Guided by Voices", "Bee Thousand"),
        new("emperor tomato ketchup", "Stereolab", "Emperor Tomato Ketchup"),
        new("the moon and antarctica", "Modest Mouse", "The Moon & Antarctica"),
        new("agaetis byrjun", "Sigur Rós", "Ágætis byrjun"),
        new("geogaddi", "Boards of Canada", "Geogaddi"),
        new("music has the right to children", "Boards of Canada", "Music Has the Right to Children"),
        new("selected ambient works 85 92", "Aphex Twin", "Selected Ambient Works 85-92"),
        new("untrue", "Burial", "Untrue"),
        new("endtroducing", "DJ Shadow", "Endtroducing....."),
        new("madvillainy", "Madvillain", "Madvillainy"),
        new("donuts", "J Dilla", "Donuts"),
        new("the money store", "Death Grips", "The Money Store"),
        new("lonerism", "Tame Impala", "Lonerism"),
        new("currents", "Tame Impala", "Currents"),
        new("carrie and lowell", "Sufjan Stevens", "Carrie & Lowell"),
        new("illinois", "Sufjan Stevens", "Illinois"),
        new("for emma forever ago", "Bon Iver", "For Emma, Forever Ago"),
        new("yankee hotel foxtrot", "Wilco", "Yankee Hotel Foxtrot"),
        new("turn on the bright lights", "Interpol", "Turn On the Bright Lights"),
        new("funeral", "Arcade Fire", "Funeral"),
        new("mezzanine", "Massive Attack", "Mezzanine"),
        new("dummy", "Portishead", "Dummy"),

        new("master of puppets", "Metallica", "Master of Puppets"),
        new("reign in blood", "Slayer", "Reign in Blood"),
        new("paranoid", "Black Sabbath", "Paranoid"),
        new("rust in peace", "Megadeth", "Rust in Peace"),
        new("blackwater park", "Opeth", "Blackwater Park"),
        new("jane doe", "Converge", "Jane Doe"),
        new("sunbather", "Deafheaven", "Sunbather"),
        new("de mysteriis dom sathanas", "Mayhem", "De Mysteriis Dom Sathanas"),

        new("kind of blue", "Miles Davis", "Kind of Blue"),
        new("bitches brew", "Miles Davis", "Bitches Brew"),
        new("the shape of jazz to come", "Ornette Coleman", "The Shape of Jazz to Come"),
        new("mingus ah um", "Charles Mingus", "Mingus Ah Um"),
        new("time out", "The Dave Brubeck Quartet", "Time Out"),
        new("the koln concert", "Keith Jarrett", "The Köln Concert"),

        new("el mal querer", "ROSALÍA", "El Mal Querer"),
        new("un verano sin ti", "Bad Bunny", "Un Verano Sin Ti"),
        new("kveikur", "Sigur Rós", "Kveikur"),

        new("the tortured poets department", "Taylor Swift", "THE TORTURED POETS DEPARTMENT"),
        new("guts", "Olivia Rodrigo", "GUTS"),
        new("sos", "SZA", "SOS"),
        new("renaissance", "Beyoncé", "RENAISSANCE"),
        new("mr morale and the big steppers", "Kendrick Lamar", "Mr. Morale & The Big Steppers"),
        new("gnx", "Kendrick Lamar", "GNX"),
        new("hit me hard and soft", "Billie Eilish", "HIT ME HARD AND SOFT"),

        new("nirvana nevermind", "Nirvana", "Nevermind"),
        new("michael jackson thriller", "Michael Jackson", "Thriller"),
        new("fleetwood mac rumours", "Fleetwood Mac", "Rumours"),
        new("pink floyd the wall", "Pink Floyd", "The Wall"),
        new("tyler the creator igor", "Tyler, the Creator", "IGOR"),
        new("radiohead in rainbows", "Radiohead", "In Rainbows"),
        new("daft punk discovery", "Daft Punk", "Discovery"),
        new("slint spiderland", "Slint", "Spiderland"),
        new("my bloody valentine loveless", "My Bloody Valentine", "Loveless"),
        new("miles davis kind of blue", "Miles Davis", "Kind of Blue"),
        new("burial untrue", "Burial", "Untrue"),
        new("opeth blackwater park", "Opeth", "Blackwater Park"),
        new("aphex twin selected ambient works", "Aphex Twin", "Selected Ambient Works 85-92"),
        new("boards of canada geogaddi", "Boards of Canada", "Geogaddi"),
        new("death grips the money store", "Death Grips", "The Money Store"),
        new("sufjan stevens illinois", "Sufjan Stevens", "Illinois"),
        new("portishead dummy", "Portishead", "Dummy"),
        new("converge jane doe", "Converge", "Jane Doe")
    ];

    private static readonly SearchCase[] TrackCases =
    [
        new("bohemian rhapsody", "Queen", "Bohemian Rhapsody"),
        new("smells like teen spirit", "Nirvana", "Smells Like Teen Spirit"),
        new("stairway to heaven", "Led Zeppelin", "Stairway to Heaven"),
        new("november rain", "Guns N' Roses", "November Rain"),
        new("lose yourself", "Eminem", "Lose Yourself"),
        new("blinding lights", "The Weeknd", "Blinding Lights"),
        new("creep", "Radiohead", "Creep"),
        new("one more time", "Daft Punk", "One More Time"),
        new("feel good inc", "Gorillaz", "Feel Good Inc."),
        new("dreams", "Fleetwood Mac", "Dreams"),
        new("hurt", "Johnny Cash", "Hurt"),
        new("alright", "Kendrick Lamar", "Alright"),
        new("paranoid android", "Radiohead", "Paranoid Android"),
        new("karma police", "Radiohead", "Karma Police"),
        new("idioteque", "Radiohead", "Idioteque"),

        new("pyramid song", "Radiohead", "Pyramid Song"),
        new("holland 1945", "Neutral Milk Hotel", "Holland, 1945"),
        new("such great heights", "The Postal Service", "Such Great Heights"),
        new("rebellion lies", "Arcade Fire", "Rebellion (Lies)"),
        new("the rat", "The Walkmen", "The Rat"),
        new("cannonball", "The Breeders", "Cannonball"),
        new("gigantic", "Pixies", "Gigantic"),
        new("where is my mind", "Pixies", "Where Is My Mind?"),
        new("windowlicker", "Aphex Twin", "Windowlicker"),
        new("avril 14th", "Aphex Twin", "Avril 14th"),
        new("roygbiv", "Boards of Canada", "Roygbiv"),
        new("archangel", "Burial", "Archangel"),
        new("midnight city", "M83", "Midnight City"),
        new("all my friends", "LCD Soundsystem", "All My Friends"),
        new("someone great", "LCD Soundsystem", "Someone Great"),
        new("two weeks", "Grizzly Bear", "Two Weeks"),
        new("my girls", "Animal Collective", "My Girls"),
        new("skinny love", "Bon Iver", "Skinny Love"),
        new("re stacks", "Bon Iver", "re: Stacks"),
        new("videotape", "Radiohead", "Videotape"),
        new("a case of you", "Joni Mitchell", "A Case of You"),
        new("so what", "Miles Davis", "So What"),
        new("take five", "The Dave Brubeck Quartet", "Take Five"),
        new("giant steps", "John Coltrane", "Giant Steps"),
        new("odessa", "Caribou", "Odessa"),
        new("shadow", "Chromatics", "Shadow"),
        new("nights", "Frank Ocean", "Nights"),
        new("self control", "Frank Ocean", "Self Control"),
        new("the less i know the better", "Tame Impala", "The Less I Know The Better"),
        new("sunbather", "Deafheaven", "Sunbather"),
        new("guiding light", "Mumford & Sons", "Guiding Light"),

        new("queen bohemian rhapsody", "Queen", "Bohemian Rhapsody"),
        new("led zeppelin stairway to heaven", "Led Zeppelin", "Stairway to Heaven"),
        new("eminem lose yourself", "Eminem", "Lose Yourself"),
        new("the weeknd blinding lights", "The Weeknd", "Blinding Lights"),
        new("daft punk one more time", "Daft Punk", "One More Time"),
        new("radiohead creep", "Radiohead", "Creep"),
        new("kanye runaway", "Kanye West", "Runaway"),
        new("johnny cash hurt", "Johnny Cash", "Hurt"),
        new("gorillaz feel good inc", "Gorillaz", "Feel Good Inc."),
        new("pixies where is my mind", "Pixies", "Where Is My Mind?"),
        new("aphex twin windowlicker", "Aphex Twin", "Windowlicker"),
        new("burial archangel", "Burial", "Archangel"),
        new("lcd soundsystem all my friends", "LCD Soundsystem", "All My Friends"),
        new("miles davis so what", "Miles Davis", "So What"),
        new("frank ocean nights", "Frank Ocean", "Nights"),
        new("boards of canada roygbiv", "Boards of Canada", "Roygbiv")
    ];

    [GeneratedRegex(@"[\(\[\{].*?[\)\]\}]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonAlphanumericRegex();

    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var stripped = BracketedRegex().Replace(value, " ")
            .Replace("&", " and ")
            .Replace("’", "'");

        var folded = new StringBuilder();
        foreach (var c in stripped.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c);
            }
        }

        return NonAlphanumericRegex().Replace(folded.ToString(), " ").Trim().ToLowerInvariant();
    }

    private static bool Matches(SearchCase searchCase, string? name, string? artistName) =>
        string.Equals(Normalise(artistName), Normalise(searchCase.ExpectedArtist), StringComparison.Ordinal) &&
        string.Equals(Normalise(name), Normalise(searchCase.ExpectedName), StringComparison.Ordinal);

    private static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionStringVariable);

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Ignore($"Set {ConnectionStringVariable} to a local fmbot database to run the search benchmark.");
        }

        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    [Test]
    [Explicit("Per-case drill-down. The accuracy tests are the regression gate; some cases are known " +
              "unwinnable while Spotify popularity stays deprecated.")]
    [TestCaseSource(nameof(AlbumCases))]
    public async Task AlbumSearchResolvesToExpectedAlbum(SearchCase searchCase)
    {
        await using var connection = await OpenAsync();

        var album = await AlbumRepository.SearchAlbum(searchCase.Query, connection);

        Assert.That(album, Is.Not.Null, $"'{searchCase.Query}' returned no album at all");
        Assert.That(Matches(searchCase, album.Name, album.ArtistName), Is.True,
            $"'{searchCase.Query}' resolved to '{album.Name}' by '{album.ArtistName}', " +
            $"expected '{searchCase.ExpectedName}' by '{searchCase.ExpectedArtist}'");
    }

    [Test]
    [Explicit("Per-case drill-down. The accuracy tests are the regression gate; some cases are known " +
              "unwinnable while Spotify popularity stays deprecated.")]
    [TestCaseSource(nameof(TrackCases))]
    public async Task TrackSearchResolvesToExpectedTrack(SearchCase searchCase)
    {
        await using var connection = await OpenAsync();

        var track = await TrackRepository.SearchTrack(searchCase.Query, connection);

        Assert.That(track, Is.Not.Null, $"'{searchCase.Query}' returned no track at all");
        Assert.That(Matches(searchCase, track.Name, track.ArtistName), Is.True,
            $"'{searchCase.Query}' resolved to '{track.Name}' by '{track.ArtistName}', " +
            $"expected '{searchCase.ExpectedName}' by '{searchCase.ExpectedArtist}'");
    }

    [Test]
    public async Task AlbumSearchMeetsAccuracyAndReportsLatency()
    {
        await using var connection = await OpenAsync();

        var accuracy = await RunBenchmarkAsync("albums", AlbumCases, async (query, c) =>
        {
            var album = await AlbumRepository.SearchAlbum(query, c);
            return (album?.Name, album?.ArtistName);
        }, connection);

        Assert.That(accuracy, Is.GreaterThanOrEqualTo(MinimumAlbumAccuracy));
    }

    [Test]
    public async Task TrackSearchMeetsAccuracyAndReportsLatency()
    {
        await using var connection = await OpenAsync();

        var accuracy = await RunBenchmarkAsync("tracks", TrackCases, async (query, c) =>
        {
            var track = await TrackRepository.SearchTrack(query, c);
            return (track?.Name, track?.ArtistName);
        }, connection);

        Assert.That(accuracy, Is.GreaterThanOrEqualTo(MinimumTrackAccuracy));
    }

    private static async Task<double> RunBenchmarkAsync(string label, SearchCase[] cases,
        Func<string, NpgsqlConnection, Task<(string? Name, string? ArtistName)>> search, NpgsqlConnection connection)
    {
        var hits = 0;
        var artistOnlyHits = 0;
        var elapsed = new List<long>(cases.Length);
        var misses = new List<string>();

        foreach (var searchCase in cases)
        {
            var stopwatch = Stopwatch.StartNew();
            var (name, artistName) = await search(searchCase.Query, connection);
            stopwatch.Stop();
            elapsed.Add(stopwatch.ElapsedMilliseconds);

            var artistMatches = string.Equals(Normalise(artistName), Normalise(searchCase.ExpectedArtist),
                StringComparison.Ordinal);
            if (artistMatches)
            {
                artistOnlyHits++;
            }

            if (Matches(searchCase, name, artistName))
            {
                hits++;
            }
            else
            {
                misses.Add($"  {searchCase.Query,-38} -> {name ?? "<none>"} by {artistName ?? "<none>"}" +
                           $"{(artistMatches ? "   [right artist, wrong title]" : string.Empty)}");
            }
        }

        elapsed.Sort();
        var accuracy = (double)hits / cases.Length;

        TestContext.Out.WriteLine($"[{label}] accuracy@1 {hits}/{cases.Length} ({accuracy:P1}) exact artist+title");
        TestContext.Out.WriteLine($"[{label}] artist-only  {artistOnlyHits}/{cases.Length} " +
                                  $"({(double)artistOnlyHits / cases.Length:P1})");
        TestContext.Out.WriteLine($"[{label}] latency median {elapsed[elapsed.Count / 2]} ms, " +
                                  $"p90 {elapsed[(int)(elapsed.Count * 0.9)]} ms, max {elapsed[^1]} ms");

        if (misses.Count > 0)
        {
            TestContext.Out.WriteLine($"[{label}] misses:");
            misses.ForEach(TestContext.Out.WriteLine);
        }

        return accuracy;
    }
}
