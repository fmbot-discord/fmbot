using System.Collections.Frozen;
using System.Collections.Generic;

namespace FMBot.Bot.Resources;

public static class FlowCommands
{
    public static readonly FrozenDictionary<string, string> Targets = new Dictionary<string, string>
    {
        [InteractionConstants.Artist.Info] = "artist",
        [InteractionConstants.Artist.Overview] = "artistoverview",
        [InteractionConstants.Artist.Tracks] = "artisttracks",
        [InteractionConstants.Artist.Albums] = "artistalbums",
        [InteractionConstants.Artist.Crown] = "crown",
        [InteractionConstants.Artist.WhoKnows] = "whoknows",

        [InteractionConstants.Album.Info] = "album",
        [InteractionConstants.Album.Tracks] = "albumtracks",
        [InteractionConstants.Album.Cover] = "cover",
        [InteractionConstants.Album.RandomCover] = "cover",

        [InteractionConstants.TrackLyrics] = "lyrics",
        [InteractionConstants.FmTrackDetails] = "trackdetails",
        [InteractionConstants.FmTrackLove] = "love",
        [InteractionConstants.FmTrackUnlove] = "unlove",

        [InteractionConstants.RecapAlltime] = "recap",
        [InteractionConstants.RecapPicker] = "recap",
        [InteractionConstants.RandomMilestone] = "milestone",
        [InteractionConstants.GapView] = "gaps",
        [InteractionConstants.GuildMembers] = "members",

        [InteractionConstants.Discogs.Collection] = "collection",

        [InteractionConstants.ImportInstructionsPickSource] = "import",
        [InteractionConstants.ImportInstructionsSpotify] = "import spotify",
        [InteractionConstants.ImportInstructionsAppleMusic] = "import applemusic",
        [InteractionConstants.ImportManage] = "import manage",
        [InteractionConstants.ImportModify.Start] = "import modify",

        [InteractionConstants.PremiumServer.GetOverview] = "premiumserver",
        [InteractionConstants.User.Settings] = "settings",
        [InteractionConstants.User.Profile] = "profile",
        [InteractionConstants.FmCommand.FmModeChange] = "fmmode",
        [InteractionConstants.ResponseModeChange] = "responsemode",
        [InteractionConstants.CoverTypeChange] = "covermode",
        [InteractionConstants.Shortcuts.ViewAll] = "shortcuts"
    }.ToFrozenDictionary();

    public static string GetBaseId(string customId)
    {
        if (string.IsNullOrEmpty(customId))
        {
            return null;
        }

        var separatorIndex = customId.IndexOf(':');
        return separatorIndex == -1 ? customId : customId[..separatorIndex];
    }

    public static string GetTarget(string customId)
    {
        var baseId = GetBaseId(customId);
        return baseId == null ? null : Targets.GetValueOrDefault(baseId);
    }
}
