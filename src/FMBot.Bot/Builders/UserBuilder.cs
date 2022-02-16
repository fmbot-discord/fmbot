using FMBot.Bot.Services;

namespace FMBot.Bot.Builders;

public class UserBuilder
{
    private readonly UserService _userService;

    public UserBuilder(UserService userService)
    {
        this._userService = userService;
    }
}
