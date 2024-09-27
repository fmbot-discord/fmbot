using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Npgsql;

namespace FMBot.Bot.Models.TemplateOptions;

public record FmResult(string Content);

public abstract class TemplateOption
{
    public FmFooterOption FooterOption { get; set; }

    public int FooterOrder { get; set; }

    public string Variable { get; set; }
}

public sealed class SqlTemplateOption : TemplateOption
{
    public string SqlQuery { get; set; }
    public Func<TemplateContext, DbDataReader, Task<FmResult>> ResultProcessor { get; set; }
    public Func<TemplateContext, Dictionary<string, object>> ParametersFactory { get; set; }

    public bool ProcessMultipleRows { get; set; } = false;

    public Task<FmResult> ExecuteAsync(TemplateContext context, DbDataReader reader)
    {
        return this.ResultProcessor(context, reader);
    }

    public NpgsqlBatchCommand CreateBatchCommand(TemplateContext context)
    {
        var command = new NpgsqlBatchCommand(this.SqlQuery);
        var parameters = this.ParametersFactory(context);
        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue(param.Key, param.Value);
        }

        return command;
    }
}

public sealed class ComplexTemplateOption : TemplateOption
{
    public Func<TemplateContext, Task<FmResult>> ExecutionLogic { get; set; }

    public Task<FmResult> ExecuteAsync(TemplateContext context, DbDataReader reader)
    {
        return this.ExecutionLogic(context);
    }
}

public class TemplateContext
{
    public UserSettingsModel UserSettings { get; set; }
    public NpgsqlConnection Connection { get; set; }

    public RecentTrack CurrentTrack { get; set; }

    public RecentTrack PreviousTrack { get; set; }

    public long TotalScrobbles { get; set; }
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

public enum EmbedOption
{
    [Option("Author")]
    Author = 10,
    [Option("Author icon url")]
    AuthorIconUrl = 11,
    [Option("Author url")]
    AuthorUrl = 12,

    [Option("Title")]
    Title = 20,
    [Option("Url")]
    Url = 21,
    [Option("Description")]
    Description = 22,
    [Option("Thumbnail image url")]
    ThumbnailImageUrl = 23,
    [Option("Large image url")]
    LargeImageUrl = 24,
    [Option("Color (hex code)")]
    ColorHex = 25,

    [Option("Add field")]
    AddField = 30,

    [Option("Footer")]
    Footer = 40,
    [Option("Footer icon url")]
    FooterIconUrl = 41,
    [Option("Footer timestamp")]
    FooterTimestamp = 42,

