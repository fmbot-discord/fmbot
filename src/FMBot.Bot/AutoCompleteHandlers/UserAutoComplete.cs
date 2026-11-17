using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Services;
using FMBot.Bot.Services.Guild;
using FMBot.Domain;
using Microsoft.Extensions.Caching.Memory;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FMBot.Bot.AutoCompleteHandlers;

public class UserAutoComplete : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private const int MaxChoices = 25;
    private const int MaxGuildUserCandidates = 100;

    private readonly ChannelActivityService _channelActivityService;
    private readonly FriendsService _friendsService;
    private readonly GuildService _guildService;
    private readonly IMemoryCache _cache;

    public UserAutoComplete(ChannelActivityService channelActivityService, FriendsService friendsService,
        GuildService guildService, IMemoryCache cache)
    {
        this._channelActivityService = channelActivityService;
        this._friendsService = friendsService;
        this._guildService = guildService;
        this._cache = cache;
    }

    private record UserChoice(string Name, string Value, string[] SearchTerms);

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var searchValue = option.Value?.Trim();

        if (!string.IsNullOrEmpty(searchValue) && IsExplicitUser(searchValue))
        {
            return [new ApplicationCommandOptionChoiceProperties(searchValue, searchValue)];
        }

        var friendNamesTask = GetFriendNames(context.User.Id).ObserveFaults();
        var guildUsersTask = context.Guild != null
            ? this._guildService.GetGuildUsers(context.Guild.Id).ObserveFaults()
            : null;

        var candidates = new List<UserChoice>();

        if (context.Channel != null)
        {
            candidates.AddRange(this._channelActivityService.GetRecentUsers(context.Channel.Id)
                .Where(w => w.DiscordUserId != context.User.Id)
                .Select(s => ForDiscordUser(s.DiscordUserId, s.DisplayName, s.UserName)));
        }

        candidates.AddRange((await friendNamesTask)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(s => new UserChoice(s, s, [s])));

        if (context.Guild != null)
        {
            if (!string.IsNullOrEmpty(searchValue))
            {
                candidates.AddRange(context.Guild.Users.Values
                    .Where(w => !w.IsBot &&
                                w.Id != context.User.Id &&
                                (w.GetDisplayName().Contains(searchValue, StringComparison.OrdinalIgnoreCase) ||
                                 w.Username.Contains(searchValue, StringComparison.OrdinalIgnoreCase)) &&
                                PublicProperties.RegisteredUsers.ContainsKey(w.Id))
                    .Select(s => ForDiscordUser(s.Id, s.GetDisplayName(), s.Username)));
            }

            candidates.AddRange((await guildUsersTask).Values
                .Where(w => w.Bot != true &&
                            w.DiscordUserId != 0 &&
                            w.DiscordUserId != context.User.Id &&
                            !string.IsNullOrWhiteSpace(w.UserName) &&
                            (string.IsNullOrEmpty(searchValue) ||
                             w.UserName.Contains(searchValue, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(o => o.LastUsed)
                .Take(MaxGuildUserCandidates)
                .Select(s => ForDiscordUser(s.DiscordUserId, s.UserName, null)));
        }

        if (string.IsNullOrEmpty(searchValue))
        {
            return candidates
                .DistinctBy(d => d.Value, StringComparer.OrdinalIgnoreCase)
                .Take(MaxChoices)
                .Select(s => new ApplicationCommandOptionChoiceProperties(s.Name, s.Value))
                .ToList();
        }

        var results = candidates
            .Where(w => w.SearchTerms.Any(a => a.StartsWith(searchValue, StringComparison.OrdinalIgnoreCase)))
            .Concat(candidates
                .Where(w => w.SearchTerms.Any(a => a.Contains(searchValue, StringComparison.OrdinalIgnoreCase))))
            .DistinctBy(d => d.Value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxChoices - 1)
            .Select(s => new ApplicationCommandOptionChoiceProperties(s.Name, s.Value))
            .ToList();

        if (searchValue.Length <= 100 &&
            !results.Any(a => a.StringValue.Equals(searchValue, StringComparison.OrdinalIgnoreCase)))
        {
            results.Add(new ApplicationCommandOptionChoiceProperties(searchValue, searchValue));
        }

        return results;
    }

    private static bool IsExplicitUser(string searchValue)
    {
        return searchValue.StartsWith("lfm:", StringComparison.OrdinalIgnoreCase) ||
               searchValue.StartsWith("<@") ||
               (searchValue.Length is >= 17 and <= 19 && ulong.TryParse(searchValue, out _));
    }

    private static UserChoice ForDiscordUser(ulong discordUserId, string displayName, string userName)
    {
        var name = userName != null && !userName.Equals(displayName, StringComparison.OrdinalIgnoreCase)
            ? $"{displayName} ({userName})"
            : displayName;

        return new UserChoice(
            name.Length > 100 ? name[..100] : name,
            discordUserId.ToString(),
            userName != null ? [displayName, userName] : [displayName]);
    }

    private async Task<List<string>> GetFriendNames(ulong discordUserId)
    {
        if (!PublicProperties.RegisteredUsers.TryGetValue(discordUserId, out var userId))
        {
            return [];
        }

        return await this._cache.GetOrCreateAsync($"user-autocomplete-friends-{userId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            return await this._friendsService.GetRegisteredFriendNamesAsync(userId);
        });
    }
}
