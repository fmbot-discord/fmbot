using FMBot.Bot.Models;
using FMBot.Bot.Models.Modals;
using NetCord;
using NetCord.Rest;

namespace FMBot.Bot.Factories;


public static class ModalFactory
{
    // Import Modals
    public static ModalProperties CreateModifyArtistModal(string customId) =>
        new(customId, "Select artist")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                })
            ]
        };

    public static ModalProperties CreateModifyAlbumModal(string customId) =>
        new(customId, "Select album")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                }),
                new LabelProperties("Album name", new TextInputProperties("album_name", TextInputStyle.Short)
                {
                    Placeholder = "Abbey Road",
                    MinLength = 1
                })
            ]
        };

    public static ModalProperties CreateModifyTrackModal(string customId) =>
        new(customId, "Select track")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                }),
                new LabelProperties("Track name", new TextInputProperties("track_name", TextInputStyle.Short)
                {
                    Placeholder = "Yesterday",
                    MinLength = 1
                })
            ]
        };

    public static ModalProperties CreateRenameArtistModal(string customId) =>
        new(customId, "Editing imports")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                })
            ]
        };

    public static ModalProperties CreateRenameAlbumModal(string customId) =>
        new(customId, "Editing imports")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                }),
                new LabelProperties("Album name", new TextInputProperties("album_name", TextInputStyle.Short)
                {
                    Placeholder = "Abbey Road",
                    MinLength = 1
                })
            ]
        };

    public static ModalProperties CreateRenameTrackModal(string customId) =>
        new(customId, "Editing imports")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    MinLength = 1
                }),
                new LabelProperties("Track name", new TextInputProperties("track_name", TextInputStyle.Short)
                {
                    Placeholder = "Yesterday",
                    MinLength = 1
                })
            ]
        };

    // Setting Modals
    public static ModalProperties CreateRemoveAccountConfirmModal(string customId) =>
        new(customId, "Confirm account deletion")
        {
            Components =
            [
                new LabelProperties("Type 'confirm' to delete", new TextInputProperties("confirm", TextInputStyle.Short)
                {
                    MinLength = 1,
                    MaxLength = 7
                })
            ]
        };

    public static ModalProperties CreateSetPrefixModal(string customId) =>
        new(customId, "Set .fmbot text command prefix")
        {
            Components =
            [
                new LabelProperties("Enter new prefix", new TextInputProperties("new_prefix", TextInputStyle.Short)
                {
                    Placeholder = ".",
                    MinLength = 1,
                    MaxLength = 15
                })
            ]
        };

    public static ModalProperties CreateSetFmbotActivityThresholdModal(string customId) =>
        new(customId, "Set .fmbot activity threshold")
        {
            Components =
            [
                new LabelProperties("Enter amount of days", new TextInputProperties("amount", TextInputStyle.Short)
                {
                    Placeholder = "30",
                    MinLength = 1,
                    MaxLength = 3
                })
            ]
        };

    public static ModalProperties CreateSetGuildActivityThresholdModal(string customId) =>
        new(customId, "Set server activity threshold")
        {
            Components =
            [
                new LabelProperties("Enter amount of days", new TextInputProperties("amount", TextInputStyle.Short)
                {
                    Placeholder = "30",
                    MinLength = 1,
                    MaxLength = 3
                })
            ]
        };

    public static ModalProperties CreateSetCrownActivityThresholdModal(string customId) =>
        new(customId, "Set .fmbot crown activity threshold")
        {
            Components =
            [
                new LabelProperties("Enter amount of days", new TextInputProperties("amount", TextInputStyle.Short)
                {
                    Placeholder = "30",
                    MinLength = 1,
                    MaxLength = 3
                })
            ]
        };

    public static ModalProperties CreateSetCrownMinPlaycountModal(string customId) =>
        new(customId, "Set .fmbot crown minimum playcount")
        {
            Components =
            [
                new LabelProperties("Enter minimum amount of plays", new TextInputProperties("amount", TextInputStyle.Short)
                {
                    Placeholder = "30",
                    MinLength = 1,
                    MaxLength = 3
                })
            ]
        };

    public static ModalProperties CreateRemoveDisabledChannelCommandModal(string customId) =>
        new(customId, "Enable command in channel")
        {
            Components =
            [
                new LabelProperties("Enter command", new TextInputProperties("command", TextInputStyle.Short)
                {
                    Placeholder = "whoknows",
                    MinLength = 1,
                    MaxLength = 40
                })
            ]
        };

    public static ModalProperties CreateAddDisabledChannelCommandModal(string customId) =>
        new(customId, "Disable command in channel")
        {
            Components =
            [
                new LabelProperties("Enter command", new TextInputProperties("command", TextInputStyle.Short)
                {
                    Placeholder = "whoknows",
                    MinLength = 1,
                    MaxLength = 40
                })
            ]
        };

    public static ModalProperties CreateRemoveDisabledGuildCommandModal(string customId) =>
        new(customId, "Enable command server-wide")
        {
            Components =
            [
                new LabelProperties("Enter command", new TextInputProperties("command", TextInputStyle.Short)
                {
                    Placeholder = "whoknows",
                    MinLength = 1,
                    MaxLength = 40
                })
            ]
        };

    public static ModalProperties CreateAddDisabledGuildCommandModal(string customId) =>
        new(customId, "Disable command server-wide")
        {
            Components =
            [
                new LabelProperties("Enter command", new TextInputProperties("command", TextInputStyle.Short)
                {
                    Placeholder = "whoknows",
                    MinLength = 1,
                    MaxLength = 40
                })
            ]
        };

    public static ModalProperties CreateCreateShortcutModal(string customId) =>
        new(customId, "Create new shortcut")
        {
            Components =
            [
                new LabelProperties("Input (what you'll type)", new TextInputProperties("input", TextInputStyle.Short)
                {
                    Placeholder = "np",
                    MinLength = 1,
                    MaxLength = 50
                }),
                new LabelProperties("Output (command to run)", new TextInputProperties("output", TextInputStyle.Short)
                {
                    Placeholder = "fm",
                    MinLength = 1,
                    MaxLength = 200
                })
            ]
        };

    public static ModalProperties CreateModifyShortcutModal(string customId) =>
        new(customId, "Modify shortcut")
        {
            Components =
            [
                new LabelProperties("Input (what you'll type)", new TextInputProperties("input", TextInputStyle.Short)
                {
                    MinLength = 1,
                    MaxLength = 50
                }),
                new LabelProperties("Output (command to run)", new TextInputProperties("output", TextInputStyle.Short)
                {
                    MinLength = 1,
                    MaxLength = 200
                })
            ]
        };

    // Admin Modals
    public static ModalProperties CreateReportGlobalWhoKnowsModal(string customId) =>
        new(customId, "Report GlobalWhoKnows user")
        {
            Components =
            [
                new LabelProperties("Enter Last.fm username", new TextInputProperties("lastfm_username", TextInputStyle.Short)
                {
                    Placeholder = "fm-bot",
                    MinLength = 2,
                    MaxLength = 15
                }),
                new LabelProperties("Add note (optional)", new TextInputProperties("note", TextInputStyle.Paragraph)
                {
                    Placeholder = "8 days listening time in a week",
                    MaxLength = 300,
                    Required = false
                })
            ]
        };

    public static ModalProperties CreateReportGlobalWhoKnowsBanModal(string customId) =>
        new(customId, "Confirm GlobalWhoKnows ban")
        {
            Components =
            [
                new LabelProperties("Add admin note", new TextInputProperties("note", TextInputStyle.Paragraph)
                {
                    Placeholder = "8 days listening time in a week",
                    MaxLength = 300
                })
            ]
        };

    public static ModalProperties CreateReportArtistModal(string customId) =>
        new(customId, "Report artist")
        {
            Components =
            [
                new LabelProperties("Artist", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "Death Grips",
                    MinLength = 2,
                    MaxLength = 150
                }),
                new LabelProperties("Add note (optional)", new TextInputProperties("note", TextInputStyle.Paragraph)
                {
                    MaxLength = 300,
                    Required = false
                })
            ]
        };

    public static ModalProperties CreateReportAlbumModal(string customId) =>
        new(customId, "Report album")
        {
            Components =
            [
                new LabelProperties("Artist", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "Death Grips",
                    MinLength = 2,
                    MaxLength = 150
                }),
                new LabelProperties("Album", new TextInputProperties("album_name", TextInputStyle.Short)
                {
                    Placeholder = "No Love Deep Web",
                    MinLength = 2,
                    MaxLength = 150
                }),
                new LabelProperties("Add note (optional)", new TextInputProperties("note", TextInputStyle.Paragraph)
                {
                    MaxLength = 300,
                    Required = false
                })
            ]
        };

    public static ModalProperties CreateDenyReportModal(string customId) =>
        new(customId, "Deny report")
        {
            Components =
            [
                new LabelProperties("Add note to send to user (optional)", new TextInputProperties("note", TextInputStyle.Paragraph)
                {
                    MaxLength = 300,
                    Required = false
                })
            ]
        };

    // Template Modals
    public static ModalProperties CreateTemplateViewScriptModal(string customId, string title) =>
        new(customId, title)
        {
            Components =
            [
                new LabelProperties("Content", new TextInputProperties("content", TextInputStyle.Paragraph))
            ]
        };

    public static ModalProperties CreateTemplateNameModal(string customId, string title) =>
        new(customId, title)
        {
            Components =
            [
                new LabelProperties("Name", new TextInputProperties("name", TextInputStyle.Short)
                {
                    MaxLength = 32
                })
            ]
        };

    // Streak Modals
    public static ModalProperties CreateDeleteStreakModal(string customId) =>
        new(customId, "Enter Streak ID to delete")
        {
            Components =
            [
                new LabelProperties("Deletion ID", new TextInputProperties("ID", TextInputStyle.Short)
                {
                    Placeholder = "1234",
                    MaxLength = 9
                })
            ]
        };
}
