using FMBot.Bot.Models;
using FMBot.Bot.Services;
using FMBot.Bot.Services.WhoKnows;
using FMBot.Domain;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Tests;

public class WhoKnowsServiceTests
{
    private const ulong DiscordGuildId = 1234567890;
    private static readonly Localizer English = new(Language.English, NumberFormat.NoSeparator);

    [OneTimeSetUp]
    public void LoadTranslations()
    {
        new LocalizationService(null!).LoadTranslations();
    }

    [TearDown]
    public void TearDown()
    {
        PublicProperties.PremiumServers.TryRemove(DiscordGuildId, out _);
    }

    private static void MakeGuildPremium()
    {
        PublicProperties.PremiumServers[DiscordGuildId] = 1;
    }

    private static Guild NewGuild() => new() { GuildId = 1, DiscordGuildId = DiscordGuildId };

    private static WhoKnowsObjectWithUser Wk(int userId, int playcount, string? lastFm = null,
        ulong[]? roles = null, DateTime? lastUsed = null, DateTime? lastMessage = null,
        PrivacyLevel privacy = PrivacyLevel.Global, string? discordName = null)
    {
        return new WhoKnowsObjectWithUser
        {
            UserId = userId,
            Playcount = playcount,
            LastFMUsername = lastFm ?? $"lastfm{userId}",
            DiscordName = discordName ?? $"user{userId}",
            Name = "Radiohead",
            Roles = roles,
            LastUsed = lastUsed ?? DateTime.UtcNow,
            LastMessage = lastMessage ?? DateTime.UtcNow,
            PrivacyLevel = privacy
        };
    }

    private static FullGuildUser Member(int userId, string? lastFm = null, bool blocked = false,
        bool selfBlocked = false, ulong[]? roles = null)
    {
        return new FullGuildUser
        {
            UserId = userId,
            UserNameLastFM = lastFm ?? $"lastfm{userId}",
            UserName = $"user{userId}",
            BlockedFromWhoKnows = blocked,
            SelfBlockFromWhoKnows = selfBlocked,
            Roles = roles
        };
    }

    private static Dictionary<int, FullGuildUser> Members(params FullGuildUser[] members) =>
        members.ToDictionary(m => m.UserId);

    private static List<int> Ids(IEnumerable<WhoKnowsObjectWithUser> users) => users.Select(u => u.UserId).ToList();

