using Discord;
using Discord.Commands;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models;

public class ContextModel
{
    public ContextModel(ICommandContext context, string prefix, User contextUser = null)
    {
        this.Prefix = prefix;
        this.DiscordGuild = context.Guild;
        this.DiscordChannel = context.Channel;
        this.DiscordUser = context.User;
        this.ContextUser = contextUser;
        this.SlashCommand = false;
        this.InteractionId = context.Message.Id;
        this.ReferencedMessage = context.Message.ReferencedMessage;
    }

    public ContextModel(IInteractionContext context, User contextUser = null, IUser discordContextUser = null)
    {
        this.Prefix = "/";
        this.DiscordGuild = context.Guild;
        this.DiscordChannel = context.Channel;
        this.DiscordUser = discordContextUser ?? context.User;
        this.ContextUser = contextUser;
        this.SlashCommand = true;
        this.InteractionId = context.Interaction.Id;
    }

    public bool SlashCommand { get; set; }

    public string Prefix { get; set; }

    public IGuild DiscordGuild { get; set; }
    public IChannel DiscordChannel { get; set; }
    public IUser DiscordUser { get; set; }

    public ulong InteractionId { get; set; }

    public IUserMessage ReferencedMessage { get; set; }

    public User ContextUser { get; set; }
}
