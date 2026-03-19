using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using NetCord;
using Npgsql;

namespace FMBot.Bot.Models.TemplateOptions;

public record VariableResult(string Content, string Result = null);

public abstract class TemplateOption
{
    public FmFooterOption FooterOption { get; set; }

    public int FooterOrder { get; set; }

    public string Variable { get; set; }

    public string Description { get; set; }
    public VariableType VariableType { get; set; }
}

public sealed class SqlTemplateOption : TemplateOption
{
    public string SqlQuery { get; set; }
    public Func<TemplateContext, DbDataReader, Task<VariableResult>> ResultProcessor { get; set; }
    public Func<TemplateContext, Dictionary<string, object>> ParametersFactory { get; set; }

    public bool ProcessMultipleRows { get; set; } = false;

    public Task<VariableResult> ExecuteAsync(TemplateContext context, DbDataReader reader)
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
    public Func<TemplateContext, Task<VariableResult>> ExecutionLogic { get; set; }

    public Task<VariableResult> ExecuteAsync(TemplateContext context, DbDataReader reader)
    {
        return this.ExecutionLogic(context);
    }
}

public class TemplateContext
{
    public UserSettingsModel UserSettings { get; set; }
    public NetCord.User DiscordContextUser { get; set; }
    public NetCord.Gateway.Guild DiscordContextGuild { get; set; }

    public NpgsqlConnection Connection { get; set; }

    public RecentTrack CurrentTrack { get; set; }

    public Track DbTrack { get; set; }

    public RecentTrack PreviousTrack { get; set; }

    public long TotalScrobbles { get; set; }
    public Guild Guild { get; set; }

    public string Genres { get; set; }

    public NumberFormat NumberFormat { get; set; }

    public IDictionary<int, FullGuildUser> GuildUsers { get; set; }
    public WhoKnowsArtistService WhoKnowsArtistService { get; set; }
    public WhoKnowsAlbumService WhoKnowsAlbumService { get; set; }
    public WhoKnowsTrackService WhoKnowsTrackService { get; set; }
    public CountryService CountryService { get; set; }
    public UserService UserService { get; set; }
    public PlayService PlayService { get; set; }

    public EurovisionService EurovisionService { get; set; }
}

public enum EmbedOption
{
    [EmbedOption("author")]
    [EmbedOptionAccepts(VariableType.Text)]
    Author = 10,
    [EmbedOption("author-icon-url")]
    [EmbedOptionAccepts(VariableType.ImageUrl)]
    AuthorIconUrl = 11,
    [EmbedOption("author-url")]
    [EmbedOptionAccepts(VariableType.ResourceUrl)]
    AuthorUrl = 12,
    [EmbedOption("title")]
    [EmbedOptionAccepts(VariableType.Text)]
    Title = 20,
    [EmbedOption("url")]
    [EmbedOptionAccepts(VariableType.ResourceUrl, VariableType.ImageUrl)]
    Url = 21,
    [EmbedOption("description")]
    [EmbedOptionAccepts(VariableType.Text, VariableType.ResourceUrl, VariableType.ImageUrl)]
    Description = 22,
    [EmbedOption("thumbnail-image-url")]
    [EmbedOptionAccepts(VariableType.ImageUrl)]
    ThumbnailImageUrl = 23,
    [EmbedOption("large-image-url")]
    [EmbedOptionAccepts(VariableType.ImageUrl)]
    LargeImageUrl = 24,
    [EmbedOption("embed-color-hex")]
    [EmbedOptionAccepts(VariableType.HexColor)]
    ColorHex = 25,
    AddField = 30,
    [EmbedOption("footer")]
    [EmbedOptionAccepts(VariableType.Text)]
    Footer = 40,
    [EmbedOption("footer-icon-url")]
    [EmbedOptionAccepts(VariableType.ImageUrl)]
    FooterIconUrl = 41,
    [EmbedOption("footer-timestamp")]
    [EmbedOptionAccepts(VariableType.Timestamp)]
    FooterTimestamp = 42,
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
$$author:{{user.display-name}}{{user.user-type-emoji}} is playing a banger
$$author-image-url:{{user.discord-image-url}}
$$thumbnail-image-url:{{album.cover-url}}
$$embed-color-hex:#A020F0
$$description:## [{{track.name}}]({{track.url}})
**{{track.artist}}** •  *{{track.album}}*
{{""### <:Whiskeydogearnest:1097591075822129292> <:dreamleft:1289310421932576821> I think this artist is from "" + artist.country + ""... <:dreamright:1289310420170965074>""}}
$$footer:{{lastfm.total-scrobbles-result + "" total scrobbles""}} {{"" - "" + artist.genres}}",
            Name = "Example - Dog",
            ShareCode = "ABCD",
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        },
        new Template
        {
            Id = 2,
            Type = TemplateType.Fm,
            Content = @"$$fm-template
$$author:Now playing - {{user.display-name}} {{user.user-type-emoji}}
$$author-image-url:{{user.discord-image-url}}
$$thumbnail-image-url:{{album.cover-url}}
$$embed-color-hex:#A020F0
$$description:-# *Current:*
**[{{track.name}}]({{track.url}})**
**{{track.artist}}** •  *{{track.album}}*

-# *Previous:*
**[{{previous-track.name}}]({{previous-track.url}})**
**{{previous-track.artist}}** • *{{previous-track.album}}*
$$footer:{{lastfm.total-scrobbles}} total scrobbles {{""- "" + artist.genres}}",
            Name = "Example - Embed Full",
            ShareCode = "EFGH",
            Created = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        },
    };
}

