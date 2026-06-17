using System.Diagnostics;
using Npgsql;

if (args.Length < 2)
{
    PrintUsage();
    return;
}

var connectionString = args[0];
var dumpPath = args[1];
var mode = (args.Length >= 3 ? args[2] : "albums").Trim().ToLowerInvariant();

if (mode != "albums" && mode != "tracks")
{
    Console.WriteLine($"Unknown mode '{mode}'. Use 'albums' (default) or 'tracks'.");
    Console.WriteLine();
    PrintUsage();
    return;
}

var connBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    CommandTimeout = 7200,
    Timeout = 300
};

await using var db = new NpgsqlConnection(connBuilder.ToString());
await db.OpenAsync();

if (mode == "tracks")
{
    await RunTrackBackfill(db, dumpPath);
}
else
{
    await RunAlbumBackfill(db, dumpPath);
}

static void PrintUsage()
{
    Console.WriteLine("MusicBrainz Backfill Tool");
    Console.WriteLine("=========================");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -- \"<connection-string>\" <mbdump-directory> [albums|tracks]");
    Console.WriteLine();
    Console.WriteLine("  albums  (default)  Backfill release_date, type, label, upc on albums.");
    Console.WriteLine("  tracks             Backfill mbid, isrc, duration_ms, disambiguation,");
    Console.WriteLine("                     language and stamp music_brainz_date on tracks.");
    Console.WriteLine();
    Console.WriteLine("Steps to get MusicBrainz dump data:");
    Console.WriteLine("  1. Go to https://data.metabrainz.org/pub/musicbrainz/data/fullexport/");
    Console.WriteLine("  2. Pick the latest date directory");
    Console.WriteLine("  3. Download mbdump.tar.zst (~4 GB) and mbdump-derived.tar.zst (for ISRCs/links)");
    Console.WriteLine("  4. Extract with 7-Zip or: tar --zstd -xf mbdump.tar.zst");
    Console.WriteLine("  5. Run this tool pointing to the extracted mbdump/ directory");
}

