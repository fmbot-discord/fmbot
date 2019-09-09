// YoutubeSearch
// YoutubeSearch is a library for .NET, written in C#, to show search query results from YouTube.
//
// (c) 2016 Torsten Klinger - torsten.klinger(at)googlemail.com
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see<http://www.gnu.org/licenses/>.

using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System;

namespace YoutubeSearch
{
    public class VideoSearch
    {
        //constants for easier maintainability
        private const string Pattern =
            "<div class=\"yt-lockup-content\">.*?title=\"(?<NAME>.*?)\".*?</div></div></div></li>";

        private const string YtQueryUrl = "https://www.youtube.com/results?search_query=";
        private const string YtThumbnailUrl = "https://i.ytimg.com/vi/";
        private const string YtWatchUrl = "http://www.youtube.com/watch?v=";


        List<VideoInformation> items;

        WebClient webclient;

        string title;
        string author;
        string description;
        string duration;
        string url;
        string thumbnail;
        string viewCount;
        bool noDesc = false;
        bool noAuthor = false;

        /// <summary>
        /// Doing asynchronous search query with given parameters. Returns a List object.
        /// </summary>
        /// <param name="querystring"></param>
        /// <param name="querypages">number of pages to query</param>
        /// <param name="querypagesOffset">offset for querypages</param>
        /// <returns></returns>
        public async Task<List<VideoInformation>> SearchQueryTaskAsync(string querystring, int querypages,
            int querypagesOffset = 0)
        {
            //negative index not allowed
            if (querypagesOffset < 0)
                querypagesOffset = 0;


            items = new List<VideoInformation>();

            webclient = new WebClient();

            // Do search, regarding offset
            for (int i = (1 + querypagesOffset); i <= (querypages + querypagesOffset); i++)
            {
                // Search address
                string html = await webclient.DownloadStringTaskAsync(YtQueryUrl + querystring + "&page=" + i);

                //extract information from page
                ProcessPage(html);
            }

            return items;
        }

        /// <summary>
        /// Doing search query with given parameters. Returns a List object.
        /// </summary>
        /// <param name="querystring"></param>
        /// <param name="querypages">number of pages to query</param>
        /// <param name="querypagesOffset">offset for querypages</param>
        /// <returns></returns>
        public List<VideoInformation> SearchQuery(string querystring, int querypages, int querypagesOffset = 0)
        {
            //negative index not allowed
            if (querypagesOffset < 0)
                querypagesOffset = 0;

            items = new List<VideoInformation>();

            webclient = new WebClient();

            // Do search, regarding offset
            for (int i = (1 + querypagesOffset); i <= (querypages + querypagesOffset); i++)
            {
                // Search address
                string html = webclient.DownloadString(YtQueryUrl + querystring + "&page=" + i);

                //extract information from page
                ProcessPage(html);
            }

            return items;
        }


        private void ProcessPage(string htmlPage)
        {
            MatchCollection result = Regex.Matches(htmlPage, Pattern, RegexOptions.Singleline);

            for (int ctr = 0; ctr <= result.Count - 1; ctr++)
            {
                if (result[ctr].Value.Contains("yt-uix-button-subscription-container\">") ||
                    result[ctr].Value.Contains("\"instream\":true"))
                    continue; // Don't add to the list of search results if the value is a channel or live stream.

                // Title
                title = result[ctr].Groups[1].Value;

                // Author
                author = VideoItemHelper.cull(result[ctr].Value, "/user/", "class").Replace('"', ' ').TrimStart()
                    .TrimEnd();
                if (string.IsNullOrEmpty(author))
                {
                    author = VideoItemHelper.cull(result[ctr].Value, " >", "</a>");
                    if (string.IsNullOrEmpty(author))
                        noAuthor = true;
                }

                // Description
                description = VideoItemHelper.cull(result[ctr].Value, "dir=\"ltr\" class=\"yt-uix-redirect-link\">",
                    "</div>");
                if (string.IsNullOrEmpty(description))
                {
                    description = VideoItemHelper.cull(result[ctr].Value,
                        "<div class=\"yt-lockup-description yt-ui-ellipsis yt-ui-ellipsis-2\" dir=\"ltr\">", "</div>");
                    if (string.IsNullOrEmpty(description))
                        noDesc = true;
                }

                // Duration
                duration = VideoItemHelper
                    .cull(VideoItemHelper.cull(result[ctr].Value, "id=\"description-id-", "span"), ": ", "<")
                    .Replace(".", "");

                // Url
                url = string.Concat(YtWatchUrl, VideoItemHelper.cull(result[ctr].Value, "watch?v=", "\""));

                // Thumbnail
                thumbnail = YtThumbnailUrl + VideoItemHelper.cull(result[ctr].Value, "watch?v=", "\"") +
                            "/mqdefault.jpg";

                // View Count
                {
                    string strView = VideoItemHelper.cull(result[ctr].Value, "</li><li>", "</li></ul></div>");
                    if (!string.IsNullOrEmpty(strView) && !string.IsNullOrWhiteSpace(strView))
                    {
                        string[] strParsedArr =
                            strView.Split(new string[] {" "}, StringSplitOptions.RemoveEmptyEntries);

                        string parsedText = strParsedArr[0];
                        parsedText = parsedText.Trim().Replace(",", ".");

                        viewCount = parsedText;
                    }
                }

                // Remove playlists
                if (title != "__title__" && duration != "")
                {
                    // Add item to list
                    items.Add(new VideoInformation()
                    {
                        Title = title, Author = author, Description = description, Duration = duration, Url = url,
                        Thumbnail = thumbnail, NoAuthor = noAuthor, NoDescription = noDesc, ViewCount = viewCount
                    });
                }

                // Reset values to default for next loop.
                noAuthor = false;
                noDesc = false;
            }
        }
    }
}