using System;
using System.Threading.Tasks;
using FMBot.Youtube.Models;
using FMBot.Youtube.Services;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace FMBot.Bot.Services.ThirdParty;

public class YoutubeService
    {
        private readonly YouTubeService _youtubeService;

        public YoutubeService(IConfiguration configuration)
        {
            try
            {
                this._youtubeService = new YouTubeService(new BaseClientService.Initializer
                {
                    ApiKey = configuration.GetSection("Google:ApiKey").Value,
                    ApplicationName = "fmbot"
                });
            }
            catch (Exception ex)
            {
                Log.Error("Error initializing YouTube service", ex);
                throw;
            }
        }

        public async Task<SearchResult> GetSearchResult(string searchValue)
        {
            try
            {
                var searchRequest = this._youtubeService.Search.List("snippet");
                searchRequest.Q = searchValue;
                searchRequest.MaxResults = 1;
                searchRequest.Type = "video";

                var searchResponse = await searchRequest.ExecuteAsync();

                return searchResponse.Items.Count > 0 ? searchResponse.Items[0] : null;
            }
            catch (Exception ex)
            {
                Log.Error("Error searching YouTube video", ex);
                return null;
            }
        }

        public async Task<Video> GetVideoResult(string videoId)
        {
            try
            {
                var videoRequest = this._youtubeService.Videos.List("snippet,statistics,contentDetails");
                videoRequest.Id = videoId;

                var videoResponse = await videoRequest.ExecuteAsync();

                return videoResponse.Items.Count > 0 ? videoResponse.Items[0] : null;
            }
            catch (Exception ex)
            {
                Log.Error("Error getting YouTube video details", ex);
                return null;
            }
        }

        public bool IsFamilyFriendly(Video video)
        {
            if (video?.ContentDetails == null)
            {
                return false;
            }

            return !video.ContentDetails.ContentRating?.YtRating?.Equals("ytAgeRestricted", StringComparison.OrdinalIgnoreCase) ?? true;
        }
    }