static async Task RunAlbumBackfill(NpgsqlConnection db, string dumpPath)
{
    var requiredFiles = new[]
    {
        "artist_credit", "artist_credit_name", "release_group", "release_group_primary_type",
        "release_group_secondary_type", "release_group_secondary_type_join",
        "release", "release_country", "release_label", "label",
        "url", "l_release_url", "l_release_group_url"
    };

    var missing = requiredFiles.Where(f => !File.Exists(Path.Combine(dumpPath, f))).ToList();
    if (missing.Count > 0)
    {
        Console.WriteLine("Missing required dump files:");
        foreach (var f in missing)
            Console.WriteLine($"  {Path.Combine(dumpPath, f)}");
        return;
    }

    Console.WriteLine("MusicBrainz Album Backfill Tool");
    Console.WriteLine("===============================");
    Console.WriteLine();

    var totalSw = Stopwatch.StartNew();

    // ── Step 1: Create schema ────────────────────────────────────────────
    Console.WriteLine("[1/6] Creating musicbrainz schema...");
    await Execute(db, """
        CREATE SCHEMA IF NOT EXISTS musicbrainz;

        DROP TABLE IF EXISTS musicbrainz.album_lookup;
        DROP TABLE IF EXISTS musicbrainz.spotify_lookup;
        DROP TABLE IF EXISTS musicbrainz.release_group_meta;
        DROP TABLE IF EXISTS musicbrainz.l_release_url;
        DROP TABLE IF EXISTS musicbrainz.l_release_group_url;
        DROP TABLE IF EXISTS musicbrainz.url;
        DROP TABLE IF EXISTS musicbrainz.release_country;
        DROP TABLE IF EXISTS musicbrainz.release_label;
        DROP TABLE IF EXISTS musicbrainz.release_group_secondary_type_join;
        DROP TABLE IF EXISTS musicbrainz.release;
        DROP TABLE IF EXISTS musicbrainz.release_group;
        DROP TABLE IF EXISTS musicbrainz.release_group_primary_type;
        DROP TABLE IF EXISTS musicbrainz.release_group_secondary_type;
        DROP TABLE IF EXISTS musicbrainz.label;
        DROP TABLE IF EXISTS musicbrainz.artist_credit_name;
        DROP TABLE IF EXISTS musicbrainz.artist_credit;

        -- Column order must match MusicBrainz dump TSV files exactly

        CREATE TABLE musicbrainz.artist_credit (
            id              INTEGER NOT NULL,
            name            TEXT NOT NULL,
            artist_count    SMALLINT NOT NULL,
            ref_count       INTEGER,
            created         TIMESTAMPTZ,
            edits_pending   INTEGER,
            gid             UUID
        );

        CREATE TABLE musicbrainz.artist_credit_name (
            artist_credit   INTEGER NOT NULL,
            position        SMALLINT NOT NULL,
            artist          INTEGER NOT NULL,
            name            TEXT NOT NULL,
            join_phrase     TEXT
        );

        CREATE TABLE musicbrainz.release_group (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            type            INTEGER,
            comment         TEXT,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.release_group_primary_type (
            id              INTEGER NOT NULL,
            name            TEXT NOT NULL,
            parent          INTEGER,
            child_order     INTEGER,
            description     TEXT,
            gid             UUID NOT NULL
        );

        CREATE TABLE musicbrainz.release_group_secondary_type (
            id              INTEGER NOT NULL,
            name            TEXT NOT NULL,
            parent          INTEGER,
            child_order     INTEGER,
            description     TEXT,
            gid             UUID NOT NULL
        );

        CREATE TABLE musicbrainz.release_group_secondary_type_join (
            release_group   INTEGER NOT NULL,
            secondary_type  INTEGER NOT NULL,
            created         TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.release (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            release_group   INTEGER NOT NULL,
            status          INTEGER,
            packaging       INTEGER,
            language        INTEGER,
            script          INTEGER,
            barcode         TEXT,
            comment         TEXT,
            edits_pending   INTEGER,
            quality         SMALLINT,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.release_country (
            release         INTEGER NOT NULL,
            country         INTEGER NOT NULL,
            date_year       SMALLINT,
            date_month      SMALLINT,
            date_day        SMALLINT
        );

        CREATE TABLE musicbrainz.release_label (
            id              INTEGER NOT NULL,
            release         INTEGER NOT NULL,
            label           INTEGER,
            catalog_number  TEXT,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.label (
            id                  INTEGER NOT NULL,
            gid                 UUID NOT NULL,
            name                TEXT NOT NULL,
            begin_date_year     SMALLINT,
            begin_date_month    SMALLINT,
            begin_date_day      SMALLINT,
            end_date_year       SMALLINT,
            end_date_month      SMALLINT,
            end_date_day        SMALLINT,
            label_code          INTEGER,
            type                INTEGER,
            area                INTEGER,
            comment             TEXT,
            edits_pending       INTEGER,
            last_updated        TIMESTAMPTZ,
            ended               BOOLEAN
        );

        CREATE TABLE musicbrainz.url (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            url             TEXT NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.l_release_url (
            id              INTEGER NOT NULL,
            link            INTEGER NOT NULL,
            entity0         INTEGER NOT NULL,
            entity1         INTEGER NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            link_order      INTEGER,
            entity0_credit  TEXT,
            entity1_credit  TEXT
        );

        CREATE TABLE musicbrainz.l_release_group_url (
            id              INTEGER NOT NULL,
            link            INTEGER NOT NULL,
            entity0         INTEGER NOT NULL,
            entity1         INTEGER NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            link_order      INTEGER,
            entity0_credit  TEXT,
            entity1_credit  TEXT
        );
        """);
    Console.WriteLine("  Done.");

    // ── Step 2: Import dump files ────────────────────────────────────────
    Console.WriteLine("[2/6] Importing dump files...");
    foreach (var file in requiredFiles)
    {
        var sw = Stopwatch.StartNew();
        var filePath = Path.Combine(dumpPath, file);

        // Filter url table at import to Spotify album URLs only (saves ~99% of disk + index time)
        Func<string, bool>? filter = file == "url"
            ? line => line.Contains("open.spotify.com") && line.Contains("/album/")
            : null;

        var count = ImportTsvFile(db, $"musicbrainz.{file}", filePath, filter);
        sw.Stop();
        Console.WriteLine($"  {file,-42} {count,12:N0} rows  ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    // ── Step 3: Create indexes ───────────────────────────────────────────
    Console.WriteLine("[3/6] Creating indexes...");
    var idxSw = Stopwatch.StartNew();
    await Execute(db, """
        CREATE INDEX idx_mb_ac_id ON musicbrainz.artist_credit (id);
        CREATE INDEX idx_mb_acn_ac ON musicbrainz.artist_credit_name (artist_credit, position);
        CREATE INDEX idx_mb_rg_id ON musicbrainz.release_group (id);
        CREATE INDEX idx_mb_rg_ac ON musicbrainz.release_group (artist_credit);
        CREATE INDEX idx_mb_rgpt_id ON musicbrainz.release_group_primary_type (id);
        CREATE INDEX idx_mb_rgst_id ON musicbrainz.release_group_secondary_type (id);
        CREATE INDEX idx_mb_rgstj_rg ON musicbrainz.release_group_secondary_type_join (release_group);
        CREATE INDEX idx_mb_r_id ON musicbrainz.release (id);
        CREATE INDEX idx_mb_r_rg ON musicbrainz.release (release_group);
        CREATE INDEX idx_mb_rc_r ON musicbrainz.release_country (release);
        CREATE INDEX idx_mb_rl_r ON musicbrainz.release_label (release);
        CREATE INDEX idx_mb_rl_l ON musicbrainz.release_label (label);
        CREATE INDEX idx_mb_l_id ON musicbrainz.label (id);
        CREATE INDEX idx_mb_url_id ON musicbrainz.url (id);
        CREATE INDEX idx_mb_lru_e0 ON musicbrainz.l_release_url (entity0);
        CREATE INDEX idx_mb_lru_e1 ON musicbrainz.l_release_url (entity1);
        CREATE INDEX idx_mb_lrgu_e1 ON musicbrainz.l_release_group_url (entity1);
        """);
    idxSw.Stop();
    Console.WriteLine($"  Done. ({idxSw.Elapsed.TotalSeconds:F1}s)");

    // ── Step 4: Build lookup tables ──────────────────────────────────────
    Console.WriteLine("[4/6] Building lookup tables...");
    var lookupSw = Stopwatch.StartNew();

    // 4a: per-release_group rollup (shared by name + spotify lookups)
    await Execute(db, """
        CREATE TABLE musicbrainz.release_group_meta AS
        WITH earliest_date AS (
            -- Earliest release date across all official releases in a group
            SELECT DISTINCT ON (r.release_group)
                r.release_group,
                rc.date_year,
                rc.date_month,
                rc.date_day
            FROM musicbrainz.release r
            JOIN musicbrainz.release_country rc ON r.id = rc.release
            WHERE rc.date_year IS NOT NULL
              AND (r.status IS NULL OR r.status = 1)
            ORDER BY r.release_group,
                     rc.date_year,
                     COALESCE(rc.date_month, 13),
                     COALESCE(rc.date_day, 32)
        ),
        first_barcode AS (
            SELECT DISTINCT ON (r.release_group) r.release_group, r.barcode
            FROM musicbrainz.release r
            WHERE r.barcode ~ '^[0-9]{12,13}$'
              AND (r.status IS NULL OR r.status = 1)
            ORDER BY r.release_group, r.id
        ),
        first_label AS (
            SELECT DISTINCT ON (r.release_group) r.release_group, l.name AS label_name
            FROM musicbrainz.release r
            JOIN musicbrainz.release_label rl ON r.id = rl.release
            JOIN musicbrainz.label l ON rl.label = l.id
            WHERE l.name IS NOT NULL
              AND l.name != '[no label]'
              AND l.name NOT LIKE 'Not On Label%'
              AND (r.status IS NULL OR r.status = 1)
            ORDER BY r.release_group, r.id, rl.id
        )
        SELECT
            rg.id  AS release_group_id,
            rg.gid AS mbid,
            rg.artist_credit,
            rg.name AS rg_name,
            CASE
                WHEN EXISTS (
                    SELECT 1
                    FROM musicbrainz.release_group_secondary_type_join stj
                    JOIN musicbrainz.release_group_secondary_type st ON stj.secondary_type = st.id
                    WHERE stj.release_group = rg.id
                      AND LOWER(st.name) = 'compilation'
                ) THEN 'compilation'
                WHEN rgpt.name = 'Album'  THEN 'album'
                WHEN rgpt.name = 'Single' THEN 'single'
                ELSE NULL
            END AS type,
            ed.date_year, ed.date_month, ed.date_day,
            fl.label_name,
            fb.barcode AS upc
        FROM musicbrainz.release_group rg
        LEFT JOIN musicbrainz.release_group_primary_type rgpt ON rg.type = rgpt.id
        LEFT JOIN earliest_date ed ON rg.id = ed.release_group
        LEFT JOIN first_barcode fb ON rg.id = fb.release_group
        LEFT JOIN first_label   fl ON rg.id = fl.release_group;

        CREATE INDEX idx_rgm_id ON musicbrainz.release_group_meta (release_group_id);
        CREATE INDEX idx_rgm_ac ON musicbrainz.release_group_meta (artist_credit);
        ANALYZE musicbrainz.release_group_meta;
        """);

    // 4b: name-based lookup — citext columns to align with albums.(artist_name, name) unique citext index
    await Execute(db, """
        CREATE TABLE musicbrainz.album_lookup (
            artist_name CITEXT NOT NULL,
            album_name  CITEXT NOT NULL,
            mbid        UUID,
            type        TEXT,
            date_year   SMALLINT,
            date_month  SMALLINT,
            date_day    SMALLINT,
            label_name  TEXT,
            upc         TEXT
        );

        INSERT INTO musicbrainz.album_lookup
        WITH all_artist_names AS (
            -- Primary artist name (position 0)
            SELECT
                rgm.release_group_id, rgm.mbid, rgm.type,
                rgm.date_year, rgm.date_month, rgm.date_day,
                rgm.label_name, rgm.upc,
                acn.name::citext    AS artist_name,
                rgm.rg_name::citext AS album_name
            FROM musicbrainz.release_group_meta rgm
            JOIN musicbrainz.artist_credit_name acn
              ON acn.artist_credit = rgm.artist_credit AND acn.position = 0

            UNION ALL

            -- Full credit name (e.g. "Artist A feat. Artist B") if it differs from primary
            SELECT
                rgm.release_group_id, rgm.mbid, rgm.type,
                rgm.date_year, rgm.date_month, rgm.date_day,
                rgm.label_name, rgm.upc,
                ac.name::citext     AS artist_name,
                rgm.rg_name::citext AS album_name
            FROM musicbrainz.release_group_meta rgm
            JOIN musicbrainz.artist_credit ac ON ac.id = rgm.artist_credit
            JOIN musicbrainz.artist_credit_name acn
              ON acn.artist_credit = rgm.artist_credit AND acn.position = 0
            WHERE ac.name::citext != acn.name::citext
        )
        SELECT DISTINCT ON (artist_name, album_name)
            artist_name, album_name,
            mbid, type, date_year, date_month, date_day, label_name, upc
        FROM all_artist_names
        ORDER BY artist_name, album_name,
                 date_year NULLS LAST,
                 upc       NULLS LAST;

        CREATE INDEX idx_mb_lookup ON musicbrainz.album_lookup (artist_name, album_name);
        ANALYZE musicbrainz.album_lookup;
        """);

    // 4c: spotify-id-based lookup (high-confidence exact ID match)
    await Execute(db, """
        CREATE TABLE musicbrainz.spotify_lookup AS
        WITH spotify_urls AS (
            SELECT
                u.id AS url_id,
                substring(u.url FROM 'open\.spotify\.com/(?:intl-[a-z]+/)?album/([a-zA-Z0-9]{22})') AS spotify_id
            FROM musicbrainz.url u
            WHERE u.url ~ 'open\.spotify\.com/(?:intl-[a-z]+/)?album/[a-zA-Z0-9]{22}'
        ),
        links AS (
            -- Spotify URL linked to a specific release → take the release's release_group
            SELECT s.spotify_id, r.release_group AS rg_id
            FROM spotify_urls s
            JOIN musicbrainz.l_release_url lru ON lru.entity1 = s.url_id
            JOIN musicbrainz.release r ON r.id = lru.entity0

            UNION

            -- Spotify URL linked directly to a release_group
            SELECT s.spotify_id, lrgu.entity0 AS rg_id
            FROM spotify_urls s
            JOIN musicbrainz.l_release_group_url lrgu ON lrgu.entity1 = s.url_id
        )
        SELECT DISTINCT ON (l.spotify_id)
            l.spotify_id,
            rgm.mbid, rgm.type,
            rgm.date_year, rgm.date_month, rgm.date_day,
            rgm.label_name, rgm.upc
        FROM links l
        JOIN musicbrainz.release_group_meta rgm ON l.rg_id = rgm.release_group_id
        WHERE l.spotify_id IS NOT NULL
        ORDER BY l.spotify_id,
                 rgm.date_year NULLS LAST,
                 rgm.date_month NULLS LAST,
                 rgm.date_day NULLS LAST;

        CREATE INDEX idx_mb_spotify_lookup ON musicbrainz.spotify_lookup (spotify_id);
        ANALYZE musicbrainz.spotify_lookup;
        """);

    var nameLookupCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.album_lookup");
    var spotifyLookupCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.spotify_lookup");
    lookupSw.Stop();
    Console.WriteLine($"  album_lookup:   {nameLookupCount,10:N0} entries");
    Console.WriteLine($"  spotify_lookup: {spotifyLookupCount,10:N0} entries");
    Console.WriteLine($"  ({lookupSw.Elapsed.TotalSeconds:F1}s total)");

    // ── Step 5: Pre-backfill stats ───────────────────────────────────────
    Console.WriteLine("[5/6] Analyzing what can be backfilled...");
    Console.WriteLine();

    var totalAlbums = await Scalar<long>(db, "SELECT COUNT(*) FROM albums");
    var matchedSpotify = await Scalar<long>(db, """
        SELECT COUNT(*) FROM albums a
        JOIN musicbrainz.spotify_lookup sl ON a.spotify_id = sl.spotify_id
        WHERE a.spotify_id IS NOT NULL
        """);
    var matchedName = await Scalar<long>(db, """
        SELECT COUNT(*) FROM albums a
        JOIN musicbrainz.album_lookup ml
          ON a.artist_name = ml.artist_name
         AND a.name        = ml.album_name
        """);

    Console.WriteLine($"  Total albums:                  {totalAlbums,12:N0}");
    Console.WriteLine($"  Matched via spotify_id (exact):{matchedSpotify,12:N0}");
    Console.WriteLine($"  Matched via name (exact):      {matchedName,12:N0}");
    Console.WriteLine();

    // Per-field "can fill" counts from a single combined join (faster than 4× joins)
    Console.WriteLine($"  {"Field",-16} {"Missing",-14} {"Spotify match",-16} {"Name match",-14}");
    Console.WriteLine($"  {"-----",-16} {"-------",-14} {"-------------",-16} {"----------",-14}");

    var fieldStats = await QueryRows(db, """
        WITH joined AS (
            SELECT a.id, a.release_date, a.type, a.label, a.upc,
                   sl.date_year   AS sl_year,   sl.type AS sl_type,
                   sl.label_name  AS sl_label,  sl.upc  AS sl_upc,
                   ml.date_year   AS ml_year,   ml.type AS ml_type,
                   ml.label_name  AS ml_label,  ml.upc  AS ml_upc
            FROM albums a
            LEFT JOIN musicbrainz.spotify_lookup sl ON a.spotify_id = sl.spotify_id
            LEFT JOIN musicbrainz.album_lookup ml
              ON a.artist_name = ml.artist_name
             AND a.name        = ml.album_name
        )
        SELECT
            'release_date',
            (SELECT COUNT(*) FROM albums WHERE release_date IS NULL),
            COUNT(*) FILTER (WHERE release_date IS NULL AND sl_year IS NOT NULL),
            COUNT(*) FILTER (WHERE release_date IS NULL AND sl_year IS NULL AND ml_year IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'type',
            (SELECT COUNT(*) FROM albums WHERE type IS NULL),
            COUNT(*) FILTER (WHERE type IS NULL AND sl_type IS NOT NULL),
            COUNT(*) FILTER (WHERE type IS NULL AND sl_type IS NULL AND ml_type IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'label',
            (SELECT COUNT(*) FROM albums WHERE label IS NULL),
            COUNT(*) FILTER (WHERE label IS NULL AND sl_label IS NOT NULL),
            COUNT(*) FILTER (WHERE label IS NULL AND sl_label IS NULL AND ml_label IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'upc',
            (SELECT COUNT(*) FROM albums WHERE upc IS NULL),
            COUNT(*) FILTER (WHERE upc IS NULL AND sl_upc IS NOT NULL AND sl_upc != ''),
            COUNT(*) FILTER (WHERE upc IS NULL AND (sl_upc IS NULL OR sl_upc = '') AND ml_upc IS NOT NULL AND ml_upc != '')
        FROM joined
        """);

    foreach (var r in fieldStats)
        Console.WriteLine($"  {r[0],-16} {long.Parse(r[1] ?? "0"),12:N0}  {long.Parse(r[2] ?? "0"),12:N0}    {long.Parse(r[3] ?? "0"),12:N0}");

    // Show samples of what would change
    Console.WriteLine();
    Console.WriteLine("  Samples of what would be set (via spotify_id, then name):");
    var samples = await QueryRows(db, """
        SELECT a.artist_name, a.name,
               CASE WHEN a.spotify_id IS NOT NULL AND sl.spotify_id IS NOT NULL THEN 'spot' ELSE 'name' END AS via,
               CONCAT(COALESCE(sl.date_year, ml.date_year)::text,
                   CASE WHEN COALESCE(sl.date_month, ml.date_month) IS NOT NULL
                        THEN '-' || LPAD(COALESCE(sl.date_month, ml.date_month)::text, 2, '0') ELSE '' END,
                   CASE WHEN COALESCE(sl.date_day, ml.date_day) IS NOT NULL
                        THEN '-' || LPAD(COALESCE(sl.date_day, ml.date_day)::text, 2, '0') ELSE '' END),
               COALESCE(sl.type, ml.type),
               COALESCE(sl.label_name, ml.label_name),
               COALESCE(NULLIF(sl.upc,''), NULLIF(ml.upc,''))
        FROM albums a
        LEFT JOIN musicbrainz.spotify_lookup sl ON a.spotify_id = sl.spotify_id
        LEFT JOIN musicbrainz.album_lookup ml
          ON a.artist_name = ml.artist_name
         AND a.name        = ml.album_name
        WHERE (sl.spotify_id IS NOT NULL OR ml.mbid IS NOT NULL)
          AND (a.release_date IS NULL OR a.type IS NULL OR a.label IS NULL OR a.upc IS NULL)
        ORDER BY random() LIMIT 15
        """);
    foreach (var r in samples)
        Console.WriteLine($"    {Trunc(r[0], 22),-22} {Trunc(r[1], 26),-26}  via={r[2],-4} date={r[3] ?? "-",-12} type={r[4] ?? "-",-12} label={Trunc(r[5], 18),-18} upc={r[6] ?? "-"}");

    Console.WriteLine();
    Console.Write("Proceed with backfill? (y/n): ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
    {
        Console.WriteLine("Aborted.");
        return;
    }

    // ── Step 6: Run backfill ─────────────────────────────────────────────
    Console.WriteLine("[6/6] Running backfill updates...");
    Console.WriteLine("  Pass A: spotify_id match (high-confidence)");

    var spotifyDate = await Execute(db, """
        UPDATE albums a
        SET release_date = CONCAT(
                sl.date_year::text,
                CASE WHEN sl.date_month IS NOT NULL
                     THEN '-' || LPAD(sl.date_month::text, 2, '0') ELSE '' END,
                CASE WHEN sl.date_day IS NOT NULL
                     THEN '-' || LPAD(sl.date_day::text, 2, '0') ELSE '' END
            ),
            release_date_precision = COALESCE(a.release_date_precision, CASE
                WHEN sl.date_day   IS NOT NULL THEN 'day'
                WHEN sl.date_month IS NOT NULL THEN 'month'
                ELSE 'year'
            END)
        FROM musicbrainz.spotify_lookup sl
        WHERE a.spotify_id = sl.spotify_id
          AND a.release_date IS NULL
          AND sl.date_year IS NOT NULL
        """);
    Console.WriteLine($"    release_date:  {spotifyDate,10:N0} updated");

    var spotifyType = await Execute(db, """
        UPDATE albums a SET type = sl.type
        FROM musicbrainz.spotify_lookup sl
        WHERE a.spotify_id = sl.spotify_id
          AND a.type IS NULL AND sl.type IS NOT NULL
        """);
    Console.WriteLine($"    type:          {spotifyType,10:N0} updated");

    var spotifyLabel = await Execute(db, """
        UPDATE albums a SET label = sl.label_name
        FROM musicbrainz.spotify_lookup sl
        WHERE a.spotify_id = sl.spotify_id
          AND a.label IS NULL AND sl.label_name IS NOT NULL
        """);
    Console.WriteLine($"    label:         {spotifyLabel,10:N0} updated");

    var spotifyUpc = await Execute(db, """
        UPDATE albums a SET upc = sl.upc
        FROM musicbrainz.spotify_lookup sl
        WHERE a.spotify_id = sl.spotify_id
          AND a.upc IS NULL AND sl.upc IS NOT NULL AND sl.upc != ''
        """);
    Console.WriteLine($"    upc:           {spotifyUpc,10:N0} updated");

    Console.WriteLine("  Pass B: name match (artist + album, citext) — fills remaining nulls");

    var nameDate = await Execute(db, """
        UPDATE albums a
        SET release_date = CONCAT(
                ml.date_year::text,
                CASE WHEN ml.date_month IS NOT NULL
                     THEN '-' || LPAD(ml.date_month::text, 2, '0') ELSE '' END,
                CASE WHEN ml.date_day IS NOT NULL
                     THEN '-' || LPAD(ml.date_day::text, 2, '0') ELSE '' END
            ),
            release_date_precision = COALESCE(a.release_date_precision, CASE
                WHEN ml.date_day   IS NOT NULL THEN 'day'
                WHEN ml.date_month IS NOT NULL THEN 'month'
                ELSE 'year'
            END)
        FROM musicbrainz.album_lookup ml
        WHERE a.artist_name = ml.artist_name
          AND a.name        = ml.album_name
          AND a.release_date IS NULL
          AND ml.date_year IS NOT NULL
        """);
    Console.WriteLine($"    release_date:  {nameDate,10:N0} updated");

    var nameType = await Execute(db, """
        UPDATE albums a SET type = ml.type
        FROM musicbrainz.album_lookup ml
        WHERE a.artist_name = ml.artist_name
          AND a.name        = ml.album_name
          AND a.type IS NULL AND ml.type IS NOT NULL
        """);
    Console.WriteLine($"    type:          {nameType,10:N0} updated");

    var nameLabel = await Execute(db, """
        UPDATE albums a SET label = ml.label_name
        FROM musicbrainz.album_lookup ml
        WHERE a.artist_name = ml.artist_name
          AND a.name        = ml.album_name
          AND a.label IS NULL AND ml.label_name IS NOT NULL
        """);
    Console.WriteLine($"    label:         {nameLabel,10:N0} updated");

    var nameUpc = await Execute(db, """
        UPDATE albums a SET upc = ml.upc
        FROM musicbrainz.album_lookup ml
        WHERE a.artist_name = ml.artist_name
          AND a.name        = ml.album_name
          AND a.upc IS NULL AND ml.upc IS NOT NULL AND ml.upc != ''
        """);
    Console.WriteLine($"    upc:           {nameUpc,10:N0} updated");

    totalSw.Stop();
    Console.WriteLine();
    Console.WriteLine($"Backfill complete in {totalSw.Elapsed:hh\\:mm\\:ss}");
    Console.WriteLine();
    Console.Write("Drop musicbrainz schema? (y/n): ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
    {
        await Execute(db, "DROP SCHEMA musicbrainz CASCADE");
        Console.WriteLine("  Schema dropped.");
    }
}

static async Task RunTrackBackfill(NpgsqlConnection db, string dumpPath)
{
    var requiredFiles = new[]
    {
        "artist_credit", "artist_credit_name", "release_group", "release",
        "medium", "track", "recording", "isrc",
        "url", "l_recording_url", "l_recording_work", "work_language", "language"
    };

    var missing = requiredFiles.Where(f => !File.Exists(Path.Combine(dumpPath, f))).ToList();
    if (missing.Count > 0)
    {
        Console.WriteLine("Missing required dump files:");
        foreach (var f in missing)
            Console.WriteLine($"  {Path.Combine(dumpPath, f)}");
        Console.WriteLine();
        Console.WriteLine("Note: isrc, l_recording_url, l_recording_work and work_language live in");
        Console.WriteLine("mbdump-derived.tar.zst, not the main mbdump.tar.zst.");
        return;
    }

    Console.WriteLine("MusicBrainz Track Backfill Tool");
    Console.WriteLine("===============================");
    Console.WriteLine();

    var totalSw = Stopwatch.StartNew();

    Console.WriteLine("[1/6] Creating musicbrainz schema...");
    await Execute(db, """
        CREATE SCHEMA IF NOT EXISTS musicbrainz;

        DROP TABLE IF EXISTS musicbrainz.track_name_lookup;
        DROP TABLE IF EXISTS musicbrainz.track_spotify_lookup;
        DROP TABLE IF EXISTS musicbrainz.recording_isrc;
        DROP TABLE IF EXISTS musicbrainz.recording_language;
        DROP TABLE IF EXISTS musicbrainz.l_recording_work;
        DROP TABLE IF EXISTS musicbrainz.l_recording_url;
        DROP TABLE IF EXISTS musicbrainz.url;
        DROP TABLE IF EXISTS musicbrainz.isrc;
        DROP TABLE IF EXISTS musicbrainz.track;
        DROP TABLE IF EXISTS musicbrainz.recording;
        DROP TABLE IF EXISTS musicbrainz.medium;
        DROP TABLE IF EXISTS musicbrainz.release;
        DROP TABLE IF EXISTS musicbrainz.release_group;
        DROP TABLE IF EXISTS musicbrainz.work_language;
        DROP TABLE IF EXISTS musicbrainz.language;
        DROP TABLE IF EXISTS musicbrainz.artist_credit_name;
        DROP TABLE IF EXISTS musicbrainz.artist_credit;

        CREATE TABLE musicbrainz.artist_credit (
            id              INTEGER NOT NULL,
            name            TEXT NOT NULL,
            artist_count    SMALLINT NOT NULL,
            ref_count       INTEGER,
            created         TIMESTAMPTZ,
            edits_pending   INTEGER,
            gid             UUID
        );

        CREATE TABLE musicbrainz.artist_credit_name (
            artist_credit   INTEGER NOT NULL,
            position        SMALLINT NOT NULL,
            artist          INTEGER NOT NULL,
            name            TEXT NOT NULL,
            join_phrase     TEXT
        );

        CREATE TABLE musicbrainz.release_group (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            type            INTEGER,
            comment         TEXT,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.release (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            release_group   INTEGER NOT NULL,
            status          INTEGER,
            packaging       INTEGER,
            language        INTEGER,
            script          INTEGER,
            barcode         TEXT,
            comment         TEXT,
            edits_pending   INTEGER,
            quality         SMALLINT,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.medium (
            id              INTEGER NOT NULL,
            release         INTEGER NOT NULL,
            position        INTEGER NOT NULL,
            format          INTEGER,
            name            TEXT,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            track_count     INTEGER
        );

        CREATE TABLE musicbrainz.track (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            recording       INTEGER NOT NULL,
            medium          INTEGER NOT NULL,
            position        INTEGER NOT NULL,
            number          TEXT,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            length          INTEGER,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            is_data_track   BOOLEAN
        );

        CREATE TABLE musicbrainz.recording (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            name            TEXT NOT NULL,
            artist_credit   INTEGER NOT NULL,
            length          INTEGER,
            comment         TEXT,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            video           BOOLEAN
        );

        CREATE TABLE musicbrainz.isrc (
            id              INTEGER NOT NULL,
            recording       INTEGER NOT NULL,
            isrc            TEXT NOT NULL,
            source          SMALLINT,
            edits_pending   INTEGER,
            created         TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.url (
            id              INTEGER NOT NULL,
            gid             UUID NOT NULL,
            url             TEXT NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.l_recording_url (
            id              INTEGER NOT NULL,
            link            INTEGER NOT NULL,
            entity0         INTEGER NOT NULL,
            entity1         INTEGER NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            link_order      INTEGER,
            entity0_credit  TEXT,
            entity1_credit  TEXT
        );

        CREATE TABLE musicbrainz.l_recording_work (
            id              INTEGER NOT NULL,
            link            INTEGER NOT NULL,
            entity0         INTEGER NOT NULL,
            entity1         INTEGER NOT NULL,
            edits_pending   INTEGER,
            last_updated    TIMESTAMPTZ,
            link_order      INTEGER,
            entity0_credit  TEXT,
            entity1_credit  TEXT
        );

        CREATE TABLE musicbrainz.work_language (
            work            INTEGER NOT NULL,
            language        INTEGER NOT NULL,
            edits_pending   INTEGER,
            created         TIMESTAMPTZ
        );

        CREATE TABLE musicbrainz.language (
            id              INTEGER NOT NULL,
            iso_code_2t     TEXT,
            iso_code_2b     TEXT,
            iso_code_1      TEXT,
            name            TEXT NOT NULL,
            frequency       INTEGER,
            iso_code_3      TEXT
        );
        """);
    Console.WriteLine("  Done.");

    Console.WriteLine("[2/6] Importing dump files...");
    foreach (var file in requiredFiles)
    {
        var sw = Stopwatch.StartNew();
        var filePath = Path.Combine(dumpPath, file);

        Func<string, bool>? filter = file == "url"
            ? line => line.Contains("open.spotify.com") && line.Contains("/track/")
            : null;

        await ReconcileColumns(db, $"musicbrainz.{file}", filePath);

        var count = ImportTsvFile(db, $"musicbrainz.{file}", filePath, filter);
        sw.Stop();
        Console.WriteLine($"  {file,-42} {count,12:N0} rows  ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    var langTotal = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.language");
    var langValid = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.language WHERE iso_code_3 ~ '^[A-Za-z]{2,3}$'");
    var languageOk = langTotal > 0 && langValid * 2 >= langTotal;
    if (!languageOk)
    {
        Console.WriteLine($"  WARNING: language table layout looks wrong ({langValid:N0}/{langTotal:N0} rows have a valid");
        Console.WriteLine("           iso_code_3). The language field will be SKIPPED to avoid bad data.");
    }

    Console.WriteLine("[3/6] Creating indexes...");
    var idxSw = Stopwatch.StartNew();
    await Execute(db, """
        CREATE INDEX idx_mb_ac_id ON musicbrainz.artist_credit (id);
        CREATE INDEX idx_mb_acn_ac ON musicbrainz.artist_credit_name (artist_credit, position);
        CREATE INDEX idx_mb_rg_id ON musicbrainz.release_group (id);
        CREATE INDEX idx_mb_r_id ON musicbrainz.release (id);
        CREATE INDEX idx_mb_r_rg ON musicbrainz.release (release_group);
        CREATE INDEX idx_mb_m_id ON musicbrainz.medium (id);
        CREATE INDEX idx_mb_m_r ON musicbrainz.medium (release);
        CREATE INDEX idx_mb_t_m ON musicbrainz.track (medium);
        CREATE INDEX idx_mb_t_rec ON musicbrainz.track (recording);
        CREATE INDEX idx_mb_rec_id ON musicbrainz.recording (id);
        CREATE INDEX idx_mb_isrc_rec ON musicbrainz.isrc (recording);
        CREATE INDEX idx_mb_url_id ON musicbrainz.url (id);
        CREATE INDEX idx_mb_lru_e1 ON musicbrainz.l_recording_url (entity1);
        CREATE INDEX idx_mb_lrw_e0 ON musicbrainz.l_recording_work (entity0);
        CREATE INDEX idx_mb_wl_work ON musicbrainz.work_language (work);
        CREATE INDEX idx_mb_lang_id ON musicbrainz.language (id);
        """);
    idxSw.Stop();
    Console.WriteLine($"  Done. ({idxSw.Elapsed.TotalSeconds:F1}s)");

    Console.WriteLine("[4/6] Building lookup tables...");
    var lookupSw = Stopwatch.StartNew();

    await Execute(db, """
        CREATE TABLE musicbrainz.recording_isrc AS
        SELECT DISTINCT ON (recording) recording, isrc
        FROM musicbrainz.isrc
        WHERE isrc IS NOT NULL AND isrc <> ''
        ORDER BY recording, id;

        CREATE INDEX idx_mb_recisrc ON musicbrainz.recording_isrc (recording);
        ANALYZE musicbrainz.recording_isrc;
        """);

    await Execute(db, "CREATE TABLE musicbrainz.recording_language (recording INTEGER, language TEXT);");
    if (languageOk)
    {
        await Execute(db, """
            INSERT INTO musicbrainz.recording_language
            SELECT DISTINCT ON (lrw.entity0)
                lrw.entity0 AS recording,
                l.iso_code_3 AS language
            FROM musicbrainz.l_recording_work lrw
            JOIN musicbrainz.work_language wl ON wl.work = lrw.entity1
            JOIN musicbrainz.language l ON l.id = wl.language
            WHERE l.iso_code_3 IS NOT NULL AND l.iso_code_3 <> ''
            ORDER BY lrw.entity0, l.frequency DESC NULLS LAST, l.id;
            """);
    }
    await Execute(db, """
        CREATE INDEX idx_mb_reclang ON musicbrainz.recording_language (recording);
        ANALYZE musicbrainz.recording_language;
        """);

    await Execute(db, """
        CREATE TABLE musicbrainz.track_spotify_lookup AS
        WITH spotify_urls AS (
            SELECT
                u.id AS url_id,
                substring(u.url FROM 'open\.spotify\.com/(?:intl-[a-z]+/)?track/([a-zA-Z0-9]{22})') AS spotify_id
            FROM musicbrainz.url u
            WHERE u.url ~ 'open\.spotify\.com/(?:intl-[a-z]+/)?track/[a-zA-Z0-9]{22}'
        ),
        rec_links AS (
            SELECT s.spotify_id, lru.entity0 AS recording_id
            FROM spotify_urls s
            JOIN musicbrainz.l_recording_url lru ON lru.entity1 = s.url_id
            WHERE s.spotify_id IS NOT NULL
        )
        SELECT DISTINCT ON (rl.spotify_id)
            rl.spotify_id,
            rec.gid                 AS mbid,
            rec.length              AS duration_ms,
            NULLIF(rec.comment, '') AS disambiguation,
            ri.isrc,
            rlang.language
        FROM rec_links rl
        JOIN musicbrainz.recording rec ON rec.id = rl.recording_id
        LEFT JOIN musicbrainz.recording_isrc ri ON ri.recording = rec.id
        LEFT JOIN musicbrainz.recording_language rlang ON rlang.recording = rec.id
        ORDER BY rl.spotify_id, rec.length DESC NULLS LAST, rec.id;

        CREATE INDEX idx_mb_tsl ON musicbrainz.track_spotify_lookup (spotify_id);
        ANALYZE musicbrainz.track_spotify_lookup;
        """);

    await Execute(db, """
        CREATE TABLE musicbrainz.track_name_lookup (
            artist_name    CITEXT NOT NULL,
            album_name     CITEXT NOT NULL,
            track_name     CITEXT NOT NULL,
            mbid           UUID,
            duration_ms    INTEGER,
            disambiguation TEXT,
            isrc           TEXT,
            language       TEXT
        );

        INSERT INTO musicbrainz.track_name_lookup
        WITH track_recordings AS (
            SELECT
                acn.name::citext AS artist_name,
                rg.name::citext  AS album_name,
                t.name::citext   AS track_name,
                t.recording      AS recording_id,
                rec.gid          AS mbid,
                rec.length       AS duration_ms,
                NULLIF(rec.comment, '') AS disambiguation
            FROM musicbrainz.release_group rg
            JOIN musicbrainz.release r ON r.release_group = rg.id AND (r.status IS NULL OR r.status = 1)
            JOIN musicbrainz.medium m ON m.release = r.id
            JOIN musicbrainz.track t ON t.medium = m.id
            JOIN musicbrainz.recording rec ON rec.id = t.recording
            JOIN musicbrainz.artist_credit_name acn
              ON acn.artist_credit = t.artist_credit AND acn.position = 0

            UNION ALL

            SELECT
                ac.name::citext, rg.name::citext, t.name::citext,
                t.recording, rec.gid, rec.length, NULLIF(rec.comment, '')
            FROM musicbrainz.release_group rg
            JOIN musicbrainz.release r ON r.release_group = rg.id AND (r.status IS NULL OR r.status = 1)
            JOIN musicbrainz.medium m ON m.release = r.id
            JOIN musicbrainz.track t ON t.medium = m.id
            JOIN musicbrainz.recording rec ON rec.id = t.recording
            JOIN musicbrainz.artist_credit ac ON ac.id = t.artist_credit
            JOIN musicbrainz.artist_credit_name acn
              ON acn.artist_credit = t.artist_credit AND acn.position = 0
            WHERE ac.name::citext <> acn.name::citext
        )
        SELECT DISTINCT ON (tr.artist_name, tr.album_name, tr.track_name)
            tr.artist_name, tr.album_name, tr.track_name,
            tr.mbid, tr.duration_ms, tr.disambiguation,
            ri.isrc, rlang.language
        FROM track_recordings tr
        LEFT JOIN musicbrainz.recording_isrc ri ON ri.recording = tr.recording_id
        LEFT JOIN musicbrainz.recording_language rlang ON rlang.recording = tr.recording_id
        ORDER BY tr.artist_name, tr.album_name, tr.track_name,
                 tr.duration_ms DESC NULLS LAST, tr.mbid;

        CREATE INDEX idx_mb_tnl ON musicbrainz.track_name_lookup (artist_name, album_name, track_name);
        ANALYZE musicbrainz.track_name_lookup;
        """);

    var recIsrcCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.recording_isrc");
    var recLangCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.recording_language");
    var spotifyLookupCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.track_spotify_lookup");
    var nameLookupCount = await Scalar<long>(db, "SELECT COUNT(*) FROM musicbrainz.track_name_lookup");
    lookupSw.Stop();
    Console.WriteLine($"  recording_isrc:       {recIsrcCount,12:N0} entries");
    Console.WriteLine($"  recording_language:   {recLangCount,12:N0} entries");
    Console.WriteLine($"  track_spotify_lookup: {spotifyLookupCount,12:N0} entries");
    Console.WriteLine($"  track_name_lookup:    {nameLookupCount,12:N0} entries");
    Console.WriteLine($"  ({lookupSw.Elapsed.TotalSeconds:F1}s total)");

    Console.WriteLine("[5/6] Analyzing what can be backfilled...");
    Console.WriteLine();

    var totalTracks = await Scalar<long>(db, "SELECT COUNT(*) FROM tracks");
    var matchedSpotify = await Scalar<long>(db, """
        SELECT COUNT(*) FROM tracks t
        JOIN musicbrainz.track_spotify_lookup sl ON t.spotify_id = sl.spotify_id
        WHERE t.spotify_id IS NOT NULL
        """);
    var matchedName = await Scalar<long>(db, """
        SELECT COUNT(*) FROM tracks t
        JOIN musicbrainz.track_name_lookup nl
          ON t.artist_name = nl.artist_name
         AND t.album_name  = nl.album_name
         AND t.name        = nl.track_name
        """);

    Console.WriteLine($"  Total tracks:                  {totalTracks,12:N0}");
    Console.WriteLine($"  Matched via spotify_id (exact):{matchedSpotify,12:N0}");
    Console.WriteLine($"  Matched via name (exact):      {matchedName,12:N0}");
    Console.WriteLine();

    Console.WriteLine($"  {"Field",-16} {"Missing",-14} {"Spotify match",-16} {"Name match",-14}");
    Console.WriteLine($"  {"-----",-16} {"-------",-14} {"-------------",-16} {"----------",-14}");

    var fieldStats = await QueryRows(db, """
        WITH joined AS MATERIALIZED (
            SELECT t.mbid, t.isrc, t.duration_ms, t.disambiguation, t.language,
                   sl.mbid AS sl_mbid, sl.isrc AS sl_isrc, sl.duration_ms AS sl_dur,
                   sl.disambiguation AS sl_dis, sl.language AS sl_lang,
                   nl.mbid AS nl_mbid, nl.isrc AS nl_isrc, nl.duration_ms AS nl_dur,
                   nl.disambiguation AS nl_dis, nl.language AS nl_lang
            FROM tracks t
            LEFT JOIN musicbrainz.track_spotify_lookup sl ON t.spotify_id = sl.spotify_id
            LEFT JOIN musicbrainz.track_name_lookup nl
              ON t.artist_name = nl.artist_name
             AND t.album_name  = nl.album_name
             AND t.name        = nl.track_name
        )
        SELECT
            'mbid',
            (SELECT COUNT(*) FROM tracks WHERE mbid IS NULL),
            COUNT(*) FILTER (WHERE mbid IS NULL AND sl_mbid IS NOT NULL),
            COUNT(*) FILTER (WHERE mbid IS NULL AND sl_mbid IS NULL AND nl_mbid IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'isrc',
            (SELECT COUNT(*) FROM tracks WHERE isrc IS NULL OR isrc = ''),
            COUNT(*) FILTER (WHERE (isrc IS NULL OR isrc = '') AND sl_isrc IS NOT NULL),
            COUNT(*) FILTER (WHERE (isrc IS NULL OR isrc = '') AND sl_isrc IS NULL AND nl_isrc IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'duration_ms',
            (SELECT COUNT(*) FROM tracks WHERE duration_ms IS NULL),
            COUNT(*) FILTER (WHERE duration_ms IS NULL AND sl_dur IS NOT NULL AND sl_dur > 0),
            COUNT(*) FILTER (WHERE duration_ms IS NULL AND (sl_dur IS NULL OR sl_dur <= 0) AND nl_dur IS NOT NULL AND nl_dur > 0)
        FROM joined
        UNION ALL
        SELECT
            'disambiguation',
            (SELECT COUNT(*) FROM tracks WHERE disambiguation IS NULL),
            COUNT(*) FILTER (WHERE disambiguation IS NULL AND sl_dis IS NOT NULL),
            COUNT(*) FILTER (WHERE disambiguation IS NULL AND sl_dis IS NULL AND nl_dis IS NOT NULL)
        FROM joined
        UNION ALL
        SELECT
            'language',
            (SELECT COUNT(*) FROM tracks WHERE language IS NULL),
            COUNT(*) FILTER (WHERE language IS NULL AND sl_lang IS NOT NULL),
            COUNT(*) FILTER (WHERE language IS NULL AND sl_lang IS NULL AND nl_lang IS NOT NULL)
        FROM joined
        """);

    foreach (var r in fieldStats)
        Console.WriteLine($"  {r[0],-16} {long.Parse(r[1] ?? "0"),12:N0}  {long.Parse(r[2] ?? "0"),12:N0}    {long.Parse(r[3] ?? "0"),12:N0}");

    Console.WriteLine();
    Console.WriteLine("  Samples of what would be set (via spotify_id, then name):");
    var samples = await QueryRows(db, """
        SELECT t.artist_name, t.name,
               CASE WHEN t.spotify_id IS NOT NULL AND sl.spotify_id IS NOT NULL THEN 'spot' ELSE 'name' END AS via,
               COALESCE(sl.mbid, nl.mbid)::text,
               COALESCE(sl.duration_ms, nl.duration_ms)::text,
               COALESCE(NULLIF(sl.isrc, ''), NULLIF(nl.isrc, '')),
               COALESCE(sl.disambiguation, nl.disambiguation),
               COALESCE(sl.language, nl.language)
        FROM tracks t
        LEFT JOIN musicbrainz.track_spotify_lookup sl ON t.spotify_id = sl.spotify_id
        LEFT JOIN musicbrainz.track_name_lookup nl
          ON t.artist_name = nl.artist_name
         AND t.album_name  = nl.album_name
         AND t.name        = nl.track_name
        WHERE (sl.spotify_id IS NOT NULL OR nl.mbid IS NOT NULL)
          AND (t.mbid IS NULL OR t.isrc IS NULL OR t.duration_ms IS NULL
               OR t.disambiguation IS NULL OR t.language IS NULL)
        ORDER BY random() LIMIT 15
        """);
    foreach (var r in samples)
        Console.WriteLine($"    {Trunc(r[0], 20),-20} {Trunc(r[1], 24),-24}  via={r[2],-4} mbid={Trunc(r[3], 13),-13} dur={r[4] ?? "-",-8} isrc={r[5] ?? "-",-13} lang={r[7] ?? "-",-4} disamb={Trunc(r[6], 18)}");

    Console.WriteLine();
    Console.Write("Proceed with backfill? (y/n): ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) != true)
    {
        Console.WriteLine("Aborted.");
        return;
    }

    Console.WriteLine("[6/6] Running backfill updates...");
    Console.WriteLine("  Pass A: spotify_id match (high-confidence)");

    var spotifyMbid = await Execute(db, """
        UPDATE tracks t
        SET mbid = sl.mbid, music_brainz_date = now()
        FROM musicbrainz.track_spotify_lookup sl
        WHERE t.spotify_id = sl.spotify_id
          AND t.mbid IS NULL AND sl.mbid IS NOT NULL
        """);
    Console.WriteLine($"    mbid:            {spotifyMbid,10:N0} updated");

    var spotifyIsrc = await Execute(db, """
        UPDATE tracks t SET isrc = sl.isrc
        FROM musicbrainz.track_spotify_lookup sl
        WHERE t.spotify_id = sl.spotify_id
          AND (t.isrc IS NULL OR t.isrc = '') AND sl.isrc IS NOT NULL
        """);
    Console.WriteLine($"    isrc:            {spotifyIsrc,10:N0} updated");

    var spotifyDuration = await Execute(db, """
        UPDATE tracks t SET duration_ms = sl.duration_ms
        FROM musicbrainz.track_spotify_lookup sl
        WHERE t.spotify_id = sl.spotify_id
          AND t.duration_ms IS NULL AND sl.duration_ms IS NOT NULL AND sl.duration_ms > 0
        """);
    Console.WriteLine($"    duration_ms:     {spotifyDuration,10:N0} updated");

    var spotifyDisamb = await Execute(db, """
        UPDATE tracks t SET disambiguation = sl.disambiguation
        FROM musicbrainz.track_spotify_lookup sl
        WHERE t.spotify_id = sl.spotify_id
          AND t.disambiguation IS NULL AND sl.disambiguation IS NOT NULL
        """);
    Console.WriteLine($"    disambiguation:  {spotifyDisamb,10:N0} updated");

    if (languageOk)
    {
        var spotifyLang = await Execute(db, """
            UPDATE tracks t SET language = sl.language
            FROM musicbrainz.track_spotify_lookup sl
            WHERE t.spotify_id = sl.spotify_id
              AND t.language IS NULL AND sl.language IS NOT NULL
            """);
        Console.WriteLine($"    language:        {spotifyLang,10:N0} updated");
    }
    else
    {
        Console.WriteLine("    language:        skipped (layout check failed)");
    }

    Console.WriteLine("  Pass B: name match (artist + album + track, citext) — fills remaining nulls");

    var nameMbid = await Execute(db, """
        UPDATE tracks t
        SET mbid = nl.mbid, music_brainz_date = now()
        FROM musicbrainz.track_name_lookup nl
        WHERE t.artist_name = nl.artist_name
          AND t.album_name  = nl.album_name
          AND t.name        = nl.track_name
          AND t.mbid IS NULL AND nl.mbid IS NOT NULL
        """);
    Console.WriteLine($"    mbid:            {nameMbid,10:N0} updated");

    var nameIsrc = await Execute(db, """
        UPDATE tracks t SET isrc = nl.isrc
        FROM musicbrainz.track_name_lookup nl
        WHERE t.artist_name = nl.artist_name
          AND t.album_name  = nl.album_name
          AND t.name        = nl.track_name
          AND (t.isrc IS NULL OR t.isrc = '') AND nl.isrc IS NOT NULL
        """);
    Console.WriteLine($"    isrc:            {nameIsrc,10:N0} updated");

    var nameDuration = await Execute(db, """
        UPDATE tracks t SET duration_ms = nl.duration_ms
        FROM musicbrainz.track_name_lookup nl
        WHERE t.artist_name = nl.artist_name
          AND t.album_name  = nl.album_name
          AND t.name        = nl.track_name
          AND t.duration_ms IS NULL AND nl.duration_ms IS NOT NULL AND nl.duration_ms > 0
        """);
    Console.WriteLine($"    duration_ms:     {nameDuration,10:N0} updated");

    var nameDisamb = await Execute(db, """
        UPDATE tracks t SET disambiguation = nl.disambiguation
        FROM musicbrainz.track_name_lookup nl
        WHERE t.artist_name = nl.artist_name
          AND t.album_name  = nl.album_name
          AND t.name        = nl.track_name
          AND t.disambiguation IS NULL AND nl.disambiguation IS NOT NULL
        """);
    Console.WriteLine($"    disambiguation:  {nameDisamb,10:N0} updated");

    if (languageOk)
    {
        var nameLang = await Execute(db, """
            UPDATE tracks t SET language = nl.language
            FROM musicbrainz.track_name_lookup nl
            WHERE t.artist_name = nl.artist_name
              AND t.album_name  = nl.album_name
              AND t.name        = nl.track_name
              AND t.language IS NULL AND nl.language IS NOT NULL
            """);
        Console.WriteLine($"    language:        {nameLang,10:N0} updated");
    }
    else
    {
        Console.WriteLine("    language:        skipped (layout check failed)");
    }

    totalSw.Stop();
    Console.WriteLine();
    Console.WriteLine($"Backfill complete in {totalSw.Elapsed:hh\\:mm\\:ss}");
    Console.WriteLine();
    Console.Write("Drop musicbrainz schema? (y/n): ");
    if (Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true)
    {
        await Execute(db, "DROP SCHEMA musicbrainz CASCADE");
        Console.WriteLine("  Schema dropped.");
    }
}

// ── Helpers ──────────────────────────────────────────────────────────

static async Task<List<string?[]>> QueryRows(NpgsqlConnection connection, string sql, int limit = 50)
{
    var rows = new List<string?[]>();
    await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 7200 };
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync() && rows.Count < limit)
    {
        var values = new string?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            values[i] = reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
        rows.Add(values);
    }
    return rows;
}

static string Trunc(string? s, int max)
{
    if (string.IsNullOrEmpty(s)) return "-";
    return s.Length <= max ? s : s[..(max - 1)] + "…";
}

static async Task<int> Execute(NpgsqlConnection connection, string sql)
{
    await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 7200 };
    return await cmd.ExecuteNonQueryAsync();
}

static async Task ReconcileColumns(NpgsqlConnection connection, string schemaTable, string filePath)
{
    string? firstLine;
    using (var reader = new StreamReader(filePath))
        firstLine = await reader.ReadLineAsync();

    if (string.IsNullOrEmpty(firstLine))
        return;

    var dumpColumns = firstLine.Split('\t').Length;
    var dot = schemaTable.IndexOf('.');
    var schema = schemaTable[..dot];
    var table = schemaTable[(dot + 1)..];

    var declaredColumns = (int)await Scalar<long>(connection,
        $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = '{schema}' AND table_name = '{table}'");

    if (dumpColumns <= declaredColumns)
        return;

    for (var i = declaredColumns; i < dumpColumns; i++)
        await Execute(connection, $"ALTER TABLE {schemaTable} ADD COLUMN mb_extra_{i} TEXT");

    Console.WriteLine($"    {table}: dump has {dumpColumns} columns vs {declaredColumns} declared; added {dumpColumns - declaredColumns} filler column(s)");
}

static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
{
    await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 7200 };
    return (T)(await cmd.ExecuteScalarAsync())!;
}

static long ImportTsvFile(NpgsqlConnection connection, string tableName, string filePath, Func<string, bool>? lineFilter = null)
{
    using var writer = connection.BeginTextImport(
        $"COPY {tableName} FROM STDIN WITH (FORMAT text, DELIMITER E'\\t', NULL '\\N')");

    long count = 0;
    long readCount = 0;
    using var reader = new StreamReader(filePath);

    while (reader.ReadLine() is { } line)
    {
        readCount++;
        if (lineFilter != null && !lineFilter(line)) continue;

        writer.WriteLine(line);
        count++;

        if (readCount % 500_000 == 0)
            Console.Write($"\r    {readCount:N0} read, {count:N0} kept...");
    }

    if (readCount >= 500_000)
        Console.WriteLine($"\r    {readCount:N0} read, {count:N0} kept    ");

    return count;
}
