using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Attributes;
using FMBot.Domain.Enums;
using FMBot.Domain.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class UserService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly BotSettings _botSettings;
    private readonly CountryService _countryService;
    private readonly PlayService _playService;

    public UserService(IMemoryCache cache,
        IDbContextFactory<FMBotDbContext> contextFactory,
        IDataSourceFactory dataSourceFactory,
        IOptions<BotSettings> botSettings,
        CountryService countryService,
        PlayService playService)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
        this._dataSourceFactory = dataSourceFactory;
        this._countryService = countryService;
        this._playService = playService;
        this._botSettings = botSettings.Value;
    }

    public async Task<User> GetUserSettingsAsync(IUser discordUser)
    {
        return await GetUserAsync(discordUser.Id);
    }

    public async Task<User> GetUserAsync(ulong discordUserId)
    {
        var discordUserIdCacheKey = UserDiscordIdCacheKey(discordUserId);

        if (this._cache.TryGetValue(discordUserIdCacheKey, out User user))
        {
            return user;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user != null)
        {
            var lastFmCacheKey = UserLastFmCacheKey(user.UserNameLastFM);

            this._cache.Set(lastFmCacheKey, user, TimeSpan.FromSeconds(5));
            this._cache.Set(discordUserIdCacheKey, user, TimeSpan.FromSeconds(5));
        }

        return user;
    }

    public void RemoveUserFromCache(User user)
    {
        this._cache.Remove(UserDiscordIdCacheKey(user.DiscordUserId));
        this._cache.Remove(UserLastFmCacheKey(user.UserNameLastFM));
    }

    public static string UserDiscordIdCacheKey(ulong discordUserId)
    {
        return $"user-{discordUserId}";
    }

    public static string UserLastFmCacheKey(string userNameLastFm)
    {
        return $"user-{userNameLastFm.ToLower()}";
    }

    public async Task<User> GetUserForIdAsync(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId);
    }

    public async Task<User> GetUserWithDiscogs(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.DiscogsReleases)
            .ThenInclude(i => i.Release)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public async Task<bool> UserRegisteredAsync(IUser discordUser)
    {
        var user = await GetUserAsync(discordUser.Id);

        return user != null;
    }

    public async Task<bool> UserBlockedAsync(ulong discordUserId)
    {
        var user = await GetUserAsync(discordUserId);

        return user?.Blocked == true;
    }

    public async Task<bool> UserHasSessionAsync(IUser discordUser)
    {
        var user = await GetUserSettingsAsync(discordUser);

        return !string.IsNullOrEmpty(user?.SessionKeyLastFm);
    }

    public async Task<Dictionary<int, User>> GetMultipleUsers(HashSet<int> userIds)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .Where(w => userIds.Contains(w.UserId))
            .ToDictionaryAsync(d => d.UserId, d => d);
    }

    public async Task UpdateUserLastUsedAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);

        if (user != null)
        {
            user.LastUsed = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            db.Update(user);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Something went wrong while attempting to update user {userId} last used", user.UserId);
            }
        }
    }

    public async Task AddUserTextCommandInteraction(ShardedCommandContext context, string commandName)
    {
        var user = await GetUserSettingsAsync(context.User);

        await Task.Delay(12000);

        try
        {
            if (user != null)
            {
                var commandResponse = CommandResponse.Ok;
                if (PublicProperties.UsedCommandsResponses.TryGetValue(context.Message.Id, out var fetchedResponse))
                {
                    commandResponse = fetchedResponse;
                }

                string errorReference = null;
                if (PublicProperties.UsedCommandsErrorReferences.TryGetValue(context.Message.Id, out var fetchedErrorId))
                {
                    errorReference = fetchedErrorId;
                }

                string artist = null;
                if (PublicProperties.UsedCommandsArtists.TryGetValue(context.Message.Id, out var fetchedArtist))
                {
                    artist = fetchedArtist;
                }

                string album = null;
                if (PublicProperties.UsedCommandsAlbums.TryGetValue(context.Message.Id, out var fetchedAlbum))
                {
                    album = fetchedAlbum;
                }

                string track = null;
                if (PublicProperties.UsedCommandsTracks.TryGetValue(context.Message.Id, out var fetchedTrack))
                {
                    track = fetchedTrack;
                }

                var interaction = new UserInteraction
                {
                    Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    CommandContent = context.Message.Content,
                    CommandName = commandName,
                    UserId = user.UserId,
                    DiscordGuildId = context.Guild?.Id,
                    DiscordChannelId = context.Channel?.Id,
                    DiscordId = context.Message.Id,
                    Response = commandResponse,
                    Type = UserInteractionType.TextCommand,
                    ErrorReferenceId = errorReference,
                    Artist = artist,
                    Album = album,
                    Track = track
                };

                await using var db = await this._contextFactory.CreateDbContextAsync();
                await db.UserInteractions.AddAsync(interaction);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "AddUserTextCommandInteraction: Error while adding user interaction");
        }
    }

    public async Task AddUserSlashCommandInteraction(ShardedInteractionContext context, string commandName)
    {
        var user = await GetUserSettingsAsync(context.User);

        await Task.Delay(12000);

        try
        {
            if (user != null)
            {
                var commandResponse = CommandResponse.Ok;
                if (PublicProperties.UsedCommandsResponses.TryGetValue(context.Interaction.Id, out var fetchedResponse))
                {
                    commandResponse = fetchedResponse;
                }

                string errorReference = null;
                if (PublicProperties.UsedCommandsErrorReferences.TryGetValue(context.Interaction.Id, out var fetchedErrorId))
                {
                    errorReference = fetchedErrorId;
                }

                string artist = null;
                if (PublicProperties.UsedCommandsArtists.TryGetValue(context.Interaction.Id, out var fetchedArtist))
                {
                    artist = fetchedArtist;
                }

                string album = null;
                if (PublicProperties.UsedCommandsAlbums.TryGetValue(context.Interaction.Id, out var fetchedAlbum))
                {
                    album = fetchedAlbum;
                }

                string track = null;
                if (PublicProperties.UsedCommandsTracks.TryGetValue(context.Interaction.Id, out var fetchedTrack))
                {
                    track = fetchedTrack;
                }

                var options = new Dictionary<string, string>();
                if (context.Interaction is SocketSlashCommand command)
                {
                    foreach (var option in command.Data.Options)
                    {
                        options.Add(option.Name, option.Value?.ToString());
                    }
                }

                var interaction = new UserInteraction
                {
                    Timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
                    CommandName = commandName,
                    CommandOptions = options.Any() ? options : null,
                    UserId = user.UserId,
                    DiscordGuildId = context.Guild?.Id,
                    DiscordChannelId = context.Channel?.Id,
                    DiscordId = context.Interaction.Id,
                    Response = commandResponse,
                    Type = UserInteractionType.SlashCommand,
                    ErrorReferenceId = errorReference,
                    Artist = artist,
                    Album = album,
                    Track = track
                };

                await using var db = await this._contextFactory.CreateDbContextAsync();
                await db.UserInteractions.AddAsync(interaction);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "AddUserSlashCommandInteraction: Error while adding user interaction");
        }
    }

    public async Task<int> GetCommandExecutedAmount(int userId, string command, DateTime filterDateTime)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.UserInteractions
            .CountAsync(c => c.UserId == userId &&
                             c.Timestamp >= filterDateTime &&
                             c.Response == CommandResponse.Ok &&
                             c.CommandName == command);
    }

    public async Task SetUserReactionsAsync(int userId, string[] reactions)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstAsync(f => f.UserId == userId);

        user.EmoteReactions = reactions;

        db.Entry(user).State = EntityState.Modified;

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);
    }

    public async Task<User> GetUserWithFriendsAsync(IUser discordUser)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .Include(i => i.Friends)
            .ThenInclude(i => i.FriendUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
    }

    public async Task<List<User>> GetAllDiscordUserIds()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<User> GetFullUserAsync(ulong discordUserId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var query = db.Users
            .Include(i => i.UserDiscogs)
            .Include(i => i.Friends)
            .Include(i => i.FriendedByUsers);

        return await query
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUserId);
    }

    public static async Task<string> GetNameAsync(IGuild guild, IUser user)
    {
        if (guild == null)
        {
            return user.GlobalName ?? user.Username;
        }

        var guildUser = await guild.GetUserAsync(user.Id);

        return guildUser?.DisplayName ?? user.GlobalName ?? user.Username;
    }

    public async Task<UserType> GetRankAsync(IUser discordUser)
    {
        var user = await GetUserSettingsAsync(discordUser);

        return user?.UserType ?? UserType.User;
    }

    public async Task<string> GetUserTitleAsync(ICommandContext context)
    {
        var name = await GetNameAsync(context.Guild, context.User);
        var userType = await GetRankAsync(context.User);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    public async Task<string> GetUserTitleAsync(IGuild guild, IUser user)
    {
        var name = await GetNameAsync(guild, user);
        var userType = await GetRankAsync(user);

        var title = name;

        title += $"{userType.UserTypeToIcon()}";

        return title;
    }

    public async Task<StringBuilder> GetFooterAsync(FmFooterOption footerOptions,
        UserSettingsModel userSettings,
        string artistName,
        string albumName,
        string trackName,
        bool loved,
        long totalScrobbles,
        Persistence.Domain.Models.Guild guild = null,
        IDictionary<int, FullGuildUser> guildUsers = null)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var options = new List<string>();
        string genres = null;

        if (footerOptions.HasFlag(FmFooterOption.Loved) && loved)
        {
            options.Add("❤️ Loved track");
        }
        if (footerOptions.HasFlag(FmFooterOption.ArtistPlays))
        {
            var trackPlaycount =
                await ArtistRepository.GetArtistPlayCountForUser(connection, artistName, userSettings.UserId);

            options.Add($"{trackPlaycount} artist scrobbles");
        }
        if (footerOptions.HasFlag(FmFooterOption.AlbumPlays) && albumName != null)
        {
            var albumPlaycount =
                await AlbumRepository.GetAlbumPlayCountForUser(connection, artistName, albumName, userSettings.UserId);

            options.Add($"{albumPlaycount} album scrobbles");
        }
        if (footerOptions.HasFlag(FmFooterOption.TrackPlays))
        {
            var trackPlaycount =
                await TrackRepository.GetTrackPlayCountForUser(connection, artistName, trackName, userSettings.UserId);

            options.Add($"{trackPlaycount} track scrobbles");
        }
        if (footerOptions.HasFlag(FmFooterOption.TotalScrobbles))
        {
            options.Add($"{totalScrobbles} total scrobbles");
        }
        if (footerOptions.HasFlag(FmFooterOption.ArtistPlaysThisWeek))
        {
            var start = DateTime.UtcNow.AddDays(-7);
            var plays = await PlayRepository.GetUserPlaysWithinTimeRange(userSettings.UserId, connection, start);

            var count = plays.Count(a => a.ArtistName.ToLower() == artistName.ToLower());

            options.Add($"{count} artist plays this week");
        }

        if (footerOptions.HasFlag(FmFooterOption.ArtistCountry) || footerOptions.HasFlag(FmFooterOption.ArtistBirthday) || footerOptions.HasFlag(FmFooterOption.ArtistGenres))
        {
            var artist = await ArtistRepository.GetArtistForName(artistName, connection, footerOptions.HasFlag(FmFooterOption.ArtistGenres));

            if (footerOptions.HasFlag(FmFooterOption.ArtistCountry) && !string.IsNullOrWhiteSpace(artist?.CountryCode))
            {
                var artistCountry = this._countryService.GetValidCountry(artist.CountryCode);

                if (artistCountry != null)
                {
                    options.Add($"{artistCountry.Name}");
                }
            }

            if (footerOptions.HasFlag(FmFooterOption.ArtistBirthday) &&
                artist?.StartDate != null &&
                (artist.StartDate.Value.Month != 1 || artist.StartDate.Value.Day != 1))
            {
                var age = GetAgeInYears(artist.StartDate.Value);

                if (artist.StartDate.Value.Month == DateTime.Today.Month &&
                    artist.StartDate.Value.Day == DateTime.Today.Day)
                {
                    options.Add(!artist.EndDate.HasValue ? $"🎂 today! ({age})" : "🎂 today!");
                }
                else if (artist.StartDate.Value.Month == DateTime.Today.AddDays(-1).Month &&
                         artist.StartDate.Value.Day == DateTime.Today.AddDays(-1).Day)
                {
                    options.Add(!artist.EndDate.HasValue ? $"🎂 tomorrow (becomes {age + 1})" : "🎂 tomorrow");
                }
                else
                {
                    options.Add(!artist.EndDate.HasValue
                        ? $"🎂 {artist.StartDate.Value.ToString("MMMM d")} (currently {age})"
                        : $"🎂 {artist.StartDate.Value.ToString("MMMM d")}");
                }
            }

            if (footerOptions.HasFlag(FmFooterOption.ArtistGenres) && artist?.ArtistGenres != null && artist.ArtistGenres.Any())
            {
                genres = GenreService.GenresToString(artist.ArtistGenres.Take(6).ToList());
            }
        }

        if (footerOptions.HasFlag(FmFooterOption.TrackBpm) || footerOptions.HasFlag(FmFooterOption.TrackDuration))
        {
            var track = await TrackRepository.GetTrackForName(artistName, trackName, connection);

            if (footerOptions.HasFlag(FmFooterOption.TrackBpm) && track?.Tempo != null)
            {
                var bpm = $"{track.Tempo.Value:0.0}";
                options.Add($"bpm {bpm}");
            }

            if (footerOptions.HasFlag(FmFooterOption.TrackDuration) && track?.DurationMs != null)
            {
                var trackLength = TimeSpan.FromMilliseconds(track.DurationMs.GetValueOrDefault());
                var formattedTrackLength =
                    $"{(trackLength.Hours == 0 ? "" : $"{trackLength.Hours}:")}{trackLength.Minutes}:{trackLength.Seconds:D2}";

                var emoji = trackLength.Minutes switch
                {
                    0 => "🕛",
                    1 => "🕐",
                    2 => "🕑",
                    3 => "🕒",
                    4 => "🕓",
                    5 => "🕔",
                    6 => "🕕",
                    7 => "🕖",
                    8 => "🕗",
                    9 => "🕘",
                    10 => "🕙",
                    11 => "🕚",
                    12 => "🕛",
                    _ => "🕒"
                };

                options.Add($"{emoji} {formattedTrackLength}");
            }
        }

        if (footerOptions.HasFlag(FmFooterOption.DiscogsCollection) && albumName != null)
        {
            var discogsUser = await this.GetUserWithDiscogs(userSettings.DiscordUserId);

            if (discogsUser.UserDiscogs != null && discogsUser.DiscogsReleases.Any())
            {
                var albumCollection = discogsUser.DiscogsReleases.Where(w =>
                    (w.Release.Title.StartsWith(albumName, StringComparison.OrdinalIgnoreCase) ||
                     albumName.StartsWith(w.Release.Title, StringComparison.OrdinalIgnoreCase))
                    &&
                    (w.Release.Artist.StartsWith(artistName, StringComparison.OrdinalIgnoreCase) ||
                     artistName.StartsWith(w.Release.Artist, StringComparison.OrdinalIgnoreCase))).ToList();

                var discogsAlbum = albumCollection.MaxBy(o => o.DateAdded);

                if (discogsAlbum != null)
                {
                    options.Add(StringService.UserDiscogsReleaseToSimpleString(discogsAlbum));
                }
            }
        }

        if (guild != null)
        {
            if (footerOptions.HasFlag(FmFooterOption.CrownHolder) && guild.CrownsDisabled != true)
            {
                var currentCrownHolder = await CrownService.GetCurrentCrownHolderWithName(connection, guild.GuildId, artistName);

                if (currentCrownHolder != null)
                {
                    options.Add($"👑 {Format.Sanitize(currentCrownHolder.UserName)} ({currentCrownHolder.CurrentPlaycount} plays)");
                }
            }
            if (footerOptions.HasFlag(FmFooterOption.ServerArtistRank) || footerOptions.HasFlag(FmFooterOption.ServerArtistListeners))
            {
                var artistListeners =
                    await WhoKnowsArtistService.GetBasicUsersForArtist(connection, guild.GuildId, artistName);

                if (artistListeners.Any())
                {
                    if (footerOptions.HasFlag(FmFooterOption.ServerArtistRank))
                    {
                        var requestedUser = artistListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                        if (requestedUser != null)
                        {
                            var index = artistListeners.IndexOf(requestedUser);
                            options.Add($"WhoKnows #{index + 1}");
                        }
                    }
                    if (footerOptions.HasFlag(FmFooterOption.ServerArtistListeners))
                    {
                        options.Add($"{artistListeners.Count} listeners");
                    }
                }
            }
            if ((footerOptions.HasFlag(FmFooterOption.ServerAlbumRank) || footerOptions.HasFlag(FmFooterOption.ServerAlbumListeners)) && albumName != null)
            {
                var albumListeners =
                    await WhoKnowsAlbumService.GetBasicUsersForAlbum(connection, guild.GuildId, artistName, albumName);

                if (albumListeners.Any())
                {
                    if (footerOptions.HasFlag(FmFooterOption.ServerAlbumRank))
                    {
                        var requestedUser = albumListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                        if (requestedUser != null)
                        {
                            var index = albumListeners.IndexOf(requestedUser);
                            options.Add($"WhoKnows album #{index + 1}");
                        }
                    }
                    if (footerOptions.HasFlag(FmFooterOption.ServerAlbumListeners))
                    {
                        options.Add($"{albumListeners.Count} album listeners");
                    }
                }
            }
            if (footerOptions.HasFlag(FmFooterOption.ServerTrackRank) || footerOptions.HasFlag(FmFooterOption.ServerTrackListeners))
            {
                var trackListeners =
                    await WhoKnowsTrackService.GetBasicUsersFromTrack(connection, guild.GuildId, artistName, trackName);

                if (trackListeners.Any())
                {
                    if (footerOptions.HasFlag(FmFooterOption.ServerTrackRank))
                    {
                        var requestedUser = trackListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                        if (requestedUser != null)
                        {
                            var index = trackListeners.IndexOf(requestedUser);
                            options.Add($"WhoKnows track #{index + 1}");
                        }
                    }
                    if (footerOptions.HasFlag(FmFooterOption.ServerTrackListeners))
                    {
                        options.Add($"{trackListeners.Count} track listeners");
                    }
                }
            }
        }

        if (footerOptions.HasFlag(FmFooterOption.GlobalArtistRank))
        {
            var artistListeners =
                await WhoKnowsArtistService.GetBasicGlobalUsersForArtists(connection, artistName);

            if (artistListeners.Any())
            {
                var requestedUser = artistListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                if (requestedUser != null)
                {
                    var lastListenerWithPlaycount = artistListeners.LastOrDefault(l => l.Playcount == requestedUser.Playcount);

                    var index = artistListeners.IndexOf(lastListenerWithPlaycount);
                    options.Add($"GlobalWhoKnows #{index + 1}");
                }
            }
        }
        if (footerOptions.HasFlag(FmFooterOption.GlobalAlbumRank) && albumName != null)
        {
            var albumListeners =
                await WhoKnowsAlbumService.GetBasicGlobalUsersForAlbum(connection, artistName, albumName);

            if (albumListeners.Any())
            {
                var requestedUser = albumListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                if (requestedUser != null)
                {
                    var lastListenerWithPlaycount = albumListeners.LastOrDefault(l => l.Playcount == requestedUser.Playcount);

                    var index = albumListeners.IndexOf(lastListenerWithPlaycount);
                    options.Add($"GlobalWhoKnows album #{index + 1}");
                }
            }
        }
        if (footerOptions.HasFlag(FmFooterOption.GlobalTrackRank))
        {
            var trackListeners =
                await WhoKnowsTrackService.GetBasicGlobalUsersForTrack(connection, artistName, trackName);

            if (trackListeners.Any())
            {
                var requestedUser = trackListeners.FirstOrDefault(f => f.UserId == userSettings.UserId);

                if (requestedUser != null)
                {
                    var lastListenerWithPlaycount = trackListeners.LastOrDefault(l => l.Playcount == requestedUser.Playcount);

                    var index = trackListeners.IndexOf(lastListenerWithPlaycount);
                    options.Add($"GlobalWhoKnows track #{index + 1}");
                }
            }
        }
        if (userSettings.UserType != UserType.User)
        {
            if (footerOptions.HasFlag(FmFooterOption.FirstArtistListen))
            {
                var firstPlay =
                    await this._playService.GetArtistFirstPlayDate(userSettings.UserId, artistName);
                if (firstPlay != null)
                {
                    options.Add($"Artist discovered {firstPlay.Value.ToString("MMMM d yyyy")}");
                }
            }
            if (footerOptions.HasFlag(FmFooterOption.FirstAlbumListen) && albumName != null)
            {
                var firstPlay =
                    await this._playService.GetAlbumFirstPlayDate(userSettings.UserId, artistName, albumName);
                if (firstPlay != null)
                {
                    options.Add($"Album discovered {firstPlay.Value.ToString("MMMM d yyyy")}");
                }
            }
            if (footerOptions.HasFlag(FmFooterOption.FirstTrackListen))
            {
                var firstPlay =
                    await this._playService.GetTrackFirstPlayDate(userSettings.UserId, artistName, trackName);
                if (firstPlay != null)
                {
                    options.Add($"Track discovered {firstPlay.Value.ToString("MMMM d yyyy")}");
                }
            }
        }

        return CreateFooter(options, genres);
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

    private static StringBuilder CreateFooter(IReadOnlyList<string> options, string genres)
    {
        var footer = new StringBuilder();

        var genresAdded = false;
        if (genres != null && genres.Length <= 48 && options.Count > 2)
        {
            footer.AppendLine(genres);
            genresAdded = true;
        }

        var lineLength = 0;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            var nextOption = options.ElementAtOrDefault(index + 1);

            if ((lineLength > 38 || (lineLength > 28 && option.Length > 18)) && nextOption != null)
            {
                footer.AppendLine();
                lineLength = option.Length;
                footer.Append(option);
            }
            else
            {
                if (lineLength != 0)
                {
                    footer.Append(" · ");
                }

                footer.Append(option);
                lineLength += option.Length;
            }

            if (nextOption == null)
            {
                footer.AppendLine();
            }
        }

        if (!genresAdded && genres != null)
        {
            footer.AppendLine(genres);
        }

        return footer;
    }

    public static (bool promo, string description) GetIndexCompletedUserStats(User user, IndexedUserStats stats)
    {
        var description = new StringBuilder();
        var promo = false;

        if (stats == null)
        {
            description.AppendLine("Full update could not complete, something went wrong. Please try again later.");
            return (false, description.ToString());
        }

        if (stats.UpdateError == true)
        {
            description.AppendLine($"❌ An error occurred while attempting to update `{user.UserNameLastFM}`:");
            if (stats.FailedUpdates.HasFlag(UpdateType.Full))
            {
                description.AppendLine($"- Could not fetch user info from Last.fm");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Artist))
            {
                description.AppendLine($"- Could not fetch top artists");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Albums))
            {
                description.AppendLine($"- Could not fetch top albums");
            }
            if (stats.FailedUpdates.HasFlag(UpdateType.Tracks))
            {
                description.AppendLine($"- Could not fetch top tracks");
            }
        }
        else
        {
            description.AppendLine($"✅ `{user.UserNameLastFM}` has been fully updated.");
            description.AppendLine();
            description.AppendLine("Cached the following playcounts:");

            if (user.UserType == UserType.User)
            {
                if (stats.PlayCount.HasValue)
                {
                    description.AppendLine($"- Last **{stats.PlayCount}** plays");
                }
                if (stats.ArtistCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.ArtistCount}** artists");
                }
                if (stats.AlbumCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.AlbumCount}** albums");
                }
                if (stats.TrackCount.HasValue)
                {
                    description.AppendLine($"- Top **{stats.TrackCount}** tracks");
                }
            }
            else
            {
                if (stats.PlayCount.HasValue)
                {
                    description.AppendLine($"- **{stats.PlayCount}** Last.fm plays");
                }
                if (stats.ArtistCount.HasValue)
                {
                    description.AppendLine($"- **{stats.ArtistCount}** top artists");
                }
                if (stats.AlbumCount.HasValue)
                {
                    description.AppendLine($"- **{stats.AlbumCount}** top albums");
                }
                if (stats.TrackCount.HasValue)
                {
                    description.AppendLine($"- **{stats.TrackCount}** top tracks");
                }

                if (stats.ImportCount != null)
                {
                    description.AppendLine();

                    var name = user.DataSource.GetAttribute<OptionAttribute>().Name;
                    description.AppendLine($"Import setting: {name}");
                    description.AppendLine($"Combined with your **{stats.ImportCount}** imported plays you have a total of **{stats.TotalCount}** plays.");
                }
            }

            if (user.UserType == UserType.User &&
                (stats.PlayCount >= 49900 ||
                 stats.TrackCount >= 5900 ||
                 stats.AlbumCount >= 4900 ||
                 stats.ArtistCount >= 3900))
            {
                description.AppendLine();
                description.AppendLine($"Want your full Last.fm history to be stored in the bot? [{Constants.GetSupporterButton}]({Constants.GetSupporterDiscordLink}).");
                promo = true;
            }
        }

        return (promo, description.ToString());
    }

    public async Task SetLastFm(IUser discordUser, User newUserSettings, bool updateSessionKey = false)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users
            .AsQueryable()
            .FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);

        if (user == null)
        {
            var newUser = new User
            {
                DiscordUserId = discordUser.Id,
                UserType = UserType.User,
                UserNameLastFM = newUserSettings.UserNameLastFM,
                FmEmbedType = newUserSettings.FmEmbedType,
                SessionKeyLastFm = newUserSettings.SessionKeyLastFm,
                DataSource = DataSource.LastFm,
                PrivacyLevel = PrivacyLevel.Server,
                FmFooterOptions = FmFooterOption.TotalScrobbles
            };

            await db.Users.AddAsync(newUser);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error in SetLastFM");
                throw;
            }

            var createdUser = await db.Users.FirstOrDefaultAsync(f => f.DiscordUserId == discordUser.Id);
            if (createdUser != null)
            {
                PublicProperties.RegisteredUsers.TryAdd(createdUser.DiscordUserId, createdUser.UserId);
            }
        }
        else
        {
            user.UserNameLastFM = newUserSettings.UserNameLastFM;
            user.FmEmbedType = newUserSettings.FmEmbedType;
            user.Mode = newUserSettings.Mode;
            if (updateSessionKey)
            {
                user.SessionKeyLastFm = newUserSettings.SessionKeyLastFm;
            }

            db.Update(user);

            await db.SaveChangesAsync();

            RemoveUserFromCache(user);
        }
    }

    public async Task<bool> GetAndStoreAuthSession(IUser contextUser, string token)
    {
        Log.Information("LastfmAuth: Login session starting for {user} | {discordUserId}", contextUser.Username, contextUser.Id);

        var loginDelay = 8000;
        for (var i = 0; i < 11; i++)
        {
            await Task.Delay(loginDelay);

            var authSession = await this._dataSourceFactory.GetAuthSession(token);

            if (authSession.Success)
            {
                var userSettings = new User
                {
                    UserNameLastFM = authSession.Content.Session.Name,
                    DataSource = DataSource.LastFm,
                    SessionKeyLastFm = authSession.Content.Session.Key,
                };

                Log.Information("LastfmAuth: User {userName} logged in with auth session (discordUserId: {discordUserId})", authSession.Content.Session.Name, contextUser.Id);
                await SetLastFm(contextUser, userSettings, true);
                return true;
            }

            if (!authSession.Success && i == 10)
            {
                Log.Information("LastfmAuth: Login timed out or auth not successful (discordUserId: {discordUserId})", contextUser.Id);
                return false;
            }
            if (!authSession.Success)
            {
                loginDelay += 3000;
            }
        }

        return false;
    }

    public async Task<PrivacyLevel> SetPrivacyLevel(int userId, PrivacyLevel privacyLevel)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        user.PrivacyLevel = privacyLevel;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user.PrivacyLevel;
    }

    public async Task<string> SetTimeZone(int userId, string timeZone)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        user.TimeZone = timeZone;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user.TimeZone;
    }

    public async Task<User> SetSettings(User userToUpdate, FmEmbedType embedType, FmCountType? countType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.FmEmbedType = embedType;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return user;
    }

    public async Task<User> SetDataSource(User userToUpdate, DataSource dataSource)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.DataSource = dataSource;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return user;
    }

    public async Task<User> SetFooterOptions(User userToUpdate, FmFooterOption fmFooterOption)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.FmFooterOptions = fmFooterOption;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user;
    }

    public async Task<User> SetResponseMode(User userToUpdate, ResponseMode mode)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var user = await db.Users.FirstAsync(f => f.UserId == userToUpdate.UserId);

        user.Mode = mode;

        db.Update(user);

        await db.SaveChangesAsync();

        RemoveUserFromCache(user);

        return user;
    }

    public async Task DeleteUser(int userId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        try
        {
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.UserId == userId);

            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            await using var deleteRelatedTables = new NpgsqlCommand(
                $"DELETE FROM public.user_artists WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_albums WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.user_tracks WHERE user_id = {user.UserId}; " +
                $"DELETE FROM public.friends WHERE user_id = {user.UserId} OR friend_user_id = {user.UserId}; " +
                $"DELETE FROM public.featured_logs WHERE user_id = {user.UserId}; ",
                connection);

            await deleteRelatedTables.ExecuteNonQueryAsync();

            db.Users.Remove(user);

            await db.SaveChangesAsync();

            RemoveUserFromCache(user);

            PublicProperties.RegisteredUsers.TryRemove(user.DiscordUserId, out _);

            Log.Information("Deleted user {userId} - {discordUserId} - {userNameLastFm}", user.UserId, user.DiscordUserId, user.UserNameLastFM);
        }
        catch (Exception e)
        {
            Log.Error(e, "Error while deleting user!");
        }
    }

    public async Task<bool?> ToggleRymAsync(User user)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var userToUpdate = await db.Users.FindAsync(user.UserId);

        if (userToUpdate.RymEnabled != true)
        {
            userToUpdate.RymEnabled = true;
        }
        else
        {
            userToUpdate.RymEnabled = false;
        }

        db.Update(userToUpdate);

        await db.SaveChangesAsync();

        RemoveUserFromCache(userToUpdate);

        return userToUpdate.RymEnabled;
    }

    public async Task ToggleBotScrobblingAsync(int userId, bool? disabled)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var user = await db.Users.FirstAsync(f => f.UserId == userId);

        user.MusicBotTrackingDisabled = disabled;

        db.Update(user);

        RemoveUserFromCache(user);

        await db.SaveChangesAsync();
    }

    public async Task<int> GetTotalUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .CountAsync();
    }

    public async Task<int> GetTotalActiveUserCountAsync(int daysToGoBack)
    {
        var filterDate = DateTime.UtcNow.AddDays(-daysToGoBack);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .CountAsync(c => c.LastUsed != null &&
                             c.LastUsed >= filterDate);
    }

    public async Task<int> GetTotalAuthorizedUserCountAsync()
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        return await db.Users
            .AsQueryable()
            .Where(w => w.SessionKeyLastFm != null)
            .CountAsync();
    }

    public async Task<int> DeleteInactiveUsers()
    {
        var deletedInactiveUsers = 0;

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var inactiveUsers = await db.InactiveUsers
            .AsQueryable()
            .Where(w => w.MissingParametersErrorCount >= 1 && w.Updated > DateTime.UtcNow.AddDays(-3))
            .ToListAsync();

        foreach (var inactiveUser in inactiveUsers)
        {
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.UserId == inactiveUser.UserId &&
                                          (f.LastUsed == null || f.LastUsed < DateTime.UtcNow.AddDays(-30)) &&
                                          string.IsNullOrWhiteSpace(f.SessionKeyLastFm));

            if (user != null)
            {
                if (!await this._dataSourceFactory.LastFmUserExistsAsync(user.UserNameLastFM))
                {
                    await DeleteUser(user.UserId);
                    Log.Information("DeleteInactiveUsers: User {userNameLastFm} | {userId} | {discordUserId} deleted", user.UserNameLastFM, user.UserId, user.DiscordUserId);
                    deletedInactiveUsers++;
                }
                else
                {
                    Log.Information("DeleteInactiveUsers: User {userNameLastFm} exists, so deletion cancelled", user.UserNameLastFM);
                }

                Thread.Sleep(250);
            }
        }

        return deletedInactiveUsers;
    }
}
