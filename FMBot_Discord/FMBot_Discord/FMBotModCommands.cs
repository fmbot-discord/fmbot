using Discord.Commands;
using Discord.WebSocket;
using System.Threading.Tasks;
using static FMBot_Discord.FMBotUtil;

namespace FMBot_Discord
{
    public class FMBotModCommands : ModuleBase
    {
        private readonly CommandService _service;

        public FMBotModCommands(CommandService service)
        {
            _service = service;
        }

        [Command("fmblacklistadd"), Summary("Adds a user to a serverside blacklist - Server Admins/Mods only")]
        public async Task fmblacklistaddAsync(SocketGuildUser user = null)
        {
            var DiscordUser = Context.Message.Author as SocketGuildUser;

            if (DiscordUser.GuildPermissions.BanMembers)
            {
                if (user == null)
                {
                    await ReplyAsync("Please specify what user you want to add to the blacklist.");
                }
                else if (user == Context.Message.Author)
                {
                    await ReplyAsync("You cannot blacklist yourself!");
                }
                else if (user.Id == user.Guild.OwnerId)
                {
                    await ReplyAsync("You cannot blacklist the owner!");
                }

                string UserID = user.Id.ToString();
                string ServerID = user.Guild.Id.ToString();

                bool blacklistresult = DBase.AddToBlacklist(UserID, ServerID);

                if (blacklistresult == true)
                {
                    if (string.IsNullOrWhiteSpace(user.Nickname))
                    {
                        await ReplyAsync("Added " + user.Username + " to the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("Added " + user.Nickname + " (" + user.Username + ") to the blacklist.");
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(user.Nickname))
                    {
                        await ReplyAsync("You have already added " + user.Username + " to the blacklist or the blacklist does not exist for this user.");
                    }
                    else
                    {
                        await ReplyAsync("You have already added " + user.Nickname + " (" + user.Username + ") to the blacklist or the blacklist does not exist for this user.");
                    }
                }
            }
            else
            {
                await ReplyAsync("You must have the 'Ban Members' permission in order to use this command.");
            }
        }

        [Command("fmblacklistremove"), Summary("Removes a user from a serverside blacklist - Server Admins/Mods only")]
        public async Task fmblacklistremoveAsync(SocketGuildUser user = null)
        {
            var DiscordUser = Context.Message.Author as SocketGuildUser;

            if (DiscordUser.GuildPermissions.BanMembers)
            {
                if (user == null)
                {
                    await ReplyAsync("Please specify what user you want to remove from the blacklist.");
                }
                else if (user == Context.Message.Author)
                {
                    await ReplyAsync("You cannot remove yourself!");
                }
                else if (user.Id == user.Guild.OwnerId)
                {
                    await ReplyAsync("You cannot remove the owner from the blacklist!");
                }

                string UserID = user.Id.ToString();
                string ServerID = user.Guild.Id.ToString();

                bool blacklistresult = DBase.RemoveFromBlacklist(UserID, ServerID);

                if (blacklistresult == true)
                {
                    if (string.IsNullOrWhiteSpace(user.Nickname))
                    {
                        await ReplyAsync("Removed " + user.Username + " from the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("Removed " + user.Nickname + " (" + user.Username + ") from the blacklist.");
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(user.Nickname))
                    {
                        await ReplyAsync("You have already removed " + user.Username + " from the blacklist.");
                    }
                    else
                    {
                        await ReplyAsync("You have already removed " + user.Nickname + " (" + user.Username + ") from the blacklist.");
                    }
                }
            }
            else
            {
                await ReplyAsync("You must have the 'Ban Members' permission in order to use this command.");
            }
        }
    }
}
