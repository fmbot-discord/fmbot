using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Extensions;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Attributes;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using static FMBot.Bot.FMBotUtil;

namespace FMBot.Bot.Commands
{
    [Summary("FMBot Owners Only")]
    public class OwnerCommands : ModuleBase
    {
        private readonly AdminService _adminService;

        public OwnerCommands(AdminService adminService)
        {
            this._adminService = adminService;
        }

        [Command("setusertype"), Summary("Sets usertype for other users")]
        [Alias("setperms")]
        [UsernameSetRequired]
        public async Task SetUserTypeAsync(string userId = null, string userType = null)
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                if (userId == null || userType == null || userId == "help")
                {
                    await ReplyAsync("Please format your command like this: `.fmsetusertype 'discord id' 'User/Admin/Owner'`");
                    this.Context.LogCommandUsed(CommandResponse.Help);
                    return;
                }

                if (!Enum.TryParse(userType, true, out UserType userTypeEnum))
                {
                    await ReplyAsync("Invalid usertype. Please use 'User', 'Contributor', 'Admin', or 'Owner'.");
                    this.Context.LogCommandUsed(CommandResponse.WrongInput);
                    return;
                }

                if (await this._adminService.SetUserTypeAsync(ulong.Parse(userId), userTypeEnum))
                {
                    await ReplyAsync("You got it. User perms changed.");
                    this.Context.LogCommandUsed();
                }
                else
                {
                    await ReplyAsync("Setting user failed. Are you sure the user exists?");
                    this.Context.LogCommandUsed(CommandResponse.NotFound);
                }
            }
            else
            {
                await ReplyAsync("Error: Insufficient rights. Only FMBot owners can change your usertype.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }


        [Command("removereadonly"), Summary("Removes read only on all directories.")]
        [Alias("readonlyfix")]
        [UsernameSetRequired]
        public async Task RemoveReadOnlyAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                try
                {
                    if (Directory.Exists(GlobalVars.CacheFolder))
                    {
                        DirectoryInfo users = new DirectoryInfo(GlobalVars.CacheFolder);
                    }

                    await ReplyAsync("Removed read only on all directories.");
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Unable to remove read only on all directories due to an internal error.");
                }
            }
        }

        [Command("storagecheck"), Summary("Checks how much storage is left on the server.")]
        [Alias("checkstorage", "storage")]
        [UsernameSetRequired]
        public async Task StorageCheckAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                try
                {
                    DriveInfo[] drives = DriveInfo.GetDrives();

                    EmbedBuilder builder = new EmbedBuilder();
                    builder.WithDescription("Server Drive Info");

                    foreach (DriveInfo drive in drives.Where(w => w.IsReady))
                    {
                        builder.AddField(drive.Name + " - " + drive.VolumeLabel + ":", drive.AvailableFreeSpace.ToFormattedByteString() + " free of " + drive.TotalSize.ToFormattedByteString());
                    }

                    await this.Context.Channel.SendMessageAsync("", false, builder.Build());
                    this.Context.LogCommandUsed();
                }
                catch (Exception e)
                {
                    this.Context.LogCommandException(e);
                    await ReplyAsync("Unable to delete server drive info due to an internal error.");
                }
            }
            else
            {
                await ReplyAsync("Only .fmbot admins or owners can execute this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("serverlist"), Summary("Displays a list showing information related to every server the bot has joined.")]
        public async Task ServerListAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                var client = this.Context.Client as DiscordShardedClient;
                string desc = null;

                foreach (SocketGuild guild in client.Guilds.OrderByDescending(o => o.MemberCount).Take(100))
                {
                    desc += $"{guild.Name} - Users: {guild.Users.Count()}, Owner: {guild.Owner.ToString()}\n";
                }

                if (!string.IsNullOrWhiteSpace(desc))
                {
                    string[] descChunks = desc.SplitByMessageLength().ToArray();
                    foreach (string chunk in descChunks)
                    {
                        await this.Context.User.SendMessageAsync(chunk);
                    }
                }

                await this.Context.Channel.SendMessageAsync("Check your DMs!");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Only .fmbot owners can execute this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }

        [Command("fixvalues"), Summary("Fixes postgresql index values")]
        public async Task FixIndexValuesAsync()
        {
            if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
            {
                await _adminService.FixValues();
                await ReplyAsync("Postgres values have been fixed.");
                this.Context.LogCommandUsed();
            }
            else
            {
                await ReplyAsync("Only .fmbot owners can execute this command.");
                this.Context.LogCommandUsed(CommandResponse.NoPermission);
            }
        }
    }
}
