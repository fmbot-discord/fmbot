using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FMBot.Bot.Attributes;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Resources;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Commands.LastFM
{
    public class CrownCommands : ModuleBase
    {
        private readonly AdminService _adminService;
        private readonly CrownService _crownService;
        private readonly GuildService _guildService;
        private readonly IPrefixService _prefixService;
        private readonly UserService _userService;

        private readonly EmbedAuthorBuilder _embedAuthor;
        private readonly EmbedBuilder _embed;
        private readonly EmbedFooterBuilder _embedFooter;

        public CrownCommands(CrownService crownService, GuildService guildService, IPrefixService prefixService, UserService userService, AdminService adminService)
        {
            this._crownService = crownService;
            this._guildService = guildService;
            this._prefixService = prefixService;
            this._userService = userService;
            this._adminService = adminService;

            this._embedAuthor = new EmbedAuthorBuilder();
            this._embed = new EmbedBuilder()
                .WithColor(DiscordConstants.LastFmColorRed);
            this._embedFooter = new EmbedFooterBuilder();
        }


        [Command("crowns", RunMode = RunMode.Async)]
        [Summary("Shows you your crowns")]
        [Alias("cws")]
        [UsernameSetRequired]
        public async Task UserCrownsAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var userCrowns = await this._crownService.GetCrownsForUser(guild, userSettings.UserId);

            this._embed.WithTitle($"Crowns for {userTitle}");

            if (!userCrowns.Any())
            {
                this._embed.WithDescription($"You don't have any crowns yet. Use whoknows to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var embedDescription = new StringBuilder();
            for (var index = 0; index < userCrowns.Count && index < 15; index++)
            {
                var userCrown = userCrowns[index];
                embedDescription.AppendLine($"{index + 1}. **{userCrown.ArtistName}** - **{userCrown.CurrentPlaycount}** plays (claimed {StringExtensions.GetTimeAgo(userCrown.Created)})");
            }

            this._embed.WithDescription(embedDescription.ToString());

            this._embed.WithFooter($"{userCrowns.Count} total crowns");

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        //[Command("crown", RunMode = RunMode.Async)]
        //[Summary("Shows who previosuly owned artist crowns")]
        //[Alias("cw")]
        [UsernameSetRequired]
        public async Task CrownAsync([Remainder] string artistValues = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var userSettings = await this._userService.GetUserSettingsAsync(this.Context.User);
            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var userTitle = await this._userService.GetUserTitleAsync(this.Context);
            var artistCrowns = await this._crownService.GetCrownsForArtist(guild, artistValues);

            if (!artistCrowns.Any())
            {
                this._embed.WithDescription($"No crown history for the artist `{artistValues}`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var currentCrown = artistCrowns
                .OrderByDescending(o => o.CurrentPlaycount)
                .First();

            this._embed.WithTitle($"Crown history for {currentCrown.ArtistName}");

            var embedDescription = new StringBuilder();

            this._embed.WithDescription(embedDescription.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("crownleaderboard", RunMode = RunMode.Async)]
        [Summary("Shows users with the most crowns in your server")]
        [Alias("cwlb", "crownlb", "cwleaderboard")]
        [UsernameSetRequired]
        public async Task CrownLeaderboardAsync()
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("This command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }
            if (guild.CrownsDisabled == true)
            {
                await ReplyAsync("Crown functionality has been disabled in this server.");
                this.Context.LogCommandUsed(CommandResponse.Disabled);
                return;
            }

            var topCrownUsers = await this._crownService.GetTopCrownUsersForGuild(guild);

            if (!topCrownUsers.Any())
            {
                this._embed.WithDescription($"No top crown users in this server. Use whoknows to start getting crowns!");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            var embedDescription = new StringBuilder();

            for (var index = 0; index < topCrownUsers.Count && index < 15; index++)
            {
                var crownUser = topCrownUsers[index];

                var guildUser = guild
                    .GuildUsers
                    .FirstOrDefault(f => f.UserId == crownUser.Key);

                string name = null;

                if (guildUser != null)
                {
                    name = guildUser.UserName;
                }

                embedDescription.AppendLine($"{index + 1}. **{name ?? crownUser.First().User.UserNameLastFM}** - **{crownUser.Count()}** crowns");
            }

            this._embed.WithTitle($"Users with most crowns in {this.Context.Guild.Name}");

            this._embed.WithDescription(embedDescription.ToString());

            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }


        [Command("killcrown", RunMode = RunMode.Async)]
        [Summary("Removes all crowns from a specific artist")]
        [Alias("kcw", "kcrown", "killcw")]
        public async Task KillCrownAsync([Remainder] string artistValues = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (string.IsNullOrWhiteSpace(artistValues))
            {
                await ReplyAsync("Please enter the artist you want to remove all crowns for.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var artistCrowns = await this._crownService.GetCrownsForArtist(guild, artistValues);

            if (!artistCrowns.Any())
            {
                this._embed.WithDescription($"No crowns found for the artist `{artistValues}`");
                await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
                return;
            }

            await this._crownService.RemoveCrowns(artistCrowns);

            this._embed.WithDescription($"All crowns for `{artistValues}` have been removed.");
            await this.Context.Channel.SendMessageAsync("", false, this._embed.Build());
            this.Context.LogCommandUsed();
        }

        [Command("togglecrowns", RunMode = RunMode.Async)]
        [Summary("Toggles crowns for your server.")]
        [Alias("togglecrown")]
        public async Task ToggleCrownsAsync([Remainder] string confirmation = null)
        {
            if (this._guildService.CheckIfDM(this.Context))
            {
                await ReplyAsync("Command is not supported in DMs.");
                this.Context.LogCommandUsed(CommandResponse.NotSupportedInDm);
                return;
            }

            var prfx = this._prefixService.GetPrefix(this.Context.Guild?.Id) ?? ConfigData.Data.Bot.Prefix;
            var guild = await this._guildService.GetGuildAsync(this.Context.Guild.Id);

            if (guild == null)
            {
                await ReplyAsync("This server hasn't been stored yet.\n" +
                                 $"Please run `{prfx}index` to store this server.");
                this.Context.LogCommandUsed(CommandResponse.IndexRequired);
                return;
            }

            var serverUser = (IGuildUser)this.Context.Message.Author;
            if (!serverUser.GuildPermissions.BanMembers && !serverUser.GuildPermissions.Administrator &&
                !await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Admin))
            {
                await ReplyAsync(
                    "You are not authorized to use this command. Only users with the 'Ban Members' permission, server admins or FMBot admins can use this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
                return;
            }

            if (guild.CrownsDisabled != true && (confirmation == null || confirmation.ToLower() != "confirm"))
            {
                await ReplyAsync($"Disabling crowns will remove all existing crowns and crown history for this server.\n" +
                                 $"Type `{prfx}togglecrowns confirm` to confirm.");
                this.Context.LogCommandUsed(CommandResponse.WrongInput);
                return;
            }

            var crownsDisabled = await this._guildService.ToggleCrownsAsync(this.Context.Guild);

            if (crownsDisabled == true)
            {
                await this._crownService.RemoveAllCrownsFromGuild(guild);
                await ReplyAsync("All crowns have been removed and crowns have been disabled for this server.");
            }
            else
            {
                await ReplyAsync($"Crowns have been enabled for this server.");
            }

            this.Context.LogCommandUsed();
        }
    }
}
