using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Npgsql;
using SpotifyAPI.Web;

namespace FMBot.Bot.Models.FmOptions;

public record FmOptionResult(string Content, int Order);

public abstract class FmOption
{
    public FmFooterOption Option { get; set; }
    public abstract Task<FmOptionResult> ExecuteAsync(FmContext context, DbDataReader reader);
    public abstract NpgsqlBatchCommand CreateBatchCommand(FmContext context);

    public int Order { get; set; }

    public string Variable { get; set; }
}

public class SqlFmOption : FmOption
{
    public string SqlQuery { get; set; }
    public Func<FmContext, DbDataReader, Task<FmOptionResult>> ResultProcessor { get; set; }
    public Func<FmContext, Dictionary<string, object>> ParametersFactory { get; set; }

    public override Task<FmOptionResult> ExecuteAsync(FmContext context, DbDataReader reader)
    {
        return ResultProcessor(context, reader);
    }

    public override NpgsqlBatchCommand CreateBatchCommand(FmContext context)
    {
        var command = new NpgsqlBatchCommand(SqlQuery);
        var parameters = ParametersFactory(context);
        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue(param.Key, param.Value);
        }

        return command;
    }
}

public class ComplexFmOption : FmOption
{
    public Func<FmContext, Task<FmOptionResult>> ExecutionLogic { get; set; }

    public override Task<FmOptionResult> ExecuteAsync(FmContext context, DbDataReader reader)
    {
        return ExecutionLogic(context);
    }

    public override NpgsqlBatchCommand CreateBatchCommand(FmContext context)
    {
        return null; // Complex options don't use SQL batch commands
    }
}

public class FmContext
{
    public UserSettingsModel UserSettings { get; set; }
    public NpgsqlConnection Connection { get; set; }
    public string ArtistName { get; set; }
    public string AlbumName { get; set; }
    public string TrackName { get; set; }

    public long TotalScrobbles { get; set; }
    public bool Loved { get; set; }
    public Guild Guild { get; set; }

    public string Genres { get; set; }

    public IDictionary<int, FullGuildUser> GuildUsers { get; set; }
    public WhoKnowsArtistService WhoKnowsArtistService { get; set; }
    public WhoKnowsAlbumService WhoKnowsAlbumService { get; set; }
    public WhoKnowsTrackService WhoKnowsTrackService { get; set; }
    public CountryService CountryService { get; set; }
    public UserService UserService { get; set; }
    public PlayService PlayService { get; set; }
}

public class FmOptionsHandler
{
    private readonly List<FmOption> _options;

