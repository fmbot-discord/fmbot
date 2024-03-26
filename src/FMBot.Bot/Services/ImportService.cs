using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AngleSharp.Css.Values;
using CsvHelper;
using CsvHelper.Configuration;
using Discord;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;
using Web.InternalApi;

namespace FMBot.Bot.Services;

public class ImportService
{
    private readonly HttpClient _httpClient;
    private readonly TimeService _timeService;
    private readonly BotSettings _botSettings;
    private readonly TimeEnrichment.TimeEnrichmentClient _timeEnrichment;
    private readonly ArtistEnrichment.ArtistEnrichmentClient _artistEnrichment;


    public ImportService(HttpClient httpClient, TimeService timeService, IOptions<BotSettings> botSettings, TimeEnrichment.TimeEnrichmentClient timeEnrichment, ArtistEnrichment.ArtistEnrichmentClient artistEnrichment)
    {
        this._httpClient = httpClient;
        this._timeService = timeService;
        this._timeEnrichment = timeEnrichment;
        this._artistEnrichment = artistEnrichment;
        this._botSettings = botSettings.Value;
    }

    public async Task<(ImportStatus status, List<AppleMusicCsvImportModel> result)> HandleAppleMusicFiles(User user, IAttachment attachment)
    {
        var appleMusicPlays = new List<AppleMusicCsvImportModel>();

        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
        };

        if (attachment.Filename.EndsWith(".zip"))
        {
            await using var stream = await this._httpClient.GetStreamAsync(attachment.Url);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            if (!zip.Entries.Any(a => a.FullName.EndsWith("Apple Music Play Activity.csv", StringComparison.OrdinalIgnoreCase)))
            {
                var innerZipEntry = zip.Entries.FirstOrDefault(f => f.FullName.EndsWith("Apple_Media_Services.zip", StringComparison.OrdinalIgnoreCase));
                if (innerZipEntry == null)
                {
                    Log.Information("Importing: {userId} / {discordUserId} - HandleAppleMusicFiles - Could not find 'Apple_Media_Services.zip' inside first zip - {zipName}",
                        user.UserId, user.DiscordUserId, attachment.Filename);

                    return (ImportStatus.UnknownFailure, null);
                }

                await using var innerZipStream = innerZipEntry.Open();
                using var innerZip = new ZipArchive(innerZipStream, ZipArchiveMode.Read);

                var csvEntry = innerZip.Entries.FirstOrDefault(f => f.FullName.EndsWith("Apple Music Play Activity.csv", StringComparison.OrdinalIgnoreCase));
                if (csvEntry != null)
                {
                    await ExtractPlaysFromCsv(csvEntry);
                }
            }
            else
            {
                var csvEntry = zip.Entries.FirstOrDefault(f => f.FullName.EndsWith("Apple Music Play Activity.csv", StringComparison.OrdinalIgnoreCase));
                if (csvEntry != null)
                {
                    await ExtractPlaysFromCsv(csvEntry);
                }
            }
        }
        if (attachment.Filename.EndsWith(".csv"))
        {
            await using var stream = await this._httpClient.GetStreamAsync(attachment.Url);
            using var innerCsvStreamReader = new StreamReader(stream);

            using var csv = new CsvReader(innerCsvStreamReader, csvConfig);

            var records = csv.GetRecords<AppleMusicCsvImportModel>();
            if (records.Any())
            {
                appleMusicPlays.AddRange(records.Where(w => w.PlayDurationMs > 0 &&
                                                            !string.IsNullOrWhiteSpace(w.AlbumName) &&
                                                            !string.IsNullOrWhiteSpace(w.SongName)).ToList());
            }
        }

        if (appleMusicPlays.Any())
        {
            return (ImportStatus.Success, appleMusicPlays);
        }

        return (ImportStatus.UnknownFailure, null);

