using System.Collections.Generic;
using FMBot.Bot.Handlers;
using NetCord;
using NetCord.Gateway;
using NetCord.Gateway.JsonModels;
using NetCord.JsonModels;
using NetCord.Rest;
using NUnit.Framework;

namespace FMBot.Tests;

public class LeanGatewayClientCacheTests
{
    private const ulong GuildId = 100;
    private const ulong ClientId = 1;

    private static readonly RestClient Rest = new();

    private static JsonPresence Presence() => new()
    {
        User = new JsonUser { Id = 5, Username = "u" },
        GuildId = GuildId,
    };

    private static (IGatewayClientCache cache, Guild guild) Build()
    {
        var cache = LeanGatewayClientCacheProvider.Instance.Create(ClientId, Rest);
        var guild = new Guild(new JsonGuild
        {
            Id = GuildId,
            Name = "test",
            OwnerId = ClientId,
            Roles = [],
            Emojis = [],
            Stickers = [],
            Features = [],
            VoiceStates = [],
            Users = [],
            Channels = [],
            ActiveThreads = [],
            Presences = [Presence()],
            StageInstances = [],
            ScheduledEvents = [],
            PreferredLocale = "en-US",
        }, ClientId, Rest, cache);
        return (cache.CacheGuild(guild), guild);
    }

    [Test]
    public void EveryMutationReturnsTheWrapperNotTheInnerCache()
    {
        var (cache, guild) = Build();

        IGatewayClientCache[] results =
        [
            cache.CacheGuild(guild),
            cache.CacheGuildThread(GuildThread.CreateFromJson(new JsonChannel
            {
                Id = 1, Type = ChannelType.PublicGuildThread, GuildId = GuildId, ParentId = 10,
            }, Rest)),
            cache.SyncGuildActiveThreads(GuildId, null, cache.CreateDictionary<JsonChannel, ulong, GuildThread>([], t => t.Id, t => GuildThread.CreateFromJson(t, Rest))),
            cache.SyncGuilds([GuildId]),
            cache.RemoveGuildThread(GuildId, 1),
            cache.CachePresences(GuildId, []),
            cache.SyncGuildEmojis(GuildId, new Dictionary<ulong, GuildEmoji>()),
            cache.SyncGuildStickers(GuildId, new Dictionary<ulong, GuildSticker>()),
            cache.CacheCurrentUser(new CurrentUser(new JsonUser { Id = ClientId, Username = "bot" }, Rest)),
            cache.CacheGuildUsers(GuildId, []),
            cache.CacheRole(new Role(new JsonRole { Id = 7, Name = "r" }, GuildId, Rest)),
            cache.CacheGuildChannel(IGuildChannel.CreateFromJson(new JsonChannel
            {
                Id = 10, Type = ChannelType.TextGuildChannel, GuildId = GuildId,
            }, GuildId, Rest)),
            cache.CachePresence(new Presence(Presence(), GuildId, Rest)),
            cache.RemoveGuildUser(GuildId, 5),
            cache.RemoveRole(GuildId, 7),
            cache.RemoveGuildChannel(GuildId, 10),
            cache.RemoveVoiceState(GuildId, 5),
            cache.RemoveGuildScheduledEvent(GuildId, 1),
            cache.RemoveStageInstance(GuildId, 1),
            cache.RemoveGuild(GuildId),
        ];

        Assert.That(cache, Is.TypeOf<LeanGatewayClientCache>());
        Assert.That(results, Is.All.SameAs(cache));
    }

    [Test]
    public void PresencesAreNeverCached()
    {
        var (cache, guild) = Build();

        cache.CachePresence(new Presence(Presence(), GuildId, Rest));

        Assert.That(guild.Presences, Is.Empty);
    }
}