    [Test]
    public void Filter_NoRestrictions_KeepsEveryoneAndReportsRequesterPresence()
    {
        var users = new List<WhoKnowsObjectWithUser> { Wk(1, 50), Wk(2, 40) };

        var (stats, filtered) = WhoKnowsService.FilterWhoKnowsObjects(users, Members(), NewGuild(), contextUserId: 1);
        var (absentStats, _) = WhoKnowsService.FilterWhoKnowsObjects(users, Members(), NewGuild(), contextUserId: 99);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(filtered), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(stats.StartCount, Is.EqualTo(2));
            Assert.That(stats.EndCount, Is.EqualTo(2));
            Assert.That(stats.RequesterFiltered, Is.False);
            Assert.That(absentStats.RequesterFiltered, Is.Null);
        });
    }

    [Test]
    public void Filter_ActivityThreshold_RemovesStaleAndNeverSeenUsers()
    {
        var guild = NewGuild();
        guild.ActivityThresholdDays = 30;
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50, lastUsed: DateTime.UtcNow.AddDays(-5)),
            Wk(2, 40, lastUsed: DateTime.UtcNow.AddDays(-31)),
            Wk(3, 30)
        };
        users[2].LastUsed = null;

        var (stats, filtered) = WhoKnowsService.FilterWhoKnowsObjects(users, Members(), guild, contextUserId: 2);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(filtered), Is.EqualTo(new[] { 1 }));
            Assert.That(stats.ActivityThresholdFiltered, Is.EqualTo(2));
            Assert.That(stats.RequesterFiltered, Is.True);
        });
    }

    [Test]
    public void Filter_BlockedUsers_AreRemovedByIdAndByLastFmNameCaseInsensitively()
    {
        var members = Members(
            Member(1),
            Member(2, blocked: true),
            Member(3, lastFm: "AltAccount", selfBlocked: true));
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50),
            Wk(2, 40),
            Wk(99, 30, lastFm: "altaccount"),
            Wk(4, 20)
        };

        var (stats, filtered) = WhoKnowsService.FilterWhoKnowsObjects(users, members, NewGuild(), contextUserId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(filtered), Is.EqualTo(new[] { 1, 4 }));
            Assert.That(stats.BlockedFiltered, Is.EqualTo(2));
        });
    }

    [Test]
    public void Filter_BlockedUsers_StillApplyWhenOtherFiltersAreDisabled()
    {
        var guild = NewGuild();
        guild.ActivityThresholdDays = 1;
        var members = Members(Member(2, blocked: true));
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50, lastUsed: DateTime.UtcNow.AddDays(-400)),
            Wk(2, 40)
        };

        var (stats, filtered) = WhoKnowsService.FilterWhoKnowsObjects(users, members, guild, contextUserId: 1,
            filterDisabled: true);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(filtered), Is.EqualTo(new[] { 1 }));
            Assert.That(stats.ActivityThresholdFiltered, Is.Null);
            Assert.That(stats.EndCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Filter_RoleAndMessageActivityRules_OnlyApplyToPremiumGuilds()
    {
        var guild = NewGuild();
        guild.AllowedRoles = [100];
        guild.BlockedRoles = [200];
        guild.UserActivityThresholdDays = 7;
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50, roles: [100]),
            Wk(2, 40, roles: [100, 200]),
            Wk(3, 30, roles: [300]),
            Wk(4, 20, roles: null),
            Wk(5, 10, roles: [100], lastMessage: DateTime.UtcNow.AddDays(-8))
        };

        var (freeStats, freeFiltered) =
            WhoKnowsService.FilterWhoKnowsObjects(users, Members(), guild, contextUserId: 1);

        MakeGuildPremium();
        var (premiumStats, premiumFiltered) =
            WhoKnowsService.FilterWhoKnowsObjects(users, Members(), guild, contextUserId: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(freeFiltered), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(freeStats.AllowedRolesFiltered, Is.Null);

            Assert.That(Ids(premiumFiltered), Is.EqualTo(new[] { 1 }));
            Assert.That(premiumStats.GuildActivityThresholdFiltered, Is.EqualTo(1));
            Assert.That(premiumStats.AllowedRolesFiltered, Is.EqualTo(2));
            Assert.That(premiumStats.BlockedRolesFiltered, Is.EqualTo(1));
        });
    }

    [Test]
    public void Filter_ManualRolePicker_AppliesWithoutPremiumAndDropsUsersWithoutRoles()
    {
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50, roles: [100]),
            Wk(2, 40, roles: [200]),
            Wk(3, 30, roles: null)
        };

        var (stats, filtered) = WhoKnowsService.FilterWhoKnowsObjects(users, Members(), NewGuild(), contextUserId: 1,
            roles: [200]);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(filtered), Is.EqualTo(new[] { 2 }));
            Assert.That(stats.ManualRoleFilter, Is.EqualTo(2));
            Assert.That(stats.RequesterFiltered, Is.True);
        });
    }

    [Test]
    public void ListToString_OrdersByPlaycountAndLinksLastFmProfiles()
    {
        var users = new List<WhoKnowsObjectWithUser> { Wk(2, 5), Wk(1, 50), Wk(3, 20) };

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Server, English);
        var lines = text.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("1.").And.Contains("user1").And.Contains("**50** plays"));
            Assert.That(lines[1], Does.StartWith("2.").And.Contains("user3").And.Contains("**20** plays"));
            Assert.That(lines[2], Does.StartWith("3.").And.Contains("user2").And.Contains("**5** plays"));
            Assert.That(lines[0], Does.Contain("(https://last.fm/user/lastfm1)"));
        });
    }

    [Test]
    public void ListToString_RequesterInsideTop_IsBoldedOnce()
    {
        var users = new List<WhoKnowsObjectWithUser> { Wk(1, 50), Wk(2, 40) };

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 2, PrivacyLevel.Server, English);
        var lines = text.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[1], Does.StartWith("**2.**").And.EndWith("40 plays**"));
            Assert.That(lines[0], Does.Not.Contain("**1.**"));
        });
    }

    [Test]
    public void ListToString_CapsAtFourteenAndPinsRequesterWithTrueRank()
    {
        var users = Enumerable.Range(1, 30).Select(i => Wk(i, 100 - i)).ToList();

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 25, PrivacyLevel.Server, English);
        var lines = text.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(15));
            Assert.That(lines[13], Does.StartWith("14."));
            Assert.That(lines[14], Does.StartWith("**25.").And.Contains("user25").And.Contains("75 plays"));
        });
    }

    [Test]
    public void ListToString_RequesterOutsideListWithoutPlays_IsNotPinned()
    {
        var users = Enumerable.Range(1, 20).Select(i => Wk(i, 100 - i)).ToList();

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 999, PrivacyLevel.Server, English);

        Assert.That(text.TrimEnd('\n').Split('\n'), Has.Length.EqualTo(14));
    }

    [Test]
    public void ListToString_DuplicateUsers_AppearOnceAndDoNotConsumeAPosition()
    {
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50),
            Wk(1, 50),
            Wk(2, 40, lastFm: "shared"),
            Wk(3, 30, lastFm: "shared"),
            Wk(4, 20)
        };

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Server, English);
        var lines = text.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(3));
            Assert.That(lines[0], Does.StartWith("1.").And.Contains("user1"));
            Assert.That(lines[1], Does.StartWith("2.").And.Contains("user2"));
            Assert.That(lines[2], Does.StartWith("3.").And.Contains("user4"));
        });
    }

    [Test]
    public void ListToString_GlobalView_MasksNonGlobalUsersButKeepsTheirRank()
    {
        var users = new List<WhoKnowsObjectWithUser>
        {
            Wk(1, 50, privacy: PrivacyLevel.Server),
            Wk(2, 40)
        };

        var shown = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Global, English);
        var hidden = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Global, English,
            hidePrivateUsers: true);

        var shownLines = shown.TrimEnd('\n').Split('\n');
        var hiddenLines = hidden.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(shownLines[0], Does.StartWith("1.").And.Contain("Private user").And.Not.Contain("last.fm/user"));
            Assert.That(shownLines[1], Does.StartWith("2.").And.Contain("user2"));

            Assert.That(hiddenLines, Has.Length.EqualTo(1));
            Assert.That(hiddenLines[0], Does.StartWith("2.").And.Contain("user2"));
        });
    }

    [Test]
    public void ListToString_GlobalView_ServerViewStillShowsEveryone()
    {
        var users = new List<WhoKnowsObjectWithUser> { Wk(1, 50, privacy: PrivacyLevel.Server) };

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Server, English);

        Assert.That(text, Does.Contain("user1").And.Not.Contain("Private user"));
    }

    [Test]
    public void ListToString_CrownHolder_GetsCrownInsteadOfPosition()
    {
        var users = new List<WhoKnowsObjectWithUser> { Wk(1, 50), Wk(2, 40) };
        var crown = new CrownModel { Crown = new UserCrown { UserId = 1 }, CrownResult = "Crown claimed!" };

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 99, PrivacyLevel.Server, English,
            crownModel: crown);
        var lines = text.Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.StartWith("👑").And.Contain("user1"));
            Assert.That(lines[1], Does.Contain("2.").And.Contain("user2"));
            Assert.That(text, Does.EndWith("Crown claimed!"));
        });
    }

    [Test]
    public void ListToString_CloseFriendsOutsideTop_ArePinnedInItalicsWithTrueRank()
    {
        var users = Enumerable.Range(1, 20).Select(i => Wk(i, 100 - i)).ToList();

        var text = WhoKnowsService.WhoKnowsListToString(users, requestedUserId: 18, PrivacyLevel.Server, English,
            closeFriendUserIds: [3, 16, 18]);
        var lines = text.TrimEnd('\n').Split('\n');

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(16));
            Assert.That(lines[14], Does.StartWith("16.").And.Contain("*").And.Contain("user16"));
            Assert.That(lines[15], Does.StartWith("**18.").And.Contain("user18"));
        });
    }

    [Test]
    public void NameWithLink_FallsBackToLastFmNameWhenDiscordNameIsEmpty()
    {
        var user = Wk(1, 10, lastFm: "lastfm1", discordName: "   ");

        var text = WhoKnowsService.NameWithLink(user);

        Assert.That(text, Is.EqualTo("[\u2066lastfm1\u2069](https://last.fm/user/lastfm1)"));
    }

    [Test]
    public void NameWithLink_StripsBracketsAndEscapesMarkdownInDiscordName()
    {
        var user = Wk(1, 10, lastFm: "lastfm1", discordName: "[bracket] user_name");

        var text = WhoKnowsService.NameWithLink(user);

        Assert.That(text, Is.EqualTo("[\u2066bracket user\\_name\u2069](https://last.fm/user/lastfm1)"));
    }
}