public static class TemplateOptions
{
    public static readonly List<TemplateOption> Options = new()
    {
        new ComplexTemplateOption
        {
            Variable = "lastfm.user-name",
            Description = "Last.fm username",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.UserSettings.UserNameLastFm))
        },
        new ComplexTemplateOption
        {
            Variable = "lastfm.join-date",
            Description = "Last.fm account creation date",
            VariableType = VariableType.Text,
            ExecutionLogic = context =>
                Task.FromResult(new VariableResult(context.UserSettings.RegisteredLastFm?.ToString("MMMM d yyyy")))
        },
        new ComplexTemplateOption
        {
            Variable = "lastfm.url",
            Description = "Last.fm account url",
            VariableType = VariableType.Text,
            ExecutionLogic = context =>
                Task.FromResult(new VariableResult(LastfmUrlExtensions.GetUserUrl(context.UserSettings.UserNameLastFm)))
        },
        new ComplexTemplateOption
        {
            Variable = "user.display-name",
            Description = "User's display name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.UserSettings.DisplayName))
        },
        new ComplexTemplateOption
        {
            Variable = "user.user-type",
            Description = "User's usertype in the bot",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(Enum.GetName(context.UserSettings.UserType)))
        },
        new ComplexTemplateOption
        {
            Variable = "user.user-type-emoji",
            Description = "User's usertype emoji",
            VariableType = VariableType.Text,
            ExecutionLogic = context =>
                Task.FromResult(new VariableResult(context.UserSettings.UserType.UserTypeToIcon()))
        },
        new ComplexTemplateOption
        {
            Variable = "author.discord-image-url",
            Description = "Authors Discord avatar url",
            VariableType = VariableType.ImageUrl,
            ExecutionLogic = context =>
                Task.FromResult(new VariableResult(context.DiscordContextUser.GetAvatarUrl()?.ToString()))
        },
        new ComplexTemplateOption
        {
            Variable = "author.display-name",
            Description = "Authors Discord display name",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var name = await UserService.GetNameAsync(context.DiscordContextGuild, context.DiscordContextUser);
                return new VariableResult(name);
            }
        },
        new ComplexTemplateOption
        {
            Variable = "author.user-name",
            Description = "Authors Discord username",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.DiscordContextUser.Username))
        },
        new ComplexTemplateOption
        {
            Variable = "author.global-name",
            Description = "Authors Discord global name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.DiscordContextUser.GlobalName))
        },
        new ComplexTemplateOption
        {
            Variable = "server.name",
            Description = "Server name",
            VariableType = VariableType.Text,
            ExecutionLogic = context =>
                Task.FromResult(context.DiscordContextGuild != null
                    ? new VariableResult(context.DiscordContextGuild.Name)
                    : null)
        },
        new ComplexTemplateOption
        {
            Variable = "server.icon-image-url",
            Description = "Server icon image url",
            VariableType = VariableType.ImageUrl,
            ExecutionLogic = context =>
                Task.FromResult(context.DiscordContextGuild != null
                    ? new VariableResult(context.DiscordContextGuild.GetIconUrl()?.ToString())
                    : null)
        },
        new ComplexTemplateOption
        {
            Variable = "server.banner-image-url",
            Description = "Server banner image url",
            VariableType = VariableType.ImageUrl,
            ExecutionLogic = context =>
                Task.FromResult(context.DiscordContextGuild != null
                    ? new VariableResult(context.DiscordContextGuild.GetBannerUrl()?.ToString())
                    : null)
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.Loved,
            Variable = "track.loved",
            Description = "Indicator if track is loved on Last.fm",
            VariableType = VariableType.Text,
            FooterOrder = 10,
            ExecutionLogic = context =>
                Task.FromResult(context.CurrentTrack.Loved ? new VariableResult("❤️ Loved track", "❤️") : null)
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistPlays,
            Variable = "artist.plays",
            Description = "Amount of plays user has on artist",
            VariableType = VariableType.Text,
            FooterOrder = 20,
            SqlQuery = "SELECT ua.playcount FROM user_artists AS ua WHERE ua.user_id = @userId AND " +
                       "UPPER(ua.name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new VariableResult($"{playcount.Format(context.NumberFormat)} artist scrobbles",
                    playcount.Format(context.NumberFormat).ToString());
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ArtistPlaysThisWeek,
            Variable = "artist.plays-week",
            Description = "Playcount on artist in last 7 days",
            VariableType = VariableType.Text,
            FooterOrder = 60,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-7);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} artist plays this week",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new ComplexTemplateOption
        {
            Variable = "artist.plays-month",
            Description = "Playcount on artist in last 30 days",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-30);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} artist plays this month",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.AlbumPlays,
            Variable = "album.plays",
            Description = "Amount of plays user has on album",
            VariableType = VariableType.Text,
            FooterOrder = 30,
            SqlQuery = "SELECT ua.playcount FROM user_albums AS ua WHERE ua.user_id = @userId AND " +
                       "UPPER(ua.name) = UPPER(CAST(@albumName AS CITEXT)) AND UPPER(ua.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new VariableResult($"{playcount.Format(context.NumberFormat)} album scrobbles",
                    playcount.Format(context.NumberFormat).ToString());
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "albumName", context.CurrentTrack.AlbumName ?? "" },
            }
        },
        new ComplexTemplateOption
        {
            Variable = "album.plays-week",
            Description = "Playcount on album in last 7 days",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-7);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.AlbumName != null &&
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                    a.AlbumName.Equals(context.CurrentTrack.AlbumName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} album plays this week",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new ComplexTemplateOption
        {
            Variable = "album.plays-month",
            Description = "Playcount on album in last 30 days",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-30);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.AlbumName != null &&
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                    a.AlbumName.Equals(context.CurrentTrack.AlbumName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} album plays this month",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.TrackPlays,
            Variable = "track.plays",
            Description = "Amount of plays user has on track",
            VariableType = VariableType.Text,
            FooterOrder = 40,
            SqlQuery = "SELECT ut.playcount FROM user_tracks AS ut WHERE ut.user_id = @userId AND " +
                       "UPPER(ut.name) = UPPER(CAST(@trackName AS CITEXT)) AND UPPER(ut.artist_name) = UPPER(CAST(@artistName AS CITEXT))",
            ResultProcessor = async (context, reader) =>
            {
                var playcount = await reader.IsDBNullAsync(0) ? 0 : await reader.GetFieldValueAsync<int>(0);
                return new VariableResult($"{playcount.Format(context.NumberFormat)} track scrobbles",
                    playcount.Format(context.NumberFormat).ToString());
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
            Variable = "track.plays-week",
            Description = "Playcount on track in last 7 days",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-7);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                    a.AlbumName.Equals(context.CurrentTrack.TrackName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} track plays this week",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new ComplexTemplateOption
        {
            Variable = "track.plays-month",
            Description = "Playcount on track in last 30 days",
            VariableType = VariableType.Text,
            ExecutionLogic = async context =>
            {
                var start = DateTime.UtcNow.AddDays(-30);
                var plays = await PlayRepository.GetUserPlaysWithinTimeRange(context.UserSettings.UserId,
                    context.Connection, start);
                var count = plays.Count(a =>
                    a.ArtistName.Equals(context.CurrentTrack.ArtistName, StringComparison.OrdinalIgnoreCase) &&
                    a.AlbumName.Equals(context.CurrentTrack.TrackName, StringComparison.OrdinalIgnoreCase));
                return new VariableResult($"{count.Format(context.NumberFormat)} track plays this month",
                    count.Format(context.NumberFormat).ToString());
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.TotalScrobbles,
            Variable = "lastfm.total-scrobbles",
            Description = "Last.fm total scrobble count",
            VariableType = VariableType.Text,
            FooterOrder = 50,
            ExecutionLogic = context =>
                Task.FromResult(new VariableResult(
                    $"{context.TotalScrobbles.Format(context.NumberFormat)} total scrobbles",
                    context.TotalScrobbles.Format(context.NumberFormat).ToString()))
        },
        new ComplexTemplateOption
        {
            Variable = "album.url",
            Description = "Last.fm album link",
            VariableType = VariableType.ResourceUrl,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.AlbumUrl))
        },
        new ComplexTemplateOption
        {
            Variable = "album.cover-url",
            Description = "Last.fm album cover image link",
            VariableType = VariableType.ImageUrl,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.AlbumCoverUrl))
        },
        new ComplexTemplateOption
        {
            Variable = "artist.url",
            Description = "Last.fm artist link",
            VariableType = VariableType.ResourceUrl,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.ArtistUrl))
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.ArtistCountry,
            Variable = "artist.country",
            Description = "Artist country (from Musicbrainz)",
            VariableType = VariableType.Text,
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
                            return new VariableResult(artistCountry.Name);
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
            Description = "Artist birthday (from Musicbrainz)",
            VariableType = VariableType.Text,
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
                                ? new VariableResult($"🎂 today! ({age})")
                                : new VariableResult("🎂 today!");
                        }

                        if (startDate.Month == today.AddDays(1).Month && startDate.Day == today.AddDays(1).Day)
                        {
                            return !endDate.HasValue
                                ? new VariableResult($"🎂 tomorrow (becomes {age + 1})")
                                : new VariableResult("🎂 tomorrow");
                        }

                        return !endDate.HasValue
                            ? new VariableResult($"🎂 {startDate:MMMM d} (currently {age})")
                            : new VariableResult($"🎂 {startDate:MMMM d}");
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
            Description = "Artist genres (from Spotify)",
            VariableType = VariableType.Text,
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

                return new VariableResult(GenreService.GenresToString(genres));
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "artistName", context.CurrentTrack.ArtistName }
            }
        },
        new ComplexTemplateOption
        {
            Variable = "track.name",
            Description = "Track name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.TrackName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.album",
            Description = "Album name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.AlbumName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.artist",
            Description = "Artist name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.ArtistName))
        },
        new ComplexTemplateOption
        {
            Variable = "track.url",
            Description = "Last.fm track link",
            VariableType = VariableType.ResourceUrl,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.CurrentTrack.TrackUrl))
        },
        new ComplexTemplateOption
        {
            Variable = "previous-track.name",
            Description = "Previous track name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.PreviousTrack.TrackName))
        },
        new ComplexTemplateOption
        {
            Variable = "previous-track.album",
            Description = "Previous album name",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.PreviousTrack.AlbumName))
        },
        new ComplexTemplateOption
        {
            Variable = "previous-track.artist",
            Description = "Previous track artist",
            VariableType = VariableType.Text,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.PreviousTrack.ArtistName))
        },
        new ComplexTemplateOption
        {
            Variable = "previous-track.url",
            Description = "Previous Last.fm track link",
            VariableType = VariableType.ResourceUrl,
            ExecutionLogic = context => Task.FromResult(new VariableResult(context.PreviousTrack.TrackUrl))
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.TrackBpm,
            Variable = "track.bpm",
            Description = "Track beats per minute",
            VariableType = VariableType.Text,
            FooterOrder = 300,
            ExecutionLogic = context =>
            {
                if (context.DbTrack?.Tempo != null)
                {
                    return Task.FromResult(new VariableResult($"bpm {context.DbTrack?.Tempo:0.0}", $"{context.DbTrack?.Tempo:0.0}"));
                }

                return Task.FromResult<VariableResult>(null);
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.TrackDuration,
            Variable = "track.duration",
            Description = "Track duration",
            VariableType = VariableType.Text,
            FooterOrder = 310,
            ExecutionLogic = context =>
            {
                if (context.DbTrack?.DurationMs != null)
                {
                    var trackLength = TimeSpan.FromMilliseconds(context.DbTrack.DurationMs.Value);
                    var formattedTrackLength =
                        $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

                    var emoji = trackLength.Minutes switch
                    {
                        0 => "🕛", 1 => "🕐", 2 => "🕑", 3 => "🕒", 4 => "🕓", 5 => "🕔",
                        6 => "🕕", 7 => "🕖", 8 => "🕗", 9 => "🕘", 10 => "🕙", 11 => "🕚",
                        12 => "🕛", _ => "🕒"
                    };

                    return Task.FromResult(new VariableResult($"{emoji} {formattedTrackLength}", formattedTrackLength));
                }

                return Task.FromResult<VariableResult>(null);
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.DiscogsCollection,
            Variable = "discogs.collection-item",
            Description = "Discogs collection item",
            VariableType = VariableType.Text,
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

                var albumName = context.CurrentTrack.AlbumName;
                var artistName = context.CurrentTrack.ArtistName;

                var discogsAlbum = discogsUser.DiscogsReleases
                    .Where(w =>
                        (w.Release.Title.StartsWith(albumName, StringComparison.OrdinalIgnoreCase) ||
                         albumName.StartsWith(w.Release.Title.Split(" (")[0], StringComparison.OrdinalIgnoreCase)) &&
                        (w.Release.Artist.StartsWith(artistName, StringComparison.OrdinalIgnoreCase) ||
                         artistName.StartsWith(w.Release.Artist.Split(" (")[0], StringComparison.OrdinalIgnoreCase)))
                    .MaxBy(o => o.DateAdded);

                return discogsAlbum != null
                    ? new VariableResult(StringService.UserDiscogsReleaseToSimpleString(discogsAlbum))
                    : null;
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.CrownHolder,
            Variable = "crown.current-holder",
            Description = "Current artist crown holder",
            VariableType = VariableType.Text,
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
                    return new VariableResult(
                        $"👑 {StringExtensions.Sanitize(userName)} ({currentPlaycount.Format(context.NumberFormat)} plays)",
                        StringExtensions.Sanitize(userName));
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
            Description = "WhoKnows artist ranking",
            VariableType = VariableType.Text,
            FooterOrder = 600,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName);
                artistListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(artistListeners, context.GuildUsers, context.Guild, 0)
                    .filteredUsers;

                if (artistListeners.Any())
                {
                    var requestedUser =
                        artistListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = artistListeners.IndexOf(requestedUser);
                        return new VariableResult($"WhoKnows #{index + 1}", (index + 1).ToString());
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerArtistListeners,
            Variable = "server.artist-listeners",
            Description = "Amount of artist listeners in server",
            VariableType = VariableType.Text,
            FooterOrder = 610,
            ExecutionLogic = async context =>
            {
                if (context.Guild == null)
                {
                    return null;
                }

                var artistListeners = await context.WhoKnowsArtistService.GetIndexedUsersForArtist(null,
                    context.GuildUsers, context.Guild.GuildId, context.CurrentTrack.ArtistName);
                artistListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(artistListeners, context.GuildUsers, context.Guild, 0)
                    .filteredUsers;

                return artistListeners.Any()
                    ? new VariableResult($"{artistListeners.Count.Format(context.NumberFormat)} listeners",
                        artistListeners.Count.Format(context.NumberFormat).ToString())
                    : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerAlbumRank,
            Variable = "server.album-whoknows-rank",
            Description = "WhoKnows album ranking",
            VariableType = VariableType.Text,
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
                albumListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(albumListeners, context.GuildUsers, context.Guild, 0).filteredUsers;

                if (albumListeners.Any())
                {
                    var requestedUser = albumListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = albumListeners.IndexOf(requestedUser);
                        return new VariableResult($"WhoKnows album #{index + 1}", (index + 1).ToString());
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerAlbumListeners,
            Variable = "server.album-listeners",
            Description = "Amount of album listeners in server",
            VariableType = VariableType.Text,
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
                albumListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(albumListeners, context.GuildUsers, context.Guild, 0).filteredUsers;

                return albumListeners.Any()
                    ? new VariableResult($"{albumListeners.Count.Format(context.NumberFormat)} album listeners",
                        albumListeners.Count.Format(context.NumberFormat).ToString())
                    : null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerTrackRank,
            Variable = "server.track-whoknows-rank",
            Description = "WhoKnows track ranking",
            VariableType = VariableType.Text,
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
                trackListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(trackListeners, context.GuildUsers, context.Guild, 0).filteredUsers;

                if (trackListeners.Any())
                {
                    var requestedUser = trackListeners.FirstOrDefault(f => f.UserId == context.UserSettings.UserId);
                    if (requestedUser != null)
                    {
                        var index = trackListeners.IndexOf(requestedUser);
                        return new VariableResult($"WhoKnows track #{index + 1}", (index + 1).ToString());
                    }
                }

                return null;
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.ServerTrackListeners,
            Variable = "server.track-listeners",
            Description = "Amount of track listeners in server",
            VariableType = VariableType.Text,
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
                trackListeners = WhoKnowsService
                    .FilterWhoKnowsObjects(trackListeners, context.GuildUsers, context.Guild, 0).filteredUsers;

                return trackListeners.Any()
                    ? new VariableResult($"{trackListeners.Count.Format(context.NumberFormat)} track listeners",
                        trackListeners.Count.Format(context.NumberFormat).ToString())
                    : null;
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.GlobalArtistRank,
            Variable = "global.artist-whoknows-rank",
            Description = "Global WhoKnows artist ranking",
            VariableType = VariableType.Text,
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
                    return new VariableResult($"GlobalWhoKnows #{rank}", rank.ToString());
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
            Description = "Global WhoKnows album ranking",
            VariableType = VariableType.Text,
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
                    return new VariableResult($"GlobalWhoKnows album #{rank}", rank.ToString());
                }

                return null;
            },
            ParametersFactory = context => new Dictionary<string, object>
            {
                { "userId", context.UserSettings.UserId },
                { "artistName", context.CurrentTrack.ArtistName },
                { "albumName", context.CurrentTrack.AlbumName ?? "" }
            }
        },
        new SqlTemplateOption
        {
            FooterOption = FmFooterOption.GlobalTrackRank,
            Variable = "global.track-whoknows-rank",
            Description = "Global WhoKnows track ranking",
            VariableType = VariableType.Text,
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
                    return new VariableResult($"GlobalWhoKnows track #{rank}", rank.ToString());
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
            Description = "Artist discovery date",
            VariableType = VariableType.Text,
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
                if (firstPlay == null) return null;

                var timestamp = ((DateTimeOffset)DateTime.SpecifyKind(firstPlay.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                return new VariableResult($"Artist discovered <t:{timestamp}:D>", $"<t:{timestamp}:D>");
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.FirstAlbumListen,
            Variable = "discovery.album-discovered",
            Description = "Album discovery date",
            VariableType = VariableType.Text,
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
                if (firstPlay == null) return null;

                var timestamp = ((DateTimeOffset)DateTime.SpecifyKind(firstPlay.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                return new VariableResult($"Album discovered <t:{timestamp}:D>", $"<t:{timestamp}:D>");
            }
        },
        new ComplexTemplateOption
        {
            FooterOption = FmFooterOption.FirstTrackListen,
            Variable = "discovery.track-discovered",
            Description = "Track discovery date",
            VariableType = VariableType.Text,
            FooterOrder = 820,
            ExecutionLogic = async context =>
            {
                if (!SupporterService.IsSupporter(context.UserSettings.UserType))
                {
                    return null;
                }

                var firstPlay = await context.PlayService.GetTrackFirstPlayDate(context.UserSettings.UserId,
                    context.CurrentTrack.ArtistName, context.CurrentTrack.TrackName);
                if (firstPlay == null) return null;

                var timestamp = ((DateTimeOffset)DateTime.SpecifyKind(firstPlay.Value, DateTimeKind.Utc)).ToUnixTimeSeconds();
                return new VariableResult($"Track discovered <t:{timestamp}:D>", $"<t:{timestamp}:D>");
            }
        }
    };
}
