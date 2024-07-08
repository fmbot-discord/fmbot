using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Bot.Attributes;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Microsoft.Extensions.Options;

namespace FMBot.Bot.TextCommands;

[Name("Owner commands")]
[Summary(".fmbot Owners Only")]
[ExcludeFromHelp]
public class OwnerCommands : BaseCommandModule
{
    private readonly AdminService _adminService;
    private readonly UserService _userService;

    public OwnerCommands(
        AdminService adminService,
        UserService userService,
        IOptions<BotSettings> botSettings) : base(botSettings)
    {
        this._adminService = adminService;
        this._userService = userService;
    }

    [Command("say"), Summary("Says something")]
    [UsernameSetRequired]
    public async Task SayAsync([Remainder] string say)
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync(say);
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
    }

    [Command("botrestart")]
    [Summary("Reboots the bot.")]
    [Alias("restart")]
    public async Task BotRestartAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            await ReplyAsync("Restarting bot...");
            this.Context.LogCommandUsed();
            Environment.Exit(1);
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can restart the bot.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
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
                await ReplyAsync(
                    "Please format your command like this: `.fmsetusertype 'discord id' 'User/Admin/Owner'`");
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

    [Command("storagecheck"), Summary("Checks how much storage is left on the server.")]
    [Alias("checkstorage", "storage")]
    [UsernameSetRequired]
    public async Task StorageCheckAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                var drives = DriveInfo.GetDrives();

                var builder = new EmbedBuilder();
                builder.WithDescription("Server Drive Info");

                foreach (var drive in drives.Where(w => w.IsReady && w.TotalSize > 10000))
                {
                    builder.AddField(drive.Name + " - " + drive.VolumeLabel + ":",
                        drive.AvailableFreeSpace.ToFormattedByteString() + " free of " +
                        drive.TotalSize.ToFormattedByteString());
                }

                await this.Context.Channel.SendMessageAsync("", false, builder.Build());
                this.Context.LogCommandUsed();
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Only .fmbot admins or owners can execute this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("serverlist"),
     Summary("Displays a list showing information related to every server the bot has joined.")]
    public async Task ServerListAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            var client = this.Context.Client as DiscordShardedClient;
            string desc = null;

            foreach (var guild in client.Guilds.OrderByDescending(o => o.MemberCount).Take(100))
            {
                desc += $"{guild.Name} - Users: {guild.MemberCount}, Owner: {guild.Owner}\n";
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

    [Command("deleteinactiveusers")]
    [Summary("Removes users who have deleted their Last.fm account from .fmbot")]
    public async Task TimerStatusAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync($"Starting removed Last.fm user deleter.");
                var deletedUsers = await this._userService.DeleteInactiveUsers();
                await ReplyAsync($"Deleted {deletedUsers} users from the database with deleted Last.fm");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can remove deleted users.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("deleteoldduplicateusers")]
    [Summary("Removes users who have deleted their Last.fm account from .fmbot")]
    public async Task DeleteOldDuplicateUsersAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            try
            {
                await ReplyAsync($"Starting inactive user deleter.");
                var deletedUsers = await this._userService.DeleteOldDuplicateUsers();
                await ReplyAsync($"Deleted {deletedUsers} inactive users from the database (test so not actually)");
            }
            catch (Exception e)
            {
                await this.Context.HandleCommandException(e);
            }
        }
        else
        {
            await ReplyAsync("Error: Insufficient rights. Only FMBot admins can remove deleted users.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }
    }

    [Command("togglespecialguild", RunMode = RunMode.Async)]
    [Summary("Makes the server a special server")]
    [GuildOnly]
    public async Task ToggleSpecialGuildAsync()
    {
        if (await this._adminService.HasCommandAccessAsync(this.Context.User, UserType.Owner))
        {
            var specialGuild = await this._adminService.ToggleSpecialGuildAsync(this.Context.Guild);

            if (specialGuild == true)
            {
                await ReplyAsync("This is now a special guild!!1!");
            }
            else
            {
                await ReplyAsync($"Not a special guild anymore :(");
            }

            this.Context.LogCommandUsed();
        }
        else
        {
            await ReplyAsync("Only .fmbot owners can execute this command.");
            this.Context.LogCommandUsed(CommandResponse.NoPermission);
        }

    }
}
