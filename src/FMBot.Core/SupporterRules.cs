using FMBot.Domain.Models;

namespace FMBot.Core;

public static class SupporterRules
{
    public static bool IsSupporter(UserType? userType)
    {
        return userType != null && userType != UserType.User;
    }
}
