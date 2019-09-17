using Discord;
using Discord.Commands;
using FMBot.Data.Entities;
using FMBot.Services;
using IF.Lastfm.Core.Api.Helpers;
using IF.Lastfm.Core.Objects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bot.Logger;
using FMBot.Bot.Configurations;
using FMBot.Bot.Resources;

namespace FMBot.Bot.Commands
{
    public class FriendsCommands : ModuleBase
    {
        private readonly Logger _logger;

        private readonly FriendsService _friendsService = new FriendsService();
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly UserService _userService = new UserService();

        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;

        public FriendsCommands(Logger logger)
        {
            _logger = logger;
            _embed = new EmbedBuilder();
            _embedAuthor = new EmbedAuthorBuilder();
            _embedFooter = new EmbedFooterBuilder();
        }

        [Command("fmfriends"), Summary("Displays a user's friends and what they are listening to.")]
        [Alias("fmrecentfriends", "fmfriendsrecent")]
        public async Task FmFriendsRecentAsync()
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            try
            {
                List<Friend> friends = await _friendsService.GetFMFriendsAsync(Context.User).ConfigureAwait(false);

                if (friends?.Any() != true)
                {
                    await ReplyAsync("We couldn't find any friends. To add friends:\n" +
                        "`.fmaddfriends 'lastfmname/discord name'`").ConfigureAwait(false);
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
                builder.Title = await _userService.GetUserTitleAsync(Context).ConfigureAwait(false);

                string amountOfScrobbles = "Amount of scrobbles of all your friends together: ";

                if (friends.Count > 1)
                {
                    builder.WithDescription("Songs from " + friends.Count + " friends");
                }
                else
                {
                    builder.WithDescription("Songs from your friend");
                    amountOfScrobbles = "Amount of scrobbles from your friend: ";
                }

                const string nulltext = "[undefined]";
                int indexval = (friends.Count - 1);
                int playcount = 0;
                List<string> failedFriends = new List<string>();

                foreach (Friend friend in friends)
                {
                    try
                    {
                        string friendusername = friend.FriendUser != null ? friend.FriendUser.UserNameLastFM : friend.LastFMUserName;

                        PageResponse<LastTrack> tracks = await _lastFmService.GetRecentScrobblesAsync(friendusername, 1).ConfigureAwait(false);

                        string TrackName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().Name) ? nulltext : tracks.FirstOrDefault().Name;
                        string ArtistName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().ArtistName) ? nulltext : tracks.FirstOrDefault().ArtistName;
                        string AlbumName = string.IsNullOrWhiteSpace(tracks.FirstOrDefault().AlbumName) ? nulltext : tracks.FirstOrDefault().AlbumName;

                        builder.AddField(friendusername + ":", TrackName + " - " + ArtistName + " | " + AlbumName);


                        if (friends.Count <= 8)
                        {
                            LastResponse<LastUser> userinfo = await _lastFmService.GetUserInfoAsync(friendusername).ConfigureAwait(false);
                            playcount += userinfo.Content.Playcount;
                        }
                    }
                    catch
                    {
                        //failedFriends.Add(friendusername);
                    }

                }

                if (friends.Count <= 8)
                {
                    EmbedFooterBuilder efb = new EmbedFooterBuilder
                    {
                        Text = amountOfScrobbles + playcount.ToString("0")
                    };
                    builder.WithFooter(efb);
                }

                //if (failedFriends.Count > 0)
                //{
                //    await ReplyAsync("Couldn't retrieve data for one or more of the following friends:\n" +
                //        string.Join(", ", friends.Select(s => s.LastFMUserName).ToArray()))
                //        .ConfigureAwait(false);
                //}

                await Context.Channel.SendMessageAsync("", false, builder.Build()).ConfigureAwait(false);
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);

