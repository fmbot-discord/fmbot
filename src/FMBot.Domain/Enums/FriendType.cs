using FMBot.Domain.Attributes;

namespace FMBot.Domain.Enums;

public enum FriendType
{
    [Option("👥 Normal", "Shown in friend commands, but not in `friendsfm`")]
    Normal = 1,

    [Option("👁️ Visible everywhere", "Also shown in `friendsfm`")]
    VisibleInNowPlaying = 2,

    [Option("⭐ Close friend", "Always visible in WhoKnows no matter their rank, plus in `friendsfm`", supporterOnly: true)]
    CloseFriend = 3
}
