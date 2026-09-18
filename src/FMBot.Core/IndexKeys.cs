namespace FMBot.Core;

public static class IndexKeys
{
    public static string IndexConcurrencyCacheKey(int userId)
    {
        return $"index-started-{userId}";
    }
}
