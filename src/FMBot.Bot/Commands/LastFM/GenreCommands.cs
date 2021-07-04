using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using Humanizer;
using Interactivity;
using Interactivity.Pagination;
using Microsoft.Extensions.Options;
using TimePeriod = FMBot.Domain.Models.TimePeriod;

namespace FMBot.Bot.Commands.LastFM
{
    public class GenreCommands : BaseCommandModule
    {
        private readonly IPrefixService _prefixService;
        private readonly UserService _userService;
        private readonly SettingService _settingService;
        private readonly LastFmRepository _lastFmRepository;
        private readonly PlayService _playService;
        private readonly GenreService _genreService;
        private readonly ArtistsService _artistsService;
        private InteractivityService Interactivity { get; }

        public GenreCommands(
            IPrefixService prefixService,
            IOptions<BotSettings> botSettings,
            UserService userService,
            SettingService settingService,
            LastFmRepository lastFmRepository,
            PlayService playService,
            InteractivityService interactivity,
            GenreService genreService,
            ArtistsService artistsService) : base(botSettings)
        {
            this._prefixService = prefixService;
            this._userService = userService;
            this._settingService = settingService;
            this._lastFmRepository = lastFmRepository;
            this._playService = playService;
            this.Interactivity = interactivity;
            this._genreService = genreService;
            this._artistsService = artistsService;
        }

        [Command("topgenres", RunMode = RunMode.Async)]
        [Summary("Shows a list of your or someone else their top genres over a certain time period.")]
        [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
        [Examples("tg", "topgenres", "tg a lfm:fm-bot", "topgenres weekly @user")]
        [Alias("gl", "tg", "genrelist", "genres", "top genres", "genreslist")]
        [UsernameSetRequired]
        [SupportsPagination]
        public async Task TopArtistsAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(extraOptions);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);

            var paginationEnabled = false;
            var pages = new List<PageBuilder>();
            var perms = await GuildService.CheckSufficientPermissionsAsync(this.Context);
            if (perms.ManageMessages)
            {
                paginationEnabled = true;
            }

            string userTitle;
            if (!userSettings.DifferentUser)
            {
                userTitle = await this._userService.GetUserTitleAsync(this.Context);
            }
            else
            {
                userTitle =
                    $"{userSettings.UserNameLastFm}, requested by {await this._userService.GetUserTitleAsync(this.Context)}";
            }

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} artist genres for {userTitle}");
            this._embedAuthor.WithUrl($"{Constants.LastFMUserUrl}{userSettings.UserNameLastFm}/library/artists?{timeSettings.UrlParameter}");

            try
            {
                var description = "";
                var footer = "";
                Response<TopArtistList> artists;

                if (!timeSettings.UsePlays && timeSettings.TimePeriod != TimePeriod.AllTime)
                {
                    artists = await this._lastFmRepository.GetTopArtistsAsync(userSettings.UserNameLastFm,
                        timeSettings.TimePeriod, 1000);

                    if (!artists.Success || artists.Content == null)
                    {
                        this._embed.ErrorResponse(artists.Error, artists.Message, this.Context);
                        this.Context.LogCommandUsed(CommandResponse.LastFmError);
                        await ReplyAsync("", false, this._embed.Build());
                        return;
                    }
                }
                else if (timeSettings.TimePeriod == TimePeriod.AllTime)
                {
                    artists = new Response<TopArtistList>
                    {
                        Content = new TopArtistList
                        {
                            TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId, true)
                        }
                    };
                }
                else
                {
                    artists = new Response<TopArtistList>
                    {
                        Content = await this._playService.GetTopArtists(userSettings.UserId,
                            timeSettings.PlayDays.GetValueOrDefault())
                    };
                }

