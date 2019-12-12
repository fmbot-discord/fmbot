using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;

namespace FMBot.Bot.Commands.LastFM
{
    public class AlbumCommands : ModuleBase
    {
        private readonly EmbedBuilder _embed;
        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedFooterBuilder _embedFooter;
        private readonly GuildService _guildService = new GuildService();
        private readonly LastFMService _lastFmService = new LastFMService();
        private readonly Logger.Logger _logger;

        private readonly UserService _userService = new UserService();

        public AlbumCommands(Logger.Logger logger)
        {
            this._logger = logger;
            this._embed = new EmbedBuilder()
                .WithColor(Constants.LastFMColorRed);
            this._embedAuthor = new EmbedAuthorBuilder();
            this._embedFooter = new EmbedFooterBuilder();
        }


        private async Task UsernameNotSetErrorResponseAsync()
        {
            this._embed.UsernameNotSetErrorResponse(this.Context, this._logger);
            await ReplyAsync("", false, this._embed.Build());
        }

        private async Task<string> FindUser(string user)
        {
            if (await this._lastFmService.LastFMUserExistsAsync(user))
            {
                return user;
            }

            if (!this._guildService.CheckIfDM(this.Context))
            {
                var guildUser = await this._guildService.FindUserFromGuildAsync(this.Context, user);

                if (guildUser != null)
                {
                    var guildUserLastFm = await this._userService.GetUserSettingsAsync(guildUser);

                    return guildUserLastFm?.UserNameLastFM;
                }
            }

            return null;
        }
    }
}
