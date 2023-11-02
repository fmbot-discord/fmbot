using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Bot.Services;

public class ImportService
{
    private readonly HttpClient _httpClient;
    private readonly TimeService _timeService;
    private readonly BotSettings _botSettings;

    public ImportService(HttpClient httpClient, TimeService timeService, IOptions<BotSettings> botSettings)
    {
        this._httpClient = httpClient;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
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
        var userPlays = new List<UserPlay>();

        var invalidPlays = 0;

        foreach (var spotifyPlay in spotifyPlays
                     .Where(w => w.MasterMetadataAlbumArtistName != null &&
                                 w.MasterMetadataTrackName != null))
        {
            var validScrobble = await this._timeService.IsValidScrobble(
                spotifyPlay.MasterMetadataAlbumArtistName, spotifyPlay.MasterMetadataTrackName, spotifyPlay.MsPlayed);

            if (validScrobble)
            {
                userPlays.Add(new UserPlay
                {
                    UserId = user.UserId,
                    TimePlayed = DateTime.SpecifyKind(spotifyPlay.Ts, DateTimeKind.Utc),
                    ArtistName = spotifyPlay.MasterMetadataAlbumArtistName,
                    AlbumName = spotifyPlay.MasterMetadataAlbumAlbumName,
                    TrackName = spotifyPlay.MasterMetadataTrackName,
                    PlaySource = PlaySource.SpotifyImport,
                    MsPlayed = spotifyPlay.MsPlayed
                });
            }
            else
            {
                invalidPlays++;
            }
        }

        Log.Information("Importing: {userId} / {discordUserId} - SpotifyImportToUserPlays found {validPlays} valid plays and {invalidPlays} invalid plays",
            user.UserId, user.DiscordUserId, userPlays.Count, invalidPlays);

        return userPlays;
    }

    public async Task<List<UserPlay>> RemoveDuplicateSpotifyImports(int userId, IEnumerable<UserPlay> userPlays)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var existingPlays = await PlayRepository.GetAllUserPlays(userId, connection);

        var timestamps = existingPlays
            .Where(w => w.PlaySource == PlaySource.SpotifyImport)
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

    public async Task RemoveImportPlays(User user)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.RemoveAllImportPlays(user.UserId, connection);

        Log.Information("Importing: {userId} / {discordUserId} - Removed imported plays", user.UserId, user.DiscordUserId);
    }

    public async Task<bool> HasImported(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        return await PlayRepository.HasImported(userId, connection);
    }

    public static string AddImportDescription(StringBuilder stringBuilder, PlaySource? playSource)
    {
        if (playSource == PlaySource.SpotifyImport)
        {
            stringBuilder.AppendLine("Contains imported Spotify plays");
        }

        return null;
    }
}