    public FmOptionsHandler()
    {
        _options = new List<FmOption>
        {
            new ComplexFmOption
            {
                Option = FmFooterOption.Loved,
                ExecutionLogic = context =>
                    Task.FromResult(context.Loved ? new FmOptionResult("❤️ Loved track", 10) : null)
            },
            new SqlFmOption
            {
                Option = FmFooterOption.ArtistPlays,
                SqlQuery = "SELECT ua.playcount FROM user_artists AS ua WHERE ua.user_id = @userId AND " +
                           "UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                    return new FmOptionResult($"{playcount} artist scrobbles", 20);
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.AlbumPlays,
                SqlQuery = "SELECT ua.playcount FROM user_albums AS ua WHERE ua.user_id = @userId AND " +
                           "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                    return new FmOptionResult($"{playcount} album scrobbles", 30);
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName },
                    { "albumName", context.AlbumName },
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.TrackPlays,
                SqlQuery = "SELECT ut.playcount FROM user_tracks AS ut WHERE ut.user_id = @userId AND " +
                           "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                    return new FmOptionResult($"{playcount} track scrobbles", 40);
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName },
                    { "trackName", context.TrackName },
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.TotalScrobbles,
                ExecutionLogic = context =>
                    Task.FromResult(new FmOptionResult($"{context.TotalScrobbles} total scrobbles", 45))
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ArtistPlaysThisWeek,
                ExecutionLogic = async context =>
                {
                    var start = DateTime.UtcNow.AddDays(-7);
                    var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                        context.Connection, start);
                    var count = plays.Count(a =>
                        a.ArtistName.Equals(context.ArtistName, StringComparison.OrdinalIgnoreCase));
                    return new FmOptionResult($"{count} artist plays this week", 50);
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.ArtistCountry,
                SqlQuery = @"
                    SELECT country_code
                    FROM public.artists
                    WHERE UPPER(name) = UPPER(CAST(@artistName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var countryCode = await reader.GetFieldValueAsync<string>(0);
                        if (!string.IsNullOrWhiteSpace(countryCode))
                        {
                            var artistCountry = context.CountryService.GetValidCountry(countryCode);
                            if (artistCountry?.Name != null)
                            {
                                return new FmOptionResult(artistCountry.Name, 60);
                            }
                        }
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "artistName", context.ArtistName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.ArtistBirthday,
                SqlQuery = @"
                    SELECT start_date, end_date
                    FROM public.artists
                    WHERE UPPER(name) = UPPER(CAST(@artistName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var startDate = await reader.GetFieldValueAsync<DateTime>(0);
                        var endDate = await reader.IsDBNullAsync(1)
                            ? (DateTime?)null
                            : await reader.GetFieldValueAsync<DateTime>(1);
                        var order = 70;

                        if (startDate.Month != 1 || startDate.Day != 1)
                        {
                            var age = GetAgeInYears(startDate);
                            var today = DateTime.Today;

                            if (startDate.Month == today.Month && startDate.Day == today.Day)
                            {
                                return !endDate.HasValue
                                    ? new FmOptionResult($"🎂 today! ({age})", order)
                                    : new FmOptionResult("🎂 today!", order);
                            }
                            else if (startDate.Month == today.AddDays(1).Month && startDate.Day == today.AddDays(1).Day)
                            {
                                return !endDate.HasValue
                                    ? new FmOptionResult($"🎂 tomorrow (becomes {age + 1})", order)
                                    : new FmOptionResult("🎂 tomorrow", order);
                            }
                            else
                            {
                                return !endDate.HasValue
                                    ? new FmOptionResult($"🎂 {startDate:MMMM d} (currently {age})", order)
                                    : new FmOptionResult($"🎂 {startDate:MMMM d}", order);
                            }
                        }
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "artistName", context.ArtistName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.ArtistGenres,
                SqlQuery = @"
                    SELECT ag.*
                    FROM public.artists a
                    JOIN public.artist_genres ag ON a.id = ag.artist_id
                    WHERE UPPER(a.name) = UPPER(CAST(@artistName AS CITEXT))
                    LIMIT 6",
                ResultProcessor = async (context, reader) =>
                {
                    var genres = new List<ArtistGenre>();
                    while (await reader.ReadAsync())
                    {
                        genres.Add(new ArtistGenre
                        {
                            Id = await reader.GetFieldValueAsync<int>(reader.GetOrdinal("id")),
                            ArtistId = await reader.GetFieldValueAsync<int>(reader.GetOrdinal("artist_id")),
                            Name = await reader.GetFieldValueAsync<string>(reader.GetOrdinal("name"))
                        });
                    }

                    context.Genres = GenreService.GenresToString(genres.Take(6).ToList());
                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "artistName", context.ArtistName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.TrackBpm,
                SqlQuery = @"
                    SELECT tempo
                    FROM public.tracks
                    WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT))
                      AND UPPER(name) = UPPER(CAST(@trackName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var tempo = await reader.GetFieldValueAsync<float>(0);
                        return new FmOptionResult($"bpm {tempo:0.0}", 110);
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "artistName", context.ArtistName },
                    { "trackName", context.TrackName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.TrackDuration,
                SqlQuery = @"
                    SELECT duration_ms
                    FROM public.tracks
                    WHERE UPPER(artist_name) = UPPER(CAST(@artistName AS CITEXT))
                      AND UPPER(name) = UPPER(CAST(@trackName AS CITEXT))",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var durationMs = await reader.GetFieldValueAsync<int>(0);
                        var trackLength = TimeSpan.FromMilliseconds(durationMs);
                        var formattedTrackLength =
                            $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

                        var emoji = trackLength.Minutes switch
                        {
                            0 => "🕛", 1 => "🕐", 2 => "🕑", 3 => "🕒", 4 => "🕓", 5 => "🕔",
                            6 => "🕕", 7 => "🕖", 8 => "🕗", 9 => "🕘", 10 => "🕙", 11 => "🕚",
                            12 => "🕛", _ => "🕒"
                        };

                        return new FmOptionResult($"{emoji} {formattedTrackLength}", 120);
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "artistName", context.ArtistName },
                    { "trackName", context.TrackName }
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.DiscogsCollection,
                ExecutionLogic = async context =>
                {
                    if (string.IsNullOrEmpty(context.AlbumName))
                    {
                        return null;
                    }

                    var discogsUser = await context.UserService.GetUserWithDiscogs(context.UserSettings.DiscordUserId);
                    if (discogsUser.UserDiscogs == null || !discogsUser.DiscogsReleases.Any())
                    {
                        return null;
                    }

                    var albumCollection = await Task.Run(() => discogsUser.DiscogsReleases.Where(w =>
                            (w.Release.Title.StartsWith(context.AlbumName, StringComparison.OrdinalIgnoreCase) ||
                             context.AlbumName.StartsWith(w.Release.Title, StringComparison.OrdinalIgnoreCase))
                            &&
                            (w.Release.Artist.StartsWith(context.ArtistName, StringComparison.OrdinalIgnoreCase) ||
                             context.ArtistName.StartsWith(w.Release.Artist, StringComparison.OrdinalIgnoreCase)))
                        .ToList());

                    var discogsAlbum = await Task.Run(() => albumCollection.MaxBy(o => o.DateAdded));
                    return discogsAlbum != null
                        ? new FmOptionResult(StringService.UserDiscogsReleaseToSimpleString(discogsAlbum), 130)
                        : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.CrownHolder,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null || context.Guild.CrownsDisabled == true)
                    {
                        return null;
                    }

                    var currentCrownHolder = await CrownService.GetCurrentCrownHolderWithName(context.Connection,
                        context.Guild.GuildId, context.ArtistName);
                    return currentCrownHolder != null
                        ? new FmOptionResult(
                            $"👑 {Format.Sanitize(currentCrownHolder.UserName)} ({currentCrownHolder.CurrentPlaycount} plays)",
                            140)
                        : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerArtistRank,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null)
                    {
                        return null;
                    }

                    var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName);
                    artistListeners = WhoKnowsService.FilterWhoKnowsObjects(artistListeners, context.Guild)
                        .filteredUsers;

                    if (artistListeners.Any())
                    {
                        var requestedUser =
                            artistListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                        if (requestedUser != null)
                        {
                            var index = artistListeners.IndexOf(requestedUser);
                            return new FmOptionResult($"WhoKnows #{index + 1}", 150);
                        }
                    }

                    return null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerArtistListeners,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null)
                    {
                        return null;
                    }

                    var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName);
                    artistListeners = WhoKnowsService.FilterWhoKnowsObjects(artistListeners, context.Guild)
                        .filteredUsers;

                    return artistListeners.Any() ? new FmOptionResult($"{artistListeners.Count} listeners", 160) : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerAlbumRank,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null || context.AlbumName == null)
                    {
                        return null;
                    }

