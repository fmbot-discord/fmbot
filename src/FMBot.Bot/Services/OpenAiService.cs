using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    IMemoryCache cache)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    private async Task<OpenAiResponse> SendRequest(string prompt, string model = "gpt-5.4-mini",
        string userMessage = null, string imageUrl = null)
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
        var response = await httpClient.SendAsync(request);
        Statistics.OpenAiCalls.Inc();

        var responseContent = await response.Content.ReadAsStringAsync();
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
                await SendRequest($"Is the username '{username}' offensive? Only reply with 'true' or 'false'.");

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
                "Is this album offensive or NSFW? Consider the album name, the artist name and the cover image. " +
                "Only reply with 'true' or 'false'.",
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
}