        async Task ExtractPlaysFromCsv(ZipArchiveEntry csvEntry)
        {
            await using var innerCsvStream = csvEntry.Open();
            using var innerCsvStreamReader = new StreamReader(innerCsvStream);

            var csv = new CsvReader(innerCsvStreamReader, csvConfig);

            var records = csv.GetRecords<AppleMusicCsvImportModel>();
            if (records.Any())
            {
                appleMusicPlays.AddRange(records.Where(w => w.PlayDurationMs > 0 &&
                                                            !string.IsNullOrWhiteSpace(w.AlbumName) &&
                                                            !string.IsNullOrWhiteSpace(w.SongName)).ToList());
            }
        }
    }

    public async Task<(RepeatedField<PlayWithoutArtist> userPlays, string matchFoundPercentage)> AppleMusicImportAddArtists(User user, List<AppleMusicCsvImportModel> appleMusicPlays)
    {
        var simplePlays = appleMusicPlays
            .Where(w => w.AlbumName != null &&
                        w.SongName != null &&
                        w.PlayDurationMs.HasValue &&
                        w.MediaDurationMs.HasValue &&
                        w.EventStartTimestamp.HasValue)
            .Select(s => new PlayWithoutArtist
            {
                ArtistName = s.ArtistName,
                AlbumName = s.AlbumName
                    .Replace("- EP", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("- Single", "", StringComparison.OrdinalIgnoreCase)
                    .TrimEnd(),
                TrackName = s.SongName,
                MsPlayed = s.PlayDurationMs.Value,
                MediaLength = s.MediaDurationMs.Value,
                Ts = DateTime.SpecifyKind(s.EventStartTimestamp.Value, DateTimeKind.Utc).ToTimestamp()
            });

        var repeatedField = new RepeatedField<PlayWithoutArtist>();
        repeatedField.AddRange(simplePlays);

        var appleMusicImports = new PlayWithoutArtistRequest
        {
            Plays = { repeatedField }
        };

        var playsWithArtist = await this._artistEnrichment.AddArtistToPlaysAsync(appleMusicImports);

        Log.Information("Importing: {userId} / {discordUserId} - AppleMusicImportToUserPlays - Total {totalPlays} - Artist found {artistFound} - Artist not found {artistNotFound}",
            user.UserId, user.DiscordUserId, playsWithArtist.Plays.Count, playsWithArtist.ArtistFound, playsWithArtist.ArtistNotFound);

        var validPlays = playsWithArtist.Plays
            .Where(w => IsValidScrobble(w.MsPlayed, w.MediaLength)).ToList();

        Log.Information("Importing: {userId} / {discordUserId} - AppleMusicImportToUserPlays - {validScrobbles} plays left after listening time filter",
            user.UserId, user.DiscordUserId, validPlays.Count);

        return (playsWithArtist.Plays, $"{(decimal)playsWithArtist.ArtistFound / playsWithArtist.Plays.Count:0%}");
    }

    public static List<UserPlay> AppleMusicImportsToValidUserPlays(User user, RepeatedField<PlayWithoutArtist> appleMusicPlays)
    {
        var validPlays = appleMusicPlays
            .Where(w => IsValidScrobble(w.MsPlayed, w.MediaLength)).ToList();

        Log.Information("Importing: {userId} / {discordUserId} - AppleMusicImportToUserPlays - {validScrobbles} plays left after listening time filter",
            user.UserId, user.DiscordUserId, validPlays.Count);

        return validPlays.Select(s => new UserPlay
        {
            UserId = user.UserId,
            TimePlayed = DateTime.SpecifyKind(s.Ts.ToDateTime(), DateTimeKind.Utc),
            ArtistName = !string.IsNullOrWhiteSpace(s.ArtistName) ? s.ArtistName : null,
            AlbumName = !string.IsNullOrWhiteSpace(s.AlbumName) ? s.AlbumName : null,
            TrackName = !string.IsNullOrWhiteSpace(s.TrackName) ? s.TrackName : null,
            MsPlayed = s.MsPlayed,
            PlaySource = PlaySource.AppleMusicImport
        }).ToList();
    }

    private static bool IsValidScrobble(long msPlayed, long mediaLength)
    {
        return msPlayed switch
        {
            < 30000 => false,
            > 240000 => true,
            _ => msPlayed > mediaLength / 2
        };
    }

    public async Task<(ImportStatus status, List<SpotifyEndSongImportModel> result, List<string> processedFiles)> HandleSpotifyFiles(User user, IEnumerable<IAttachment> attachments)
    {
        try
        {
            var spotifyPlays = new List<SpotifyEndSongImportModel>();
            var processedFiles = new List<string>();

            foreach (var attachment in attachments.Where(w => w?.Url != null && w.Filename.Contains(".json")).GroupBy(g => g.Filename))
            {
                await using var stream = await this._httpClient.GetStreamAsync(attachment.First().Url);

                try
                {
                    var result = await JsonSerializer.DeserializeAsync<List<SpotifyEndSongImportModel>>(stream);

                    spotifyPlays.AddRange(result);
                    processedFiles.Add(attachment.Key);
                }
                catch (Exception e)
                {
                    Log.Error("Importing: {userId} / {discordUserId} - Error in import .zip file ({fileName})", user.UserId, user.DiscordUserId, attachment.Key, e);
                }
            }
            foreach (var attachment in attachments.Where(w => w?.Url != null && w.Filename.Contains(".zip")).GroupBy(g => g.Filename))
            {
                await using var stream = await this._httpClient.GetStreamAsync(attachment.First().Url);

                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

                if (zip.Entries.Any(w => w.Name.Contains("Userdata")))
                {
                    return (ImportStatus.WrongPackageFailure, null, null);
                }

                foreach (var entry in zip.Entries.Where(w => w.Name.Contains(".json")))
                {
                    try
                    {
                        await using var zipStream = entry.Open();
                        var result = await JsonSerializer.DeserializeAsync<List<SpotifyEndSongImportModel>>(zipStream);

                        spotifyPlays.AddRange(result);
                        processedFiles.Add(entry.Name);
                    }
                    catch (Exception e)
                    {
                        Log.Error("Importing: {userId} / {discordUserId} - Error in import .zip file ({fileName})", user.UserId, user.DiscordUserId, entry.Name, e);
                    }
                }
            }

            return (ImportStatus.Success, spotifyPlays, processedFiles);
        }
        catch (Exception e)
        {
            Log.Error("Importing: {userId} / {discordUserId} - Error while attempting to process Spotify import file", user.UserId, user.DiscordUserId, e);
            return (ImportStatus.UnknownFailure, null, null);
        }
    }

    public async Task<List<UserPlay>> SpotifyImportToUserPlays(User user, List<SpotifyEndSongImportModel> spotifyPlays)
    {
        var simplePlays = spotifyPlays
            .Where(w => w.MasterMetadataAlbumArtistName != null &&
                        w.MasterMetadataTrackName != null)
            .Select(s => new SpotifyImportModel
            {
                MsPlayed = s.MsPlayed,
                MasterMetadataAlbumAlbumName = s.MasterMetadataAlbumAlbumName ?? "",
                MasterMetadataAlbumArtistName = s.MasterMetadataAlbumArtistName,
                MasterMetadataTrackName = s.MasterMetadataTrackName,
                Ts = Timestamp.FromDateTime(s.Ts.ToUniversalTime())
            });

        var repeatedField = new RepeatedField<SpotifyImportModel>();
        repeatedField.AddRange(simplePlays);

        var spotifyImports = new SpotifyImportsRequest
        {
            ImportedEndSongs = { repeatedField }
        };

        var filterInvalidPlays = await this._timeEnrichment.FilterInvalidSpotifyImportsAsync(spotifyImports);

        Log.Information("Importing: {userId} / {discordUserId} - SpotifyImportToUserPlays found {validPlays} valid plays and {invalidPlays} invalid plays",
            user.UserId, user.DiscordUserId, filterInvalidPlays.ValidImports.Count, filterInvalidPlays.InvalidPlays);

        return filterInvalidPlays.ValidImports.Select(s => new UserPlay
        {
            UserId = user.UserId,
            TimePlayed = DateTime.SpecifyKind(s.Ts.ToDateTime(), DateTimeKind.Utc),
            ArtistName = s.MasterMetadataAlbumArtistName,
            AlbumName = !string.IsNullOrWhiteSpace(s.MasterMetadataAlbumAlbumName) ? s.MasterMetadataAlbumAlbumName : null,
            TrackName = s.MasterMetadataTrackName,
            PlaySource = PlaySource.SpotifyImport,
            MsPlayed = s.MsPlayed
        }).ToList();
    }

    public async Task<List<UserPlay>> RemoveDuplicateImports(int userId, IEnumerable<UserPlay> userPlays)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var existingPlays = await PlayRepository.GetAllUserPlays(userId, connection);

        var timestamps = existingPlays
            .Where(w => w.PlaySource == PlaySource.SpotifyImport || w.PlaySource == PlaySource.AppleMusicImport)
            .GroupBy(g => g.TimePlayed)
            .ToDictionary(d => d.Key, d => d.ToList());

        var playsToReturn = new List<UserPlay>();
        foreach (var userPlay in userPlays)
        {
            if (!timestamps.ContainsKey(userPlay.TimePlayed))
            {
                playsToReturn.Add(userPlay);
                timestamps.Add(userPlay.TimePlayed, new List<UserPlay> { userPlay });
            }
            else
            {
                var playsWithTimestamp = timestamps[userPlay.TimePlayed];

                if (!playsWithTimestamp.Any(a => a.TrackName == userPlay.TrackName &&
                                                 a.AlbumName == userPlay.AlbumName &&
                                                 a.ArtistName == userPlay.ArtistName))
                {
                    playsToReturn.Add(userPlay);
                    playsWithTimestamp.Add(userPlay);
                }
            }
        }

        return playsToReturn;
    }

    public async Task UpdateExistingPlays(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.SetDefaultSourceForPlays(user.UserId, connection);
        Log.Information("Importing: {userId} / {discordUserId} - Set default data source", user.UserId, user.DiscordUserId);
    }

    public async Task InsertImportPlays(User user, IList<UserPlay> plays)
    {
        if (plays != null && plays.Any())
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            var inserted = await PlayRepository.InsertTimeSeriesPlays(plays, connection);
            Log.Information("Importing: {userId} / {discordUserId} - Inserted {insertCount} plays (Should be {importCount})", user.UserId, user.DiscordUserId, inserted, plays.Count);
        }
        else
        {
            Log.Error("Importing: {userId} / {discordUserId} Tried to insert 0 import plays!", user.UserId, user.DiscordUserId);
        }
    }

    public async Task RemoveImportedSpotifyPlays(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.RemoveAllImportedSpotifyPlays(user.UserId, connection);

        Log.Information("Importing: {userId} / {discordUserId} - Removed imported Spotify plays", user.UserId, user.DiscordUserId);
    }

    public async Task RemoveImportedAppleMusicPlays(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.RemoveAllImportedAppleMusicPlays(user.UserId, connection);

        Log.Information("Importing: {userId} / {discordUserId} - Removed imported Apple Music plays", user.UserId, user.DiscordUserId);
    }

    public async Task<bool> HasImported(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await PlayRepository.HasImported(userId, connection);
    }

    public static string AddImportDescription(StringBuilder stringBuilder, List<PlaySource> playSources)
    {
        if (playSources == null || playSources.Count == 0)
        {
            return null;
        }
        if (playSources.Contains(PlaySource.SpotifyImport) && playSources.Contains(PlaySource.AppleMusicImport))
        {
            stringBuilder.AppendLine("Contains imported Spotify and Apple Music plays");
        }
        else if (playSources.Contains(PlaySource.SpotifyImport))
        {
            stringBuilder.AppendLine("Contains imported Spotify plays");
        }
        else if (playSources.Contains(PlaySource.AppleMusicImport))
        {
            stringBuilder.AppendLine("Contains imported Apple Music plays");
        }

        return null;
    }
}
