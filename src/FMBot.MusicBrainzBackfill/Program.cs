using System.Diagnostics;
using Npgsql;

if (args.Length < 2)
{
    Console.WriteLine("MusicBrainz Album Backfill Tool");
    Console.WriteLine("===============================");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -- \"<connection-string>\" <mbdump-directory>");
    Console.WriteLine();
    Console.WriteLine("Steps to get MusicBrainz dump data:");
    Console.WriteLine("  1. Go to https://data.metabrainz.org/pub/musicbrainz/data/fullexport/");
    Console.WriteLine("  2. Pick the latest date directory");
    Console.WriteLine("  3. Download mbdump.tar.zst (~4 GB)");
    Console.WriteLine("  4. Extract with 7-Zip or: tar --zstd -xf mbdump.tar.zst");
    Console.WriteLine("  5. Run this tool pointing to the extracted mbdump/ directory");
    return;
}

var connectionString = args[0];
var dumpPath = args[1];

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

var connBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    CommandTimeout = 7200,
    Timeout = 300
};

await using var db = new NpgsqlConnection(connBuilder.ToString());
await db.OpenAsync();

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
    return s.Length <= max ? s : s[..(max - 1)] + "\u2026";
}

static async Task<int> Execute(NpgsqlConnection connection, string sql)
{
    await using var cmd = new NpgsqlCommand(sql, connection) { CommandTimeout = 7200 };
    return await cmd.ExecuteNonQueryAsync();
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
