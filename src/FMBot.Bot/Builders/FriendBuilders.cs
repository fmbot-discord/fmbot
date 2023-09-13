using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dasync.Collections;
using Fergun.Interactive;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;

namespace FMBot.Bot.Builders;

public class FriendBuilders
{
    private readonly FriendsService _friendsService;
    private readonly UserService _userService;
    private readonly GuildService _guildService;
    private readonly LastFmRepository _lastFmRepository;
    private IUpdateService _updateService;

    public FriendBuilders(FriendsService friendsService, UserService userService, GuildService guildService, LastFmRepository lastFmRepository, IUpdateService updateService)
    {
        this._friendsService = friendsService;
        this._userService = userService;
        this._guildService = guildService;
        this._lastFmRepository = lastFmRepository;
        this._updateService = updateService;
    }

    public async Task<ResponseModel> FriendsAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        var friends = await this._friendsService.GetFriendsAsync(context.ContextUser.DiscordUserId);

        if (friends?.Any() != true)
        {
            response.Embed.WithDescription("We couldn't find any friends. To add friends:\n" +
                             $"`{context.Prefix}friendsadd {Constants.UserMentionOrLfmUserNameExample.Replace("`", "")}`");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        var guild = await this._guildService.GetGuildForWhoKnows(context.DiscordGuild.Id);

        var embedFooterText = "Amount of scrobbles of all your friends together: ";
        string embedTitle;
        if (friends.Count > 1)
        {
            embedTitle = $"Last songs for {friends.Count} friends from ";
        }
        else
        {
            embedTitle = "Last songs for 1 friend from ";
            embedFooterText = "Amount of scrobbles from your friend: ";
        }

        embedTitle += await this._userService.GetUserTitleAsync(context.DiscordGuild, context.DiscordUser);

        response.EmbedAuthor.WithName(embedTitle);
        if (!context.SlashCommand)
        {
            response.EmbedAuthor.WithIconUrl(context.DiscordUser.GetAvatarUrl());
        }
        response.EmbedAuthor.WithUrl(Constants.LastFMUserUrl + context.ContextUser.UserNameLastFM);
        response.Embed.WithAuthor(response.EmbedAuthor);

        var totalPlaycount = 0;
        var embedDescription = "";
        await friends.ParallelForEachAsync(async friend =>
        {
            var friendUsername = friend.LastFMUserName;
            var friendNameToDisplay = friendUsername;

            if (guild?.GuildUsers != null && guild.GuildUsers.Any() && friend.FriendUserId.HasValue)
            {
                var guildUser = guild.GuildUsers.FirstOrDefault(f => f.UserId == friend.FriendUserId.Value);
                if (guildUser?.UserName != null)
                {
                    friendNameToDisplay = guildUser.UserName;

                    var user = await this._userService.GetUserForIdAsync(guildUser.UserId);
                    var discordUser = await context.DiscordGuild.GetUserAsync(user.DiscordUserId);
                    if (discordUser?.Username != null)
                    {
                        friendNameToDisplay = discordUser.Nickname ?? discordUser.Username;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(friendNameToDisplay))
            {
                friendUsername = friend.LastFMUserName;
            }

            string sessionKey = null;
            if (friend.FriendUser?.UserNameLastFM != null)
            {
                friendUsername = friend.FriendUser.UserNameLastFM;
                if (!string.IsNullOrWhiteSpace(friend.FriendUser.SessionKeyLastFm))
                {
                    sessionKey = friend.FriendUser.SessionKeyLastFm;
                }
            }

            Response<RecentTrackList> tracks;

            if (friend.FriendUserId != null && friend.FriendUser?.SessionKeyLastFm != null)
            {
                tracks = await this._updateService.UpdateUserAndGetRecentTracks(friend.FriendUser);
            }
            else
            {
                tracks = await this._lastFmRepository.GetRecentTracksAsync(friendUsername, useCache: true, sessionKey: sessionKey);
            }

            string track;
            if (!tracks.Success || tracks.Content == null)
            {
                track = $"Friend could not be retrieved ({tracks.Error})";
            }
            else if (!tracks.Content.RecentTracks.Any())
            {
                track = "No scrobbles found.";
            }
            else
            {
                var lastTrack = tracks.Content.RecentTracks[0];
                track = LastFmRepository.TrackToOneLinedString(lastTrack);
                if (lastTrack.NowPlaying)
                {
                    track += " 🎶";
                }
                else if (lastTrack.TimePlayed.HasValue)
                {
                    track += $" ({StringExtensions.GetTimeAgoShortString(lastTrack.TimePlayed.Value)})";
                }

                totalPlaycount += (int)tracks.Content.TotalAmount;
            }

            embedDescription += $"**[{friendNameToDisplay}]({Constants.LastFMUserUrl}{friendUsername})** | {track}\n";
        }, maxDegreeOfParallelism: 3);

        response.EmbedFooter.WithText(embedFooterText + totalPlaycount.ToString("0"));
        response.Embed.WithFooter(response.EmbedFooter);

        response.Embed.WithDescription(embedDescription);

        return response;
    }

    public async Task<ResponseModel> FriendedAsync(ContextModel context)
    {
        var response = new ResponseModel
        {
            ResponseType = ResponseType.Embed,
        };

        if (context.DiscordGuild != null)
        {
            response.Embed.WithDescription("This command is only supported in DM");
            response.CommandResponse = CommandResponse.Error; // unsure if some other type should be used
            return response;
        }

        var friended = await this._friendsService.GetFriendedAsync(context.ContextUser.UserNameLastFM);

        if (friended?.Any() != true)
        {
            response.Embed.WithDescription("It doesn't seem like anyone's added you as a friend yet.");
            response.CommandResponse = CommandResponse.NotFound;
            return response;
        }

        response.EmbedAuthor.WithName("People who have added you as a friend in .fmbot");

        var pages = new List<PageBuilder>();

        var friendedPages = friended.ChunkBy(10);
        var counter = 1;
        var pageCounter = 1;
        foreach (var friendedPage in friendedPages)
        {
            var friendedPageString = new StringBuilder();

            foreach (var friend in friendedPage)
            {
                friendedPageString.AppendLine($"{counter}. **{friend.User.UserNameLastFM}**");
                counter++;
            }
            
            pages.Add(new PageBuilder()
                .WithDescription(friendedPageString.ToString())
                .WithAuthor(response.EmbedAuthor)
                .WithFooter($"Page {pageCounter}/{friendedPages.Count} - {friended?.Count} users"));

            pageCounter++;
        }

        response.StaticPaginator = StringService.BuildStaticPaginator(pages);
        return response;
    }
}