    [Option("Add button")]
    AddButton = 50,
}

public static class ExampleTemplates
{
    public static readonly List<Template> Templates = new()
    {
        new Template
        {
            Id = 1,
            Type = TemplateType.Fm,
            Content = @"$$fm-template
$$title:Now playing for {{user.display-name}}
$$description:[{{track.name}}]({{track.url}})
{{track.artist}} - *{{track.album}}*
$$footer:{{lastfm.total-scrobbles}}",
            Name = "Example",
            ShareCode = "ABCD",
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        }
    };

}

public static class TemplateOptions
{
    public static readonly List<TemplateOption> Options = new()
    {
        new ComplexTemplateOption
        {
            Variable = "lastfm.username",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.UserSettings.UserNameLastFm))
        },
        new ComplexTemplateOption
        {
            Variable = "lastfm.join-date",
            ExecutionLogic = context =>
                Task.FromResult(new FmResult(context.UserSettings.RegisteredLastFm?.ToString("MMMM d yyyy")))
        },
        new ComplexTemplateOption
        {
            Variable = "lastfm.total-scrobbles",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.TotalScrobbles.ToString()))
        },
        new ComplexTemplateOption
        {
            Variable = "user.displayname",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.UserSettings.DisplayName))
        },
        new ComplexTemplateOption
        {
            Variable = "user.user-type",
            ExecutionLogic = context => Task.FromResult(new FmResult(Enum.GetName(context.UserSettings.UserType)))
        },
        new ComplexTemplateOption
        {
            Variable = "user.user-type-emoji",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.UserSettings.UserType.UserTypeToIcon()))
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.Loved,
            Variable = "track.loved",
            FooterOrder = 10,
            ExecutionLogic = context =>
                Task.FromResult(context.CurrentTrack.Loved ? new FmResult("❤️ Loved track") : null)
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistPlays,
            Variable = "artist.plays",
            FooterOrder = 20,
            SqlQuery = "SELECT ua.playcount FROM user_artists AS ua WHERE ua.user_id = @userId AND " +
                       "UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new FmResult($"{playcount} artist scrobbles");
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.AlbumPlays,
            Variable = "album.plays",
            FooterOrder = 30,
            SqlQuery = "SELECT ua.playcount FROM user_albums AS ua WHERE ua.user_id = @userId AND " +
                       "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new FmResult($"{playcount} album scrobbles");
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "albumName", context.CurrentTrack.AlbumName },
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.TrackPlays,
            Variable = "track.plays",
            FooterOrder = 40,
            SqlQuery = "SELECT ut.playcount FROM user_tracks AS ut WHERE ut.user_id = @userId AND " +
                       "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new FmResult($"{playcount} track scrobbles");
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "trackName", context.CurrentTrack.TrackName },
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.TotalScrobbles,
            Variable = "lastfm.totalscrobbles",
            FooterOrder = 50,
            ExecutionLogic = context =>
                Task.FromResult(new FmResult($"{context.TotalScrobbles} total scrobbles"))
        },
        new ComplexTemplateOption
        {
            Variable = "album.url",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.AlbumUrl))
        },
        new ComplexTemplateOption
        {
            Variable = "album.cover-url",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.AlbumCoverUrl))
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ArtistPlaysThisWeek,
            Variable = "artist.weekplays",
            FooterOrder = 60,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-7);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase));
                return new FmResult($"{count} artist plays this week");
            }
        },
        new ComplexTemplateOption
        {
            Variable = "artist.url",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.ArtistUrl))
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistCountry,
            Variable = "artist.country",
            FooterOrder = 100,
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
                            return new FmResult(artistCountry.Name);
                        }
                    }
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistBirthday,
            Variable = "artist.birthday",
            FooterOrder = 110,
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

                    if (startDate.Month != 1 || startDate.Day != 1)
                    {
                        var age = GetAgeInYears(startDate);
                        var today = DateTime.Today;

                        if (startDate.Month == today.Month && startDate.Day == today.Day)
                        {
                            return !endDate.HasValue
                                ? new FmResult($"🎂 today! ({age})")
                                : new FmResult("🎂 today!");
                        }

                        if (startDate.Month == today.AddDays(1).Month && startDate.Day == today.AddDays(1).Day)
                        {
                            return !endDate.HasValue
                                ? new FmResult($"🎂 tomorrow (becomes {age + 1})")
                                : new FmResult("🎂 tomorrow");
                        }

                        return !endDate.HasValue
                            ? new FmResult($"🎂 {startDate:MMMM d} (currently {age})")
                            : new FmResult($"🎂 {startDate:MMMM d}");
                    }
                }

                return null;

                int GetAgeInYears(DateTime birthDate)
                {
                    var now = DateTime.UtcNow;
                    var age = now.Year - birthDate.Year;

                    if (now.Month < birthDate.Month || (now.Month == birthDate.Month && now.Day < birthDate.Day))
                    {
                        age--;
                    }

                    return age;
                }
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistGenres,
            Variable = "artist.genres",
            FooterOrder = 200,
            ProcessMultipleRows = true,
            SqlQuery = @"
                    SELECT ag.name
                    FROM public.artists a
                    JOIN public.artist_genres ag ON a.id = ag.artist_id
                    WHERE UPPER(a.name) = UPPER(CAST(@artistName AS CITEXT))
                    ORDER BY ag.id
                    LIMIT 6",
            ResultProcessor = async (context, reader) =>
            {
                var genres = new List<ArtistGenre>();
                while (await reader.ReadAsync())
                {
                    genres.Add(new ArtistGenre
                    {
                        Name = reader.GetString(0)
                    });
                }

                if (genres.Any())
                {
                    context.Genres = GenreService.GenresToString(genres);
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new ComplexTemplateOption
        {
            Variable = "track.name",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.TrackName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.album",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.AlbumName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.artist",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.ArtistName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.url",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.CurrentTrack.TrackUrl))
        },
        new ComplexTemplateOption
        {
            Variable = "previousTrack.name",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.PreviousTrack.TrackName))
        },
        new ComplexTemplateOption
        {
            Variable = "previousTrack.album",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.PreviousTrack.AlbumName))
        },
        new ComplexTemplateOption
        {
            Variable = "previousTrack.artist",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.PreviousTrack.ArtistName))
        },
        new ComplexTemplateOption
        {
            Variable = "previousTrack.url",
            ExecutionLogic = context => Task.FromResult(new FmResult(context.PreviousTrack.TrackUrl))
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.TrackBpm,
            Variable = "track.bpm",
            FooterOrder = 300,
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
                    return new FmResult($"bpm {tempo:0.0}");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName },
                { "trackName", context.CurrentTrack.TrackName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.TrackDuration,
            Variable = "track.duration",
            FooterOrder = 310,
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

                    return new FmResult($"{emoji} {formattedTrackLength}");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName },
                { "trackName", context.CurrentTrack.TrackName }
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.DiscogsCollection,
            Variable = "discogs.collection-item",
            FooterOrder = 400,
            ExecutionLogic = async context =>
            {
                if (string.IsNullOrEmpty(context.CurrentTrack.AlbumName))
                {
                    return null;
                }

                var discogsUser = await context.UserService.GetUserWithDiscogs(context.UserSettings.DiscordUserId);
                if (discogsUser.UserDiscogs == null || !discogsUser.DiscogsReleases.Any())
                {
                    return null;
                }

                var albumCollection = await Task.Run(() => discogsUser.DiscogsReleases.Where(w =>
                        (w.Release.Title.StartsWith(context.CurrentTrack.AlbumName,
                             StringComparison.OrdinalIgnoreCase) ||
                         context.CurrentTrack.AlbumName.StartsWith(w.Release.Title,
                             StringComparison.OrdinalIgnoreCase))
                        &&
                        (w.Release.Artist.StartsWith(context.CurrentTrack.ArtistName,
                             StringComparison.OrdinalIgnoreCase) ||
                         context.CurrentTrack.ArtistName.StartsWith(w.Release.Artist,
                             StringComparison.OrdinalIgnoreCase)))
                    .ToList());

                var discogsAlbum = await Task.Run(() => albumCollection.MaxBy(o => o.DateAdded));
                return discogsAlbum != null
                    ? new FmResult(StringService.UserDiscogsReleaseToSimpleString(discogsAlbum))
                    : null;
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.CrownHolder,
            Variable = "crown.current-holder",
            FooterOrder = 500,
            SqlQuery = @"
                    SELECT uc.current_playcount, gu.user_id, gu.user_name
                    FROM public.user_crowns AS uc
                    INNER JOIN guild_users AS gu ON gu.user_id = uc.user_id AND gu.guild_id = @guildId
                    WHERE uc.guild_id = @guildId
                      AND uc.active = true
                      AND UPPER(uc.artist_name) = UPPER(CAST(@artistName AS CITEXT))
                    ORDER BY uc.current_playcount DESC
                    LIMIT 1",
            ResultProcessor = async (context, reader) =>
            {
                if (context.Guild == null || context.Guild.CrownsDisabled == true)
                {
                    return null;
                }

                if (!await reader.IsDBNullAsync(0))
                {
                    var currentPlaycount = await reader.GetFieldValueAsync<int>(0);
                    var userName = await reader.GetFieldValueAsync<string>(2);
                    return new FmResult($"👑 {Format.Sanitize(userName)} ({currentPlaycount} plays)");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "guildId", context.Guild?.GuildId ?? 0 },
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerArtistRank,
            Variable = "server.artist-whoknows-rank",
            FooterOrder = 600,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName);
                artistListeners = WhoKnowsService.FilterWhoKnowsObjects(artistListeners, context.Guild)
                    .filteredUsers;

                if (artistListeners.Any())
                {
                    var requestedUser =
                        artistListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = artistListeners.IndexOf(requestedUser);
                        return new FmResult($"WhoKnows #{index + 1}");
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerArtistListeners,
            Variable = "server.artist-listeners",
            FooterOrder = 610,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName);
                artistListeners = WhoKnowsService.FilterWhoKnowsObjects(artistListeners, context.Guild)
                    .filteredUsers;

                return artistListeners.Any() ? new FmResult($"{artistListeners.Count} listeners") : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerAlbumRank,
            Variable = "server.album-whoknows-rank",
            FooterOrder = 620,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null || context.CurrentTrack.AlbumName == null)
                {
                    return null;
                }

                var albumListeners = await context.WhoKnowsAlbumService.GetIndexedUsersForAlbum(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName,
                    context.CurrentTrack.AlbumName);
                albumListeners = WhoKnowsService.FilterWhoKnowsObjects(albumListeners, context.Guild).filteredUsers;

                if (albumListeners.Any())
                {
                    var requestedUser = albumListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = albumListeners.IndexOf(requestedUser);
                        return new FmResult($"WhoKnows album #{index + 1}");
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerAlbumListeners,
            Variable = "server.album-listeners",
            FooterOrder = 630,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null || context.CurrentTrack.AlbumName == null)
                {
                    return null;
                }

                var albumListeners = await context.WhoKnowsAlbumService.GetIndexedUsersForAlbum(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName,
                    context.CurrentTrack.AlbumName);
                albumListeners = WhoKnowsService.FilterWhoKnowsObjects(albumListeners, context.Guild).filteredUsers;

                return albumListeners.Any()
                    ? new FmResult($"{albumListeners.Count} album listeners")
                    : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerTrackRank,
            Variable = "server.track-whoknows-rank",
            FooterOrder = 640,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var trackListeners = await context.WhoKnowsTrackService.GetIndexedUsersForTrack(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName,
                    context.CurrentTrack.TrackName);
                trackListeners = WhoKnowsService.FilterWhoKnowsObjects(trackListeners, context.Guild).filteredUsers;

                if (trackListeners.Any())
                {
                    var requestedUser = trackListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = trackListeners.IndexOf(requestedUser);
                        return new FmResult($"WhoKnows track #{index + 1}");
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerTrackListeners,
            Variable = "server.track-listeners",
            FooterOrder = 650,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var trackListeners = await context.WhoKnowsTrackService.GetIndexedUsersForTrack(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName,
                    context.CurrentTrack.TrackName);
                trackListeners = WhoKnowsService.FilterWhoKnowsObjects(trackListeners, context.Guild).filteredUsers;

                return trackListeners.Any()
                    ? new FmResult($"{trackListeners.Count} track listeners")
                    : null;
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.GlobalArtistRank,
            Variable = "global.artist-whoknows-rank",
            FooterOrder = 700,
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
                    return new FmResult($"GlobalWhoKnows #{rank}");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.GlobalAlbumRank,
            Variable = "global.album-whoknows-rank",
            FooterOrder = 710,
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
                    return new FmResult($"GlobalWhoKnows album #{rank}");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "albumName", context.CurrentTrack.AlbumName }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.GlobalTrackRank,
            Variable = "global.track-whoknows-rank",
            FooterOrder = 720,
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
                    return new FmResult($"GlobalWhoKnows track #{rank}");
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "trackName", context.CurrentTrack.TrackName }
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.FirstArtistListen,
            Variable = "discovery.artist-discovered",
            FooterOrder = 800,
            ExecutionLogic = async context =>
            {
                if (!SupporterService.IsSupporter(context.UserSettings.UserType))
                {
                    return null;
                }

                var firstPlay =
                    await context.PlayService.GetArtistFirstPlayDate(context.UserSettings.UserId,
                        context.CurrentTrack.ArtistName);
                return firstPlay != null
                    ? new FmResult($"Artist discovered {firstPlay.Value.ToString("MMMM d yyyy")}")
                    : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.FirstAlbumListen,
            Variable = "discovery.album-discovered",
            FooterOrder = 810,
            ExecutionLogic = async context =>
            {
                if (!SupporterService.IsSupporter(context.UserSettings.UserType) ||
                    context.CurrentTrack.AlbumName == null)
                {
                    return null;
                }

                var firstPlay = await context.PlayService.GetAlbumFirstPlayDate(context.UserSettings.UserId,
                    context.CurrentTrack.ArtistName, context.CurrentTrack.AlbumName);
                return firstPlay != null
                    ? new FmResult($"Album discovered {firstPlay.Value.ToString("MMMM d yyyy")}")
                    : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.FirstTrackListen,
            Variable = "discovery.track-discovered",
            FooterOrder = 820,
            ExecutionLogic = async context =>
            {
                if (!SupporterService.IsSupporter(context.UserSettings.UserType))
                {
                    return null;
                }

                var firstPlay = await context.PlayService.GetTrackFirstPlayDate(context.UserSettings.UserId,
                    context.CurrentTrack.ArtistName, context.CurrentTrack.TrackName);
                return firstPlay != null
                    ? new FmResult($"Track discovered {firstPlay.Value.ToString("MMMM d yyyy")}")
                    : null;
            }
        }
    };
}
