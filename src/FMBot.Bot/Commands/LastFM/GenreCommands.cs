using System;
using System.Collections.Generic;
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
        [Examples("ta", "topartists", "ta a lfm:fm-bot", "topartists weekly @user")]
        [Alias("gl", "tg", "genrelist", "genres", "top genres", "genreslist")]
        [UsernameSetRequired]
        [SupportsPagination]
        public async Task TopArtistsAsync([Remainder] string extraOptions = null)
        {
            var contextUser = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id);

            _ = this.Context.Channel.TriggerTypingAsync();

            var timeSettings = SettingService.GetTimePeriod(extraOptions);
            var userSettings = await this._settingService.GetUser(extraOptions, contextUser, this.Context);
            long amount = SettingService.GetAmount(extraOptions);

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

            var artistsString = amount == 1 ? "artist" : "artists";

            this._embedAuthor.WithIconUrl(this.Context.User.GetAvatarUrl());
            this._embedAuthor.WithName($"Top {timeSettings.Description.ToLower()} {artistsString} for {userTitle}");
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
                            TopArtists = await this._artistsService.GetUserAllTimeTopArtists(userSettings.UserId)
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
                await ReplyAsync("Unable to show Last.fm info due to an internal error.");
            }
        }
    }
}