                    var albumListeners = await context.WhoKnowsAlbumService.GetIndexedUsersForAlbum(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName, context.AlbumName);
                    albumListeners = WhoKnowsService.FilterWhoKnowsObjects(albumListeners, context.Guild).filteredUsers;

                    if (albumListeners.Any())
                    {
                        var requestedUser = albumListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                        if (requestedUser != null)
                        {
                            var index = albumListeners.IndexOf(requestedUser);
                            return new FmOptionResult($"WhoKnows album #{index + 1}", 170);
                        }
                    }

                    return null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerAlbumListeners,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null || context.AlbumName == null)
                    {
                        return null;
                    }

                    var albumListeners = await context.WhoKnowsAlbumService.GetIndexedUsersForAlbum(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName, context.AlbumName);
                    albumListeners = WhoKnowsService.FilterWhoKnowsObjects(albumListeners, context.Guild).filteredUsers;

                    return albumListeners.Any()
                        ? new FmOptionResult($"{albumListeners.Count} album listeners", 180)
                        : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerTrackRank,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null)
                    {
                        return null;
                    }

                    var trackListeners = await context.WhoKnowsTrackService.GetIndexedUsersForTrack(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName, context.TrackName);
                    trackListeners = WhoKnowsService.FilterWhoKnowsObjects(trackListeners, context.Guild).filteredUsers;

                    if (trackListeners.Any())
                    {
                        var requestedUser = trackListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                        if (requestedUser != null)
                        {
                            var index = trackListeners.IndexOf(requestedUser);
                            return new FmOptionResult($"WhoKnows track #{index + 1}", 190);
                        }
                    }

                    return null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.ServerTrackListeners,
                ExecutionLogic = async context =>
                {
                    if (context.Guild == null)
                    {
                        return null;
                    }

                    var trackListeners = await context.WhoKnowsTrackService.GetIndexedUsersForTrack(null,
                        context.GuildUsers, context.Guild.GuildId, context.ArtistName, context.TrackName);
                    trackListeners = WhoKnowsService.FilterWhoKnowsObjects(trackListeners, context.Guild).filteredUsers;

                    return trackListeners.Any()
                        ? new FmOptionResult($"{trackListeners.Count} track listeners", 200)
                        : null;
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.GlobalArtistRank,
                SqlQuery = @"
        WITH ranked_users AS (
            SELECT
                ua.user_id,
                ua.playcount,
                ROW_NUMBER() OVER (ORDER BY ua.playcount DESC) as rank
            FROM (
                SELECT DISTINCT ON (UPPER(u.user_name_last_fm))
                    ua.user_id,
                    ua.playcount
                FROM user_artists AS ua
                JOIN users AS u ON ua.user_id = u.user_id
                WHERE UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM botted_users WHERE ban_active = true)
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM global_filtered_users WHERE created >= NOW() - INTERVAL '3 months')
                ORDER BY UPPER(u.user_name_last_fm), ua.playcount DESC
            ) ua
        )
        SELECT rank
        FROM ranked_users
        WHERE user_id = @userId",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var rank = await reader.GetFieldValueAsync<long>(0);
                        return new FmOptionResult($"GlobalWhoKnows #{rank}", 300);
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.GlobalAlbumRank,
                SqlQuery = @"
        WITH ranked_users AS (
            SELECT
                ub.user_id,
                ub.playcount,
                ROW_NUMBER() OVER (ORDER BY ub.playcount DESC) as rank
            FROM (
                SELECT DISTINCT ON (UPPER(u.user_name_last_fm))
                    ub.user_id,
                    ub.playcount
                FROM user_albums AS ub
                JOIN users AS u ON ub.user_id = u.user_id
                WHERE UPPER(ub.name) = UPPER(CAST(@albumName AS CITEXT))
                  AND UPPER(ub.artist_name) = UPPER(CAST(@artistName AS CITEXT))
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM botted_users WHERE ban_active = true)
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM global_filtered_users WHERE created >= NOW() - INTERVAL '3 months')
                ORDER BY UPPER(u.user_name_last_fm), ub.playcount DESC
            ) ub
        )
        SELECT rank
        FROM ranked_users
        WHERE user_id = @userId",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var rank = await reader.GetFieldValueAsync<long>(0);
                        return new FmOptionResult($"GlobalWhoKnows album #{rank}", 310);
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName },
                    { "albumName", context.AlbumName }
                }
            },
            new SqlFmOption
            {
                Option = FmFooterOption.GlobalTrackRank,
                SqlQuery = @"
        WITH ranked_users AS (
            SELECT
                ut.user_id,
                ut.playcount,
                ROW_NUMBER() OVER (ORDER BY ut.playcount DESC) as rank
            FROM (
                SELECT DISTINCT ON (UPPER(u.user_name_last_fm))
                    ut.user_id,
                    ut.playcount
                FROM user_tracks AS ut
                JOIN users AS u ON ut.user_id = u.user_id
                WHERE UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT))
                  AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM botted_users WHERE ban_active = true)
                  AND NOT UPPER(u.user_name_last_fm) = ANY(SELECT UPPER(user_name_last_fm) FROM global_filtered_users WHERE created >= NOW() - INTERVAL '3 months')
                ORDER BY UPPER(u.user_name_last_fm), ut.playcount DESC
            ) ut
        )
        SELECT rank
        FROM ranked_users
        WHERE user_id = @userId",
                ResultProcessor = async (context, reader) =>
                {
                    if (!await reader.IsDBNullAsync(0))
                    {
                        var rank = await reader.GetFieldValueAsync<long>(0);
                        return new FmOptionResult($"GlobalWhoKnows track #{rank}", 320);
                    }

                    return null;
                },
                ParametersFactory = context => new Dictionary<string, object>
                {
                    { "userId", context.UserSettings.UserId },
                    { "artistName", context.ArtistName },
                    { "trackName", context.TrackName }
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.FirstArtistListen,
                ExecutionLogic = async context =>
                {
                    if (!SupporterService.IsSupporter(context.UserSettings.UserType))
                    {
                        return null;
                    }

                    var firstPlay =
                        await context.PlayService.GetArtistFirstPlayDate(context.UserSettings.UserId,
                            context.ArtistName);
                    return firstPlay != null
                        ? new FmOptionResult($"Artist discovered {firstPlay.Value.ToString("MMMM d yyyy")}", 400)
                        : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.FirstAlbumListen,
                ExecutionLogic = async context =>
                {
                    if (!SupporterService.IsSupporter(context.UserSettings.UserType) || context.AlbumName == null)
                    {
                        return null;
                    }

                    var firstPlay = await context.PlayService.GetAlbumFirstPlayDate(context.UserSettings.UserId,
                        context.ArtistName, context.AlbumName);
                    return firstPlay != null
                        ? new FmOptionResult($"Album discovered {firstPlay.Value.ToString("MMMM d yyyy")}", 410)
                        : null;
                }
            },
            new ComplexFmOption
            {
                Option = FmFooterOption.FirstTrackListen,
                ExecutionLogic = async context =>
                {
                    if (!SupporterService.IsSupporter(context.UserSettings.UserType))
                    {
                        return null;
                    }

                    var firstPlay = await context.PlayService.GetTrackFirstPlayDate(context.UserSettings.UserId,
                        context.ArtistName, context.TrackName);
                    return firstPlay != null
                        ? new FmOptionResult($"Track discovered {firstPlay.Value.ToString("MMMM d yyyy")}", 420)
                        : null;
                }
            }
        };
    }

    public async Task<List<string>> GetFooterAsync(FmFooterOption footerOptions, FmContext context)
    {
        var options = new ConcurrentBag<FmOptionResult>();
        var relevantOptions = _options.Where(o => footerOptions.HasFlag(o.Option)).ToList();

        // Prepare batch for SQL options
        await using var batch = new NpgsqlBatch(context.Connection);
        foreach (var option in relevantOptions.OfType<SqlFmOption>())
        {
            batch.BatchCommands.Add(option.CreateBatchCommand(context));
        }

        // Execute SQL batch
        if (batch.BatchCommands.Count > 0)
        {
            await using var reader = await batch.ExecuteReaderAsync();
            foreach (var option in relevantOptions.OfType<SqlFmOption>())
            {
                if (await reader.ReadAsync())
                {
                    var result = await option.ExecuteAsync(context, reader);
                    if (!string.IsNullOrEmpty(result?.Content))
                    {
                        options.Add(result);
                    }
                }

                await reader.NextResultAsync();
            }
        }

        // Execute complex options
        var complexTasks = relevantOptions.OfType<ComplexFmOption>()
            .Select(async o =>
            {
                var result = await o.ExecuteAsync(context, null);
                if (!string.IsNullOrEmpty(result?.Content))
                {
                    options.Add(result);
                }
            });

        await Task.WhenAll(complexTasks);

        return options.OrderBy(o => o.Order).Select(o => o.Content).ToList();
    }

    private static int GetAgeInYears(DateTime birthDate)
    {
        var now = DateTime.UtcNow;
        var age = now.Year - birthDate.Year;

        if (now.Month < birthDate.Month || (now.Month == birthDate.Month && now.Day < birthDate.Day))
        {
            age--;
        }

        return age;
    }
}
