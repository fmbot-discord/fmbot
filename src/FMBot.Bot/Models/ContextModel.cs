
using System.Collections.Generic;
using FMBot.Domain.Enums;
using FMBot.Persistence.Domain.Models;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.Commands;
using NetCord.Services.ComponentInteractions;
using GuildUser = NetCord.GuildUser;

namespace FMBot.Bot.Models;

public class ContextModel
{
    public ContextModel(CommandContext context, string prefix, User contextUser = null)
    {
        this.Prefix = prefix;
        this.NumberFormat = contextUser?.NumberFormat ?? NumberFormat.NoSeparator;
        this.DiscordGuild = context.Guild;
        if (context.Guild != null)
        {
            this.CachedGuildUsers = context.Client.Cache.Guilds[context.Guild.Id]?.Users;
        }
        this.DiscordChannel = context.Channel;
        this.DiscordUser = context.User;
        this.ContextUser = contextUser;
        this.SlashCommand = false;
        this.InteractionId = context.Message.Id;
        this.ReferencedMessage = context.Message.ReferencedMessage;
    }

    public ContextModel(ApplicationCommandContext context, User contextUser = null, NetCord.User discordContextUser = null)
    {
        this.Prefix = "/";
        this.NumberFormat = contextUser?.NumberFormat ?? NumberFormat.NoSeparator;
        this.DiscordGuild = context.Guild;
        if (context.Guild != null)
        {
            this.CachedGuildUsers = context.Client.Cache.Guilds[context.Guild.Id]?.Users;
        }
        this.DiscordChannel = context.Channel;
        this.DiscordUser = discordContextUser ?? context.User;
        this.ContextUser = contextUser;
        this.SlashCommand = true;
        this.InteractionId = context.Interaction.Id;
    }

    public ContextModel(ComponentInteractionContext context, User contextUser = null)
    {
        this.Prefix = "/";
        this.NumberFormat = contextUser?.NumberFormat ?? NumberFormat.NoSeparator;
        this.DiscordGuild = context.Guild;
        if (context.Guild != null)
        {
            this.CachedGuildUsers = context.Client.Cache.Guilds[context.Guild.Id]?.Users;
        }
        this.DiscordChannel = context.Channel;
        this.DiscordUser = context.User;
        this.ContextUser = contextUser;
        this.SlashCommand = true;
        this.InteractionId = context.Interaction.Id;
    }

    public bool SlashCommand { get; set; }

    public string Prefix { get; set; }

    public NetCord.Gateway.Guild DiscordGuild { get; set; }
    public IReadOnlyDictionary<ulong, GuildUser> CachedGuildUsers { get; set; }

    public NetCord.Channel DiscordChannel { get; set; }
    public NetCord.User DiscordUser { get; set; }

    public ulong InteractionId { get; set; }

    public RestMessage ReferencedMessage { get; set; }

    public StringMenuProperties SelectMenu { get; set; }

    public User ContextUser { get; set; }

    public NumberFormat NumberFormat { get; set; }
}
