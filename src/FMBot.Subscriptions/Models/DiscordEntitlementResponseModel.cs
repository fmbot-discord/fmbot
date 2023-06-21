using System.Text.Json.Serialization;

namespace FMBot.Subscriptions.Models;

public class DiscordEntitlementResponseModel
{
    [JsonPropertyName("id")]
    public ulong Id { get; set; }

    [JsonPropertyName("sku_id")]
    public ulong SkuId { get; set; }

    [JsonPropertyName("application_id")]
    public ulong ApplicationId { get; set; }

    [JsonPropertyName("user_id")]
    public ulong? UserId { get; set; }

    [JsonPropertyName("guild_id")]
    public ulong? GuildId { get; set; }

    [JsonPropertyName("promotion_id")]
    public ulong? PromotionId { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("gift_code_flags")]
    public int GiftCodeFlags { get; set; }

    [JsonPropertyName("consumed")]
    public bool Consumed { get; set; }

    [JsonPropertyName("starts_at")]
    public DateTime? StartsAt { get; set; }

    [JsonPropertyName("ends_at")]
    public DateTime? EndsAt { get; set; }

    [JsonPropertyName("subscription_id")]
    public ulong? SubscriptionId { get; set; }
}