                if (artists.Content.TopArtists.Count < 10)
                {
                    this._embed.WithDescription("Sorry, you don't have enough top artists yet to use this command.\n\n" +
                                                "Please try again later.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                var genres = await this._genreService.GetTopGenresForTopArtists(artists.Content.TopArtists);
                for (var i = 0; i < genres.Count; i++)
                {
                    var genre = genres[i];

                    description += $"{i + 1}. **{genre.GenreName.Humanize(LetterCasing.Title)}**\n";

                    var pageAmount = i + 1;
                    if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == genres.Count))
                    {
                        pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor).WithFooter(footer));
                        description = "";
                    }
                }

                if (paginationEnabled)
                {
                    var paginator = new StaticPaginatorBuilder()
                        .WithPages(pages)
                        .WithFooter(PaginatorFooter.PageNumber)
                        .WithTimoutedEmbed(null)
                        .WithCancelledEmbed(null)
                        .WithEmotes(DiscordConstants.PaginationEmotes)
                        .Build();

                    _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds), runOnGateway: false);
                }
                else
                {
                    this._embed.WithAuthor(this._embedAuthor);
                    this._embed.WithDescription(description);
                    this._embed.WithFooter(this._embedFooter);

                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show genre info due to an internal error. Please contact .fmbot staff.");
            }
        }

        [Command("genre", RunMode = RunMode.Async)]
        [Summary("Shows your top artists for a specific genre")]
        [Options(Constants.CompactTimePeriodList, Constants.UserMentionExample)]
        [Examples("genre", "genres hip hop, electronic", "g", "genre Indie Soul")]
        [Alias("genreinfo", "genres", "gi", "g")]
        [UsernameSetRequired]
        [SupportsPagination]
        public async Task GenreInfoAsync([Remainder] string genreOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);

            _ = this.Context.Channel.TriggerTypingAsync();

            try
            {
                List<string> genres;
                if (string.IsNullOrWhiteSpace(genreOptions))
                {
                    var recentScrobbles = await this._lastFmRepository.GetRecentTracksAsync(contextUser.UserNameLastFM, 1, true, contextUser.SessionKeyLastFm);

                    if (await GenericEmbedService.RecentScrobbleCallFailedReply(recentScrobbles, contextUser.UserNameLastFM, this.Context))
                    {
                        return;
                    }

                    genres = await this._genreService.GetGenresForArtist(recentScrobbles.Content.RecentTracks.First().ArtistName, contextUser.UserId);

                    if (!genres.Any())
                    {
                        var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);
                        this._embed.WithDescription(
                            "Sorry, we don't have any registered genres for the artist you're currently listening to.\n\n" +
                            $"Please try again later or manually enter a genre (example: `{prfx}genre hip hop`)");
                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                        this.Context.LogCommandUsed(CommandResponse.NotFound);
                        return;
                    }

                }
                else
                {
                    genres = SettingService.GetGenres(genreOptions);
                }

                var topArtists = await this._artistsService.GetUserAllTimeTopArtists(contextUser.UserId, true);
                if (topArtists.Count < 100)
                {
                    this._embed.WithDescription("Sorry, you don't have enough top artists yet to use this command.\n\n" +
                                                "Please try again later.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NoScrobbles);
                    return;
                }

                var genresWithArtists = await this._genreService.GetArtistsForGenres(genres, topArtists);

                if (!genresWithArtists.Any())
                {
                    this._embed.WithDescription("Sorry, we couldn't find any top artists for your selected genres.");
                    await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                    return;
                }

                var userTitle = await this._userService.GetUserTitleAsync(this.Context);

                if (genresWithArtists.Count > 1)
                {
                    this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                    this._embedAuthor.WithName($"Genre top artist overview for {userTitle}");
                    this._embed.WithAuthor(this._embedAuthor);

                    foreach (var genre in genresWithArtists)
                    {
                        var genreDescription = "";

                        for (var i = 0; i < genre.Artists.Count && i < 10; i++)
                        {
                            var genreArtist = genre.Artists[i];

                            genreDescription += $"{i + 1}. **{genreArtist.ArtistName.Humanize(LetterCasing.Title)}** ({genreArtist.UserPlaycount} plays)\n";
                        }

                        this._embed.AddField($"{genre.GenreName.Humanize(LetterCasing.Title)}", genreDescription, true);
                    }
                }
                else
                {
                    var paginationEnabled = false;
                    var pages = new List<PageBuilder>();
                    var perms = await GuildService.CheckSufficientPermissionsAsync(this.Context);
                    if (perms.ManageMessages)
                    {
                        paginationEnabled = true;
                    }

                    var genre = genresWithArtists.First();

                    this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
                    this._embedAuthor.WithName($"Top '{genre.GenreName.Humanize(LetterCasing.Title)}' artists for {userTitle}");

                    var description = "";
                    for (var i = 0; i < genre.Artists.Count; i++)
                    {
                        var genreArtist = genre.Artists[i];

                        description += $"{i + 1}. **{genreArtist.ArtistName.Humanize(LetterCasing.Title)}** ({genreArtist.UserPlaycount} plays)\n";

                        var pageAmount = i + 1;
                        if (paginationEnabled && (pageAmount > 0 && pageAmount % 10 == 0 || pageAmount == genre.Artists.Count))
                        {
                            pages.Add(new PageBuilder().WithDescription(description).WithAuthor(this._embedAuthor));
                            description = "";
                        }
                    }

                    if (paginationEnabled)
                    {
                        var paginator = new StaticPaginatorBuilder()
                            .WithPages(pages)
                            .WithFooter(PaginatorFooter.PageNumber)
                            .WithTimoutedEmbed(null)
                            .WithCancelledEmbed(null)
                            .WithEmotes(DiscordConstants.PaginationEmotes)
                            .Build();

                        _ = this.Interactivity.SendPaginatorAsync(paginator, this.Context.Channel, TimeSpan.FromSeconds(DiscordConstants.PaginationTimeoutInSeconds), runOnGateway: false);
                    }
                    else
                    {
                        this._embed.WithAuthor(this._embedAuthor);
                        this._embed.WithDescription(description);
                        this._embed.WithFooter(this._embedFooter);

                        await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                    }
                }

                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                this.Context.LogCommandException(e);
                await ReplyAsync("Unable to show genre info due to an internal error. Please contact .fmbot staff.");
            }
        }
    }
}
