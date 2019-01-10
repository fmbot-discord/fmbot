using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Data.Entities;
using FMBot.Services;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    public class FriendsCommands : ModuleBase
    {
        private readonly FriendsService friendsService = new FriendsService();

        private readonly GuildService guildService = new GuildService();

        private readonly LastFMService lastFMService = new LastFMService();

        private readonly UserService userService = new UserService();


        [Command("fmfriends"), Summary("Displays a user's friends and what they are listening to.")]
        [Alias("fmrecentfriends", "fmfriendsrecent")]
        public async Task fmfriendsrecentAsync(IUser user = null)
        {
            User userSettings = await userService.GetUserSettingsAsync(Context.User);

            if (userSettings == null || userSettings.UserNameLastFM == null)
            {
                await ReplyAsync("Your LastFM username has not been set. Please set your username using the `.fmset 'username' 'embedfull/embedmini/textfull/textmini'` command.");
                return;
            }

            try
            {
                List<Friend> friends = await friendsService.GetFMFriendsAsync(Context.User);

                if (friends == null || !friends.Any())
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                        "`.fmaddfriends 'lastfmname/discord name'`");
                    return;
                }

                EmbedAuthorBuilder eab = new EmbedAuthorBuilder
                {
                    IconUrl = Context.User.GetAvatarUrl()
                };

                EmbedBuilder builder = new EmbedBuilder();
                builder.WithAuthor(eab);
                string URI = "https://www.last.fm/user/" + userSettings.UserNameLastFM;
                builder.WithUrl(URI);
                builder.Title = await userService.GetUserTitleAsync(Context);

                string amountOfScrobbles = "Amount of scrobbles of all your friends together: ";

                if (friends.Count() > 1)
                {
                    builder.WithDescription("Songs from " + friends.Count() + " friends");
                }
                else
                {
                    builder.WithDescription("Songs from your friend");
                    amountOfScrobbles = "Amount of scrobbles from your friend: ";
                }

                string nulltext = "[undefined]";
                int indexval = (friends.Count() - 1);
                int playcount = 0;

                foreach (Friend friend in friends)
                {
                    string friendusername = friend.FriendUser != null ? friend.FriendUser.UserNameLastFM : friend.LastFMUserName;


                    PageResponse<LastTrack> tracks = await lastFMService.GetRecentScrobblesAsync(friendusername, 1);

                    string TrackName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().Name) ? nulltext : tracks.FirstOrDefault().Name;
                    string ArtistName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().ArtistName) ? nulltext : tracks.FirstOrDefault().ArtistName;
                    string AlbumName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().AlbumName) ? nulltext : tracks.FirstOrDefault().AlbumName;

                    builder.AddField(friendusername + ":", TrackName + " - " + ArtistName + " | " + AlbumName);

                    if (friends.Count() <= 8)
                    {
                        LastResponse<LastUser> userinfo = await lastFMService.GetUserInfoAsync(friendusername);
                        playcount = playcount + userinfo.Content.Playcount;
                    }
                }

                if (friends.Count() <= 8)
                {
                    EmbedFooterBuilder efb = new EmbedFooterBuilder
                    {
                        Text = amountOfScrobbles + playcount.ToString("0")
                    };
                    builder.WithFooter(efb);
                }

                await Context.Channel.SendMessageAsync("", false, builder.Build());


            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                await ReplyAsync("Unable to show friends due to an internal error.");
            }
        }


        [Command("fmaddfriends"), Summary("Adds your friends' Last.FM names.")]
        [Alias("fmfriendsset", "fmsetfriends", "fmfriendsadd", "fmaddfriend")]
        public async Task fmfriendssetAsync([Summary("Friend names")] params string[] friends)
        {
            try
            {
                string SelfID = Context.Message.Author.Id.ToString();

                JsonCfg.ConfigJson cfgjson = await JsonCfg.GetJSONDataAsync();

                List<string> friendList = new List<string>();
                List<string> friendNotFoundList = new List<string>();

                int friendcount = 0;

                List<Friend> existingFriends = await friendsService.GetFMFriendsAsync(Context.User);

                foreach (string friend in friends)
                {
                    if (!existingFriends.Select(s => s.LastFMUserName).Contains(friend))
                    {
                        if (!guildService.CheckIfDM(Context))
                        {
                            IGuildUser friendUser = await guildService.FindUserFromGuildAsync(Context, friend);

                            if (friendUser != null)
                            {
                                Data.Entities.User friendUserSettings = await userService.GetUserSettingsAsync(friendUser);

                                if (friendUserSettings == null || friendUserSettings.UserNameLastFM == null)
                                {
                                    if (await lastFMService.LastFMUserExistsAsync(friend))
                                    {
                                        await friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend);
                                        friendcount++;
                                    }
                                    else
                                    {
                                        friendNotFoundList.Add(friend);
                                    }
                                }
                                else
                                {
                                    await friendsService.AddDiscordFriendAsync(Context.User.Id.ToString(), friendUser.Id.ToString());
                                    friendcount++;
                                }
                            }
                            else
                            {
                                await friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend);
                                friendcount++;
                            }
                        }
                        else
                        {
                            await friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend);
                            friendcount++;
                        }
                    }
                }

                if (friendcount > 1)
                {
                    await ReplyAsync("Succesfully added " + friendcount + " friends.");
                }
                else if (friendcount < 1)
                {
                    await ReplyAsync("Didn't add  " + friendcount + " friends. Maybe they are already on your friendlist.");
                }
                else
                {
                    await ReplyAsync("Succesfully added a friend.");
                }

                if (friendNotFoundList.Any())
                {
                    if (friendNotFoundList.Count > 1)
                    {
                        await ReplyAsync("Could not find " + friendNotFoundList.Count + " friends. Please ensure that you spelled their names correctly.");
                    }
                    else
                    {
                        await ReplyAsync("Could not find 1 friend. Please ensure that you spelled the name correctly.");
                    }
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                int friendcount = friends.Count();

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to add " + friendcount + " friends due to an internal error.");
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error.");
                }
            }
        }

        [Command("fmremovefriends"), Summary("Remove your friends' Last.FM names.")]
        [Alias("fmfriendsremove")]
        public async Task fmfriendsremoveAsync([Summary("Friend names")] params string[] friends)
        {
            if (!friends.Any())
            {
                await ReplyAsync("Please enter at least one friend to remove.");
                return;
            }

            try
            {
                string SelfID = Context.Message.Author.Id.ToString();

                int friendcount = DBase.RemoveFriendsEntry(SelfID, friends);

                if (friendcount > 1)
                {
                    await ReplyAsync("Succesfully removed " + friendcount + " friends.");
                }
                else if (friendcount < 1)
                {
                    await ReplyAsync("Couldn't remove " + friendcount + " friends. Please check if the user is on your friendslist.");
                }
                else
                {
                    await ReplyAsync("Succesfully removed a friend.");
                }
            }
            catch (Exception e)
            {
                DiscordSocketClient disclient = Context.Client as DiscordSocketClient;
                ExceptionReporter.ReportException(disclient, e);

                int friendcount = friends.Count();

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to remove " + friendcount + " friends due to an internal error. Did you add anyone?");
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error. Did you add anyone?");
                }
            }
        }


    }
}