                await ReplyAsync("Unable to show friends due to an internal error. Try removing all your current friends using `.fmremoveallfriends` and try again.").ConfigureAwait(false);
            }
        }

        [Command("fmaddfriends"), Summary("Adds your friends' Last.FM names.")]
        [Alias("fmfriendsset", "fmsetfriends", "fmfriendsadd", "fmaddfriend", "fmsetfriend")]
        public async Task fmfriendssetAsync([Summary("Friend names")] params string[] friends)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            try
            {
                string SelfID = Context.Message.Author.Id.ToString();

                List<string> friendList = new List<string>();
                List<string> friendNotFoundList = new List<string>();

                int friendcount = 0;

                List<Friend> existingFriends = await _friendsService.GetFMFriendsAsync(Context.User).ConfigureAwait(false);

                if (existingFriends.Count + friends.Length > 10)
                {
                    await ReplyAsync("Sorry, but you can't have more then 10 friends.").ConfigureAwait(false);
                    return;
                }

                foreach (string friend in friends)
                {
                    if (existingFriends?.Where(w => w.LastFMUserName != null).Select(s => s.LastFMUserName.ToLower()).Contains(friend.ToLower()) != true)
                    {
                        if (!_guildService.CheckIfDM(Context))
                        {
                            IGuildUser friendUser = await _guildService.FindUserFromGuildAsync(Context, friend).ConfigureAwait(false);

                            if (friendUser != null)
                            {
                                User friendUserSettings = await _userService.GetUserSettingsAsync(friendUser).ConfigureAwait(false);

                                if (friendUserSettings == null || friendUserSettings.UserNameLastFM == null)
                                {
                                    if (await _lastFmService.LastFMUserExistsAsync(friend).ConfigureAwait(false))
                                    {
                                        await _friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend).ConfigureAwait(false);
                                        friendcount++;
                                    }
                                    else
                                    {
                                        friendNotFoundList.Add(friend);
                                    }
                                }
                                else
                                {
                                    await _friendsService.AddDiscordFriendAsync(Context.User.Id.ToString(), friendUser.Id.ToString()).ConfigureAwait(false);
                                    friendcount++;
                                }
                            }
                            else
                            {
                                if (await _lastFmService.LastFMUserExistsAsync(friend).ConfigureAwait(false))
                                {
                                    await _friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend).ConfigureAwait(false);
                                    friendcount++;
                                }
                                else
                                {
                                    friendNotFoundList.Add(friend);
                                }
                            }
                        }
                        else
                        {
                            if (await _lastFmService.LastFMUserExistsAsync(friend).ConfigureAwait(false))
                            {
                                await _friendsService.AddLastFMFriendAsync(Context.User.Id.ToString(), friend).ConfigureAwait(false);
                                friendcount++;
                            }
                            else
                            {
                                friendNotFoundList.Add(friend);
                            }
                        }
                    }
                }

                if (friendcount > 1)
                {
                    await ReplyAsync("Succesfully added " + friendcount + " friends.").ConfigureAwait(false);
                }
                else if (friendcount == 1)
                {
                    await ReplyAsync("Succesfully added a friend.").ConfigureAwait(false);
                }

                if (friendNotFoundList.Count > 0)
                {
                    if (friendNotFoundList.Count > 1)
                    {
                        await ReplyAsync("Could not find " + friendNotFoundList.Count + " friends. Please ensure that you spelled their names correctly.").ConfigureAwait(false);
                    }
                    else
                    {
                        await ReplyAsync("Could not find 1 friend. Please ensure that you spelled the name correctly.").ConfigureAwait(false);
                    }
                }
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);

                int friendcount = friends.Length;

                if (friendcount > 1)
                {
                    await ReplyAsync("Unable to add " + friendcount + " friends due to an internal error.").ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error.").ConfigureAwait(false);
                }
            }
        }

        [Command("fmremovefriends"), Summary("Remove your friends' Last.FM names.")]
        [Alias("fmfriendsremove", "fmdeletefriend", "fmdeletefriends", "fmremovefriend")]
        public async Task fmfriendsremoveAsync([Summary("Friend names")] params string[] friends)
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            if (friends.Length == 0)
            {
                await ReplyAsync("Please enter at least one friend to remove.").ConfigureAwait(false);
                return;
            }

            int removedfriendcount = 0;

            try
            {
                List<Friend> existingFriends = await _friendsService.GetFMFriendsAsync(Context.User).ConfigureAwait(false);

                foreach (string friend in friends)
                {
                    if (existingFriends.Select(s => s.LastFMUserName.ToLower()).Contains(friend.ToLower()))
                    {
                        await _friendsService.RemoveLastFMFriendAsync(userSettings.UserID, friend).ConfigureAwait(false);
                        removedfriendcount++;
                    }
                }

                if (removedfriendcount > 1)
                {
                    await ReplyAsync("Succesfully removed " + removedfriendcount + " friends.").ConfigureAwait(false);
                }
                else if (removedfriendcount < 1)
                {
                    await ReplyAsync("Couldn't remove " + removedfriendcount + " friends. Please check if you spelled that all Last.FM names correct.").ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("Succesfully removed a friend.").ConfigureAwait(false);
                }

                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);

                if (friends.Length > 1)
                {
                    await ReplyAsync("Unable to remove " + friends.Length + " friends due to an internal error. Did you add anyone?").ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("Unable to add a friend due to an internal error. Did you add anyone?").ConfigureAwait(false);
                }
            }
        }

        [Command("fmremoveallfriends"), Summary("Remove all your friends")]
        [Alias("fmfriendsremoveall")]
        public async Task fmfriendsremoveallAsync()
        {
            User userSettings = await _userService.GetUserSettingsAsync(Context.User).ConfigureAwait(false);

            if (userSettings?.UserNameLastFM == null)
            {
                await UsernameNotSetErrorResponseAsync();
                return;
            }

            try
            {
                await _friendsService.RemoveAllLastFMFriendsAsync(userSettings.UserID).ConfigureAwait(false);

                await ReplyAsync("Removed all your friends.").ConfigureAwait(false);
                this._logger.LogCommandUsed(Context.Guild?.Id, Context.Channel.Id, Context.User.Id, Context.Message.Content);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, Context.Message.Content, Context.User.Username, Context.Guild?.Name, Context.Guild?.Id);

                await ReplyAsync("Unable to remove all friends due to an internal error.").ConfigureAwait(false);
            }
        }

        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.WithTitle("Error while attempting get Last.FM information");
            this._embed.WithDescription("Last.FM username has not been set. \n" +
                                        "To setup your Last.FM account with this bot, please use the `.fmset` command. \n" +
                                        $"Usage: `{ConfigData.Data.CommandPrefix}fmset username mode`\n" +
                                        "Possible modes: embedmini/embedfull/textmini/textfull");

            this._embed.WithColor(Constants.WarningColorOrange);
            await ReplyAsync("", false, this._embed.Build());
        }
    }
}
