using System.Net.Http;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static FMBot.Bot.Models.OpenAIModels;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;
using System;
using System.Text;
using FMBot.Bot.Extensions;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Extensions;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class OpenAiService(
    HttpClient httpClient,
    IOptions<BotSettings> botSettings,
    IDbContextFactory<FMBotDbContext> contextFactory,
    IMemoryCache cache,
    CountryService countryService)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    private const int MinimumDescriptionSourceLength = 150;
    private const int MaximumDescriptionSourceLength = 2000;
    private const int MaximumEditorialSourceLength = 1200;

    private static readonly SemaphoreSlim DescriptionConcurrency = new(6, 6);

    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> DescriptionsInFlight = new();

    private static readonly Regex SentenceEndRegex = new(@"[.!?](\s|$)", RegexOptions.Compiled);
    private static readonly Regex DescriptionNumberRegex = new(@"\d[\d,.]*", RegexOptions.Compiled);
    private static readonly Regex NumberSeparatorRegex = new(@"[,.]", RegexOptions.Compiled);

    private static readonly string[] DescriptionForbiddenFragments =
        ["```", "http", "](", "<", ">", "#", "**"];

    private static readonly string[] DescriptionRefusalPhrases =
    [
        "as an ai", "i cannot", "i can't", "i don't have", "i do not have", "not enough information",
        "insufficient information", "the source text", "the provided", "the metadata"
    ];

    private static readonly string[] SentenceAbbreviations =
        ["Mr.", "Mrs.", "Ms.", "Dr.", "St.", "vs.", "feat.", "No.", "Jr.", "Sr.", "U.S.", "etc."];

    private async Task<OpenAiResponse> SendRequest(string prompt, string model = "gpt-5.4-mini",
        string userMessage = null, string imageUrl = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Add("Authorization", $"Bearer {this._botSettings.OpenAi.Key}");

        var inputMessages = new List<InputMessage>
        {
            new()
            {
                Role = "developer",
                Content = [new InputContent { Type = "input_text", Text = prompt }]
            }
        };

        if (userMessage != null || imageUrl != null)
        {
            var userContent = new List<InputContent>();

            if (userMessage != null)
            {
                userContent.Add(new InputContent { Type = "input_text", Text = userMessage });
            }

            if (imageUrl != null)
            {
                userContent.Add(new InputContent
                {
                    Type = "input_image",
                    ImageUrl = imageUrl,
                    Detail = "high"
                });
            }

            inputMessages.Add(new InputMessage
            {
                Role = "user",
                Content = userContent
            });
        }

        var content = new ResponsesRequest
        {
            Model = model,
            Input = inputMessages,
            Text = new TextConfig
            {
                Format = new TextFormat { Type = "text" },
                Verbosity = "medium"
            },
            Reasoning = new ReasoningConfig
            {
                Effort = "none",
                Summary = "auto"
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(content), null, "application/json");
        var response = await httpClient.SendAsync(request, cancellationToken);
        Statistics.OpenAiCalls.Inc();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Log.Error("OpenAI: request to {Model} failed with {StatusCode} - {Response}", model,
                response.StatusCode, responseContent);

            return new OpenAiResponse
            {
                Model = model,
                Prompt = prompt,
                Usage = new Usage()
            };
        }

        var responsesModel = JsonSerializer.Deserialize<ResponsesResponse>(responseContent);

        return new OpenAiResponse
        {
            Model = responsesModel.Model,
            Usage = responsesModel.Usage,
            Prompt = prompt,
            Output = responsesModel.Output?.FirstOrDefault(o => o.Type == "message")?.Content?.FirstOrDefault()?.Text
        };
    }

    public async Task<OpenAiResponse> GetJudgeResponse(List<TopArtist> artists, List<TopTrack> topTracks,
        PromptType promptType, int amountThisWeek, bool supporter = false, Language language = Language.English)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var prompt = await db.AiPrompts
            .OrderByDescending(o => o.Version)
            .FirstAsync(f => f.Type == promptType &&
                             f.Language == "en-us");

        var promptText = prompt.Prompt;
        if (language != Language.English)
        {
            var languageName = language.GetEnglishName();
            promptText +=
                $"\n\nWrite your entire response in {languageName}. Keep artist, album and track names exactly as they are. " +
                $"The response should read like it was originally written by a native {languageName} speaker, not translated from English.";
        }

        var music = new StringBuilder();
        music.AppendLine("My top artists: ");
        foreach (var artist in artists.OrderByDescending(o => o.UserPlaycount).Take(14))
        {
            music.Append(artist.ArtistName[..Math.Min(artist.ArtistName.Length, 40)]);
            music.Append($" - {artist.UserPlaycount} plays");
            music.AppendLine();
        }

        music.AppendLine();
        music.AppendLine("My top tracks: ");
        foreach (var track in topTracks.OrderByDescending(o => o.UserPlaycount).Take(16))
        {
            music.Append(track.TrackName[..Math.Min(track.TrackName.Length, 50)]);
            music.Append(" by ");
            music.Append(track.ArtistName[..Math.Min(track.ArtistName.Length, 40)]);
            music.Append($" - {track.UserPlaycount} plays");
            music.AppendLine();
        }

        var model = supporter ? (amountThisWeek <= 2 ? prompt.UltraModel : prompt.PremiumModel) : prompt.FreeModel;

        return await SendRequest(promptText, model, music.ToString());
    }

    public async Task<AiGeneration> StoreAiGeneration(ulong contextId, int userId, int? targetedUserId)
    {
        var generation = new AiGeneration
        {
            DateGenerated = DateTime.UtcNow,
            DiscordId = contextId,
            UserId = userId,
            TargetedUserId = targetedUserId
        };

        await using var db = await contextFactory.CreateDbContextAsync();

        await db.AiGenerations.AddAsync(generation);

        await db.SaveChangesAsync();

        return generation;
    }

    public async Task<AiGeneration> UpdateAiGeneration(ulong contextId, OpenAiResponse response)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var existingGeneration = await db.AiGenerations.FirstAsync(f => f.DiscordId == contextId);

        existingGeneration.Model = response.Model;
        existingGeneration.Output = response.Output;
        existingGeneration.TotalTokens = response.Usage.TotalTokens;
        existingGeneration.Prompt = response.Prompt;

        db.Update(existingGeneration);

        await db.SaveChangesAsync();

        return existingGeneration;
    }

    public async Task<(int amount, bool show, int amountThisWeek)> GetJudgeUsesLeft(User user)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var filterDate = DateTime.UtcNow.Date;
        var generatedToday =
            await db.AiGenerations.CountAsync(c => c.UserId == user.UserId && c.DateGenerated >= filterDate);

        var filterWeek = DateTime.UtcNow.AddDays(-7);
        var amountThisWeek =
            await db.AiGenerations.CountAsync(c => c.UserId == user.UserId && c.DateGenerated >= filterWeek);

        var maxDailyUses = SupporterService.IsSupporter(user.UserType) ? 25 : 6;
        return (maxDailyUses - generatedToday, generatedToday >= maxDailyUses / 2, amountThisWeek);
    }

    public async Task<bool> CheckIfUsernameOffensive(string username)
    {
        try
        {
            var response =
                await SendRequest(
                    "You check Last.fm usernames before they are shown publicly by a Discord bot. " +
                    "Reply 'true' only if the username contains a slur, hate speech, an explicit sexual reference, or a clear reference to a tragedy or atrocity. " +
                    "Mild rudeness, jokes, innuendo, edgy words, drug references or dark humor are all 'false'. " +
                    "If in doubt, reply 'false'. Only reply with 'true' or 'false'.\n\n" +
                    $"Username: '{username}'");

            var output = response.Output;
            return output != null && output.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Log.Error(e, "Feature: Error in OpenAI call");
            return false;
        }
    }

    public async Task<bool> CheckIfAlbumOffensive(string albumName, string artistName, string imageUrl)
    {
        try
        {
            Log.Information("Featured: Album offensive check for {Album} by {Artist}, image: {ImageUrl}",
                albumName, artistName, imageUrl);

            var description = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(albumName))
            {
                description.AppendLine($"Album: {albumName}");
            }
            if (!string.IsNullOrWhiteSpace(artistName))
            {
                description.AppendLine($"Artist: {artistName}");
            }

            var response = await SendRequest(
                "You check whether an album cover is safe to use as the public avatar of a Discord bot. Consider the album name, the artist name and the cover image. " +
                "Reply 'true' if it contains: nudity (genitalia, anuses or female-presenting nipples), erotic or sexual content, " +
                "people covered in blood or wounds, gore, hate speech or hate symbols, or slurs in the album or artist name. " +
                "Parental Advisory stickers, dark or unsettling artwork and profanity that is not a slur are 'false'. " +
                "If unsure whether any of the above is present, reply 'true'. Only reply with 'true' or 'false'.",
                userMessage: description.Length > 0 ? description.ToString() : null,
                imageUrl: string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl);

            var output = response.Output;
            return output != null && output.Contains("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e)
        {
            Log.Error(e, "Feature: Error in OpenAI call");
            return false;
        }
    }

    public bool RecapCacheHot(string timePeriod, string lastFmUserName, Language language = Language.English)
    {
        return cache.TryGetValue(RecapCacheKey(timePeriod, lastFmUserName, language), out _);
    }

    private static string RecapCacheKey(string timePeriod, string lastFmUserName, Language language)
    {
        return $"{lastFmUserName}-recap-{timePeriod}-{language.GetLocaleCode()}";
    }

    public async Task<string> GetPlayRecap(string timePeriod, List<UserPlay> userPlays, string lastFmUserName,
        Response<TopArtistList> topArtists, Language language = Language.English)
    {
        try
        {
            var cacheKey = RecapCacheKey(timePeriod, lastFmUserName, language);
            if (cache.TryGetValue(cacheKey, out string cachedResponse))
            {
                return cachedResponse;
            }

            await using var db = await contextFactory.CreateDbContextAsync();
            var prompt = await db.AiPrompts
                .OrderByDescending(o => o.Version)
                .FirstAsync(f => f.Type == PromptType.Recap);

            var promptText = prompt.Prompt.Replace("{{recapType}}", timePeriod);
            if (language != Language.English)
            {
                var languageName = language.GetEnglishName();
                promptText +=
                    $"\n\nWrite your entire response in {languageName}. Keep artist, album and track names exactly as they are. " +
                    $"The response should read like it was originally written by a native {languageName} speaker, not translated from English.";
            }

            var promptBuilder = new StringBuilder();

            if (topArtists?.Content?.TopArtists != null)
            {
                promptBuilder.AppendLine("Top 80 artists");
                foreach (var topArtist in topArtists.Content.TopArtists.Take(80))
                {
                    promptBuilder.AppendLine(
                        $"{StringExtensions.TruncateLongString(topArtist.ArtistName, 28)}, {topArtist.UserPlaycount} plays");
                }
            }

            if (userPlays.Count > 100)
            {
                var topAlbums = userPlays
                    .Where(w => w.AlbumName != null)
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        AlbumName = g.AlbumName.ToLower()
                    })
                    .OrderByDescending(o => o.Count())
                    .Take(40)
                    .ToList();

                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine("Top 40 albums");
                foreach (var topAlbum in topAlbums)
                {
                    promptBuilder.AppendLine(
                        $"{StringExtensions.TruncateLongString(topAlbum.Key.AlbumName, 32)} by {StringExtensions.TruncateLongString(topAlbum.Key.ArtistName, 28)}, " +
                        $"{topAlbum.Count()} plays");
                }

                var topTracks = userPlays
                    .GroupBy(g => new
                    {
                        ArtistName = g.ArtistName.ToLower(),
                        TrackName = g.TrackName.ToLower()
                    })
                    .OrderByDescending(o => o.Count())
                    .Take(40)
                    .ToList();

                promptBuilder.AppendLine("---");
                promptBuilder.AppendLine("Top 40 tracks");
                foreach (var topTrack in topTracks)
                {
                    promptBuilder.AppendLine(
                        $"{StringExtensions.TruncateLongString(topTrack.Key.TrackName, 32)} by {StringExtensions.TruncateLongString(topTrack.Key.ArtistName, 28)}, " +
                        $"{topTrack.Count()} plays");
                }
            }

            var response = await SendRequest(promptText, userMessage: promptBuilder.ToString());

            if (string.IsNullOrWhiteSpace(response?.Output))
            {
                return null;
            }

            cache.Set(cacheKey, response.Output, TimeSpan.FromHours(2));

            return response.Output;
        }
        catch (Exception e)
        {
            Log.Error(e, "Recap: Error in OpenAI call");
            return null;
        }
    }

    public Task<string> GetArtistDescription(Artist dbArtist, ArtistInfo lastFmArtist)
    {
        if (dbArtist == null || lastFmArtist == null)
        {
            return Task.FromResult(lastFmArtist?.Description);
        }

        return GetMusicDescription("artist", dbArtist.Id, lastFmArtist.Description, dbArtist.AiDescription,
            dbArtist.AiDescriptionHash,
            source => BuildArtistContext(dbArtist, lastFmArtist, source),
            async (description, hash) =>
            {
                dbArtist.AiDescription = description;
                dbArtist.AiDescriptionHash = hash;

                await using var db = await contextFactory.CreateDbContextAsync();

                var artist = new Artist { Id = dbArtist.Id };
                db.Artists.Attach(artist);
                artist.AiDescription = description;
                artist.AiDescriptionHash = hash;
                artist.AiDescriptionDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                await db.SaveChangesAsync();
            });
    }

    public Task<string> GetAlbumDescription(Album dbAlbum, AlbumInfo lastFmAlbum)
    {
        if (dbAlbum == null || lastFmAlbum == null)
        {
            return Task.FromResult(lastFmAlbum?.Description);
        }

        return GetMusicDescription("album", dbAlbum.Id, lastFmAlbum.Description, dbAlbum.AiDescription,
            dbAlbum.AiDescriptionHash,
            source => BuildAlbumContext(dbAlbum, lastFmAlbum, source),
            async (description, hash) =>
            {
                dbAlbum.AiDescription = description;
                dbAlbum.AiDescriptionHash = hash;

                await using var db = await contextFactory.CreateDbContextAsync();

                var album = new Album { Id = dbAlbum.Id };
                db.Albums.Attach(album);
                album.AiDescription = description;
                album.AiDescriptionHash = hash;
                album.AiDescriptionDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                await db.SaveChangesAsync();
            });
    }

    public Task<string> GetTrackDescription(Track dbTrack, TrackInfo lastFmTrack)
    {
        if (dbTrack == null || lastFmTrack == null)
        {
            return Task.FromResult(lastFmTrack?.Description);
        }

        return GetMusicDescription("track", dbTrack.Id, lastFmTrack.Description, dbTrack.AiDescription,
            dbTrack.AiDescriptionHash,
            source => BuildTrackContext(dbTrack, lastFmTrack, source),
            async (description, hash) =>
            {
                dbTrack.AiDescription = description;
                dbTrack.AiDescriptionHash = hash;

                await using var db = await contextFactory.CreateDbContextAsync();

                var track = new Track { Id = dbTrack.Id };
                db.Tracks.Attach(track);
                track.AiDescription = description;
                track.AiDescriptionHash = hash;
                track.AiDescriptionDate = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

                await db.SaveChangesAsync();
            });
    }

    private async Task<string> GetMusicDescription(string entityType, int entityId, string lastFmDescription,
        string storedDescription, string storedHash, Func<string, string> buildContext,
        Func<string, string, Task> store)
    {
        if (string.IsNullOrWhiteSpace(lastFmDescription))
        {
            return null;
        }

        if (entityId == 0)
        {
            return lastFmDescription;
        }

        var source = StringExtensions.StripHtml(lastFmDescription);
        var sourceHash = HashDescriptionSource(source);

        if (!string.IsNullOrWhiteSpace(storedDescription) && storedHash == sourceHash)
        {
            return storedDescription;
        }

        if (source.Length < MinimumDescriptionSourceLength)
        {
            return lastFmDescription;
        }

        var failedCacheKey = $"ai-desc-failed-{entityType}-{entityId}";
        if (cache.TryGetValue(failedCacheKey, out _))
        {
            return storedDescription ?? lastFmDescription;
        }

        var generated = await GetOrStartDescription($"{entityType}-{entityId}",
            () => GenerateMusicDescription(entityType, entityId, source, sourceHash, failedCacheKey, buildContext,
                store));

        return generated ?? storedDescription ?? lastFmDescription;
    }

    private async Task<string> GenerateMusicDescription(string entityType, int entityId, string source,
        string sourceHash, string failedCacheKey, Func<string, string> buildContext, Func<string, string, Task> store)
    {
        if (!await DescriptionConcurrency.WaitAsync(TimeSpan.Zero))
        {
            Statistics.AiDescriptionGenerations.WithLabels(entityType, "busy").Inc();
            return null;
        }

        try
        {
            var prompt = await GetMusicDescriptionPrompt();
            if (prompt == null)
            {
                Statistics.AiDescriptionGenerations.WithLabels(entityType, "error").Inc();
                return null;
            }

            var groundingContext = buildContext(source);

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var response = await SendRequest(prompt.Prompt.Replace("{{entityType}}", entityType), prompt.FreeModel,
                groundingContext, cancellationToken: cancellationTokenSource.Token);

            if (!TryValidateDescription(response?.Output, groundingContext, out var cleaned, out var reason))
            {
                Statistics.AiDescriptionGenerations.WithLabels(entityType, reason).Inc();
                Log.Warning(
                    "AiDescription: rejected generation for {EntityType} {EntityId} because of {Reason} - {Output}",
                    entityType, entityId, reason, response?.Output);

                cache.Set(failedCacheKey, true, TimeSpan.FromHours(6));
                return null;
            }

            await store(cleaned, sourceHash);

            Statistics.AiDescriptionGenerations.WithLabels(entityType, "stored").Inc();
            return cleaned;
        }
        catch (OperationCanceledException)
        {
            Statistics.AiDescriptionGenerations.WithLabels(entityType, "timeout").Inc();
            Log.Warning("AiDescription: timed out generating for {EntityType} {EntityId}", entityType, entityId);
            return null;
        }
        catch (Exception e)
        {
            Statistics.AiDescriptionGenerations.WithLabels(entityType, "error").Inc();
            Log.Error(e, "AiDescription: error generating for {EntityType} {EntityId}", entityType, entityId);
            return null;
        }
        finally
        {
            DescriptionConcurrency.Release();
        }
    }

    private static Task<string> GetOrStartDescription(string key, Func<Task<string>> factory)
    {
        var lazy = DescriptionsInFlight.GetOrAdd(key,
            _ => new Lazy<Task<string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication));

        var task = lazy.Value;
        _ = task.ContinueWith(completed => DescriptionsInFlight.TryRemove(key, out _), TaskScheduler.Default);

        return task;
    }

    private async Task<AiPrompt> GetMusicDescriptionPrompt()
    {
        const string cacheKey = "ai-prompt-music-description";
        if (cache.TryGetValue(cacheKey, out AiPrompt cachedPrompt))
        {
            return cachedPrompt;
        }

        await using var db = await contextFactory.CreateDbContextAsync();
        var prompt = await db.AiPrompts
            .OrderByDescending(o => o.Version)
            .FirstOrDefaultAsync(f => f.Type == PromptType.MusicDescription &&
                                      f.Language == "en-us");

        if (prompt == null)
        {
            Log.Warning("AiDescription: no ai_prompts row found for MusicDescription");
            return null;
        }

        cache.Set(cacheKey, prompt, TimeSpan.FromMinutes(30));

        return prompt;
    }

    public static string HashDescriptionSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));

        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private string BuildArtistContext(Artist dbArtist, ArtistInfo lastFmArtist, string source)
    {
        var context = new StringBuilder();

        context.AppendLine("ENTITY: artist");
        context.AppendLine($"NAME: {dbArtist.Name}");
        context.AppendLine();
        context.AppendLine("SOURCE (Last.fm biography):");
        context.AppendLine(StringExtensions.TruncateLongString(source, MaximumDescriptionSourceLength));
        context.AppendLine();
        context.AppendLine("METADATA (verified):");

        AppendDescriptionMetadata(context, "Disambiguation", dbArtist.Disambiguation);
        AppendDescriptionMetadata(context, "Country", CountryNameForCode(dbArtist.CountryCode));
        AppendDescriptionMetadata(context, "Location", dbArtist.Location);
        AppendDescriptionMetadata(context, "Type", dbArtist.Type);
        AppendDescriptionMetadata(context, "Gender", dbArtist.Gender);
        AppendDescriptionMetadata(context, "Active from", FormatDescriptionDate(dbArtist.StartDate));
        AppendDescriptionMetadata(context, "Active until", FormatDescriptionDate(dbArtist.EndDate));
        AppendDescriptionMetadata(context, "Genres", JoinDescriptionValues(dbArtist.ArtistGenres?.Select(s => s.Name)));
        AppendDescriptionMetadata(context, "Last.fm tags", JoinDescriptionValues(lastFmArtist.Tags?.Select(s => s.Name)));

        return context.ToString();
    }

    private static string BuildAlbumContext(Album dbAlbum, AlbumInfo lastFmAlbum, string source)
    {
        var context = new StringBuilder();

        context.AppendLine("ENTITY: album");
        context.AppendLine($"NAME: {dbAlbum.Name}");
        context.AppendLine($"ARTIST: {dbAlbum.ArtistName}");
        context.AppendLine();
        context.AppendLine("SOURCE (Last.fm album wiki):");
        context.AppendLine(StringExtensions.TruncateLongString(source, MaximumDescriptionSourceLength));

        AppendEditorialSource(context, dbAlbum.AppleMusicTagline,
            dbAlbum.AppleMusicShortDescription ?? dbAlbum.AppleMusicDescription);

        context.AppendLine();
        context.AppendLine("METADATA (verified):");

        AppendDescriptionMetadata(context, "Release date", dbAlbum.ReleaseDate);
        AppendDescriptionMetadata(context, "Release date precision", dbAlbum.ReleaseDatePrecision);
        AppendDescriptionMetadata(context, "Release type", dbAlbum.Type);
        AppendDescriptionMetadata(context, "Label", dbAlbum.Label);
        AppendDescriptionMetadata(context, "Last.fm tags", JoinDescriptionValues(lastFmAlbum.Tags?.Select(s => s.Name)));
        AppendDescriptionMetadata(context, "Tracks",
            JoinDescriptionValues(lastFmAlbum.AlbumTracks?.Select(s => s.TrackName), 20, "; "));

        return context.ToString();
    }

    private static string BuildTrackContext(Track dbTrack, TrackInfo lastFmTrack, string source)
    {
        var context = new StringBuilder();

        context.AppendLine("ENTITY: track");
        context.AppendLine($"NAME: {dbTrack.Name}");
        context.AppendLine($"ARTIST: {dbTrack.ArtistName}");

        if (!string.IsNullOrWhiteSpace(dbTrack.AlbumName))
        {
            context.AppendLine($"ALBUM: {dbTrack.AlbumName}");
        }

        context.AppendLine();
        context.AppendLine("SOURCE (Last.fm track wiki):");
        context.AppendLine(StringExtensions.TruncateLongString(source, MaximumDescriptionSourceLength));

        AppendEditorialSource(context, dbTrack.AppleMusicTagline,
            dbTrack.AppleMusicShortDescription ?? dbTrack.AppleMusicDescription);

        context.AppendLine();
        context.AppendLine("METADATA (verified):");

        AppendDescriptionMetadata(context, "Disambiguation", dbTrack.Disambiguation);
        AppendDescriptionMetadata(context, "Duration", FormatDescriptionDuration(dbTrack.DurationMs));
        AppendDescriptionMetadata(context, "Last.fm tags", JoinDescriptionValues(lastFmTrack.Tags?.Select(s => s.Name)));

        return context.ToString();
    }

    private static void AppendEditorialSource(StringBuilder context, string tagline, string editorial)
    {
        var strippedTagline = StringExtensions.StripHtml(tagline);
        var strippedEditorial = StringExtensions.StripHtml(editorial);

        if (string.IsNullOrWhiteSpace(strippedTagline) && string.IsNullOrWhiteSpace(strippedEditorial))
        {
            return;
        }

        context.AppendLine();
        context.AppendLine("SOURCE 2 (Apple Music editorial):");

        if (!string.IsNullOrWhiteSpace(strippedTagline))
        {
            context.AppendLine(strippedTagline);
        }

        if (!string.IsNullOrWhiteSpace(strippedEditorial))
        {
            context.AppendLine(StringExtensions.TruncateLongString(strippedEditorial, MaximumEditorialSourceLength));
        }
    }

    private static void AppendDescriptionMetadata(StringBuilder context, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        context.AppendLine($"{label}: {value}");
    }

    private static string JoinDescriptionValues(IEnumerable<string> values, int amount = 8, string separator = ", ")
    {
        if (values == null)
        {
            return null;
        }

        return string.Join(separator, values.Where(w => !string.IsNullOrWhiteSpace(w)).Take(amount));
    }

    private static string FormatDescriptionDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd");
    }

    private static string FormatDescriptionDuration(int? durationMs)
    {
        if (durationMs is not > 0)
        {
            return null;
        }

        return TimeSpan.FromMilliseconds(durationMs.Value).ToString(@"m\:ss");
    }

    private string CountryNameForCode(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        return countryService.Countries.FirstOrDefault(f => f.Code == countryCode)?.Name;
    }

    public static bool TryValidateDescription(string raw, string groundingContext, out string cleaned,
        out string reason)
    {
        cleaned = null;
        reason = "invalid";

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var candidate = string.Join(' ', raw.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (candidate.Length > 1 &&
            ((candidate[0] == '"' && candidate[^1] == '"') || (candidate[0] == '\'' && candidate[^1] == '\'')))
        {
            candidate = candidate[1..^1].Trim();
        }

        if (candidate.TrimEnd('.', '!').Equals("INSUFFICIENT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "insufficient";
            return false;
        }

        if (candidate.Length is < 40 or > 400)
        {
            return false;
        }

        if (DescriptionForbiddenFragments.Any(a => candidate.Contains(a, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (DescriptionRefusalPhrases.Any(a => candidate.Contains(a, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var forSentenceCount = SentenceAbbreviations.Aggregate(candidate,
            (current, abbreviation) => current.Replace(abbreviation, "", StringComparison.OrdinalIgnoreCase));

        var sentences = SentenceEndRegex.Matches(forSentenceCount).Count;
        if (sentences is < 1 or > 3)
        {
            return false;
        }

        var normalizedGrounding = NumberSeparatorRegex.Replace(groundingContext ?? "", "");
        foreach (Match match in DescriptionNumberRegex.Matches(candidate))
        {
            var number = NumberSeparatorRegex.Replace(match.Value, "");
            if (number.Length < 2)
            {
                continue;
            }

            if (!normalizedGrounding.Contains(number, StringComparison.Ordinal))
            {
                return false;
            }
        }

        cleaned = candidate.FilterOutMentions().Trim();

        return !string.IsNullOrWhiteSpace(cleaned);
    }
}
