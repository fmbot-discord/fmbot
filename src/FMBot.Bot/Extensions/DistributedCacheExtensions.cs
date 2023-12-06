using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Bot.Extensions;

public static class DistributedCacheExtensions
{
    public static void Set<T>(this IDistributedCache cache, string key, T value, DistributedCacheEntryOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, GetJsonSerializerOptions()));
        cache.Set(key, bytes, options);
    }

    public static Task SetAsync<T>(this IDistributedCache cache, string key, T value, DistributedCacheEntryOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, GetJsonSerializerOptions()));
        return cache.SetAsync(key, bytes, options);
    }

    public static T Get<T>(this IDistributedCache cache, string key)
    {
        var val = cache.Get(key);
        return val == null ? default : JsonSerializer.Deserialize<T>(val, GetJsonSerializerOptions());
    }

    public static async Task<T> GetAsync<T>(this IDistributedCache cache, string key)
    {
        var val = await cache.GetAsync(key);
        return val == null ? default : JsonSerializer.Deserialize<T>(val, GetJsonSerializerOptions());
    }

    public static bool TryGetValue<T>(this IDistributedCache cache, string key, out T? value)
    {
        var val = cache.Get(key);
        value = default;
        if (val == null) return false;
        value = JsonSerializer.Deserialize<T>(val, GetJsonSerializerOptions());
        return true;
    }

    private static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions()
        {
            PropertyNamingPolicy = null,
            WriteIndented = true,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
