using Discord.Interactions;
using FMBot.Bot.Services;

namespace FMBot.Bot.SlashCommands;

public class TemplateSlashCommands : InteractionModuleBase
{
    private readonly UserService _userService;

    public TemplateSlashCommands(UserService userService)
    {
        this._userService = userService;
    }


}
