namespace FMBot.Domain.Enums;

public enum UserInteractionType
{
    TextCommand = 1,
    SlashCommandGuild = 2,
    SlashCommandUser = 3,
    Component = 4,
    FlowCommand = 5,
    Modal = 6
}
