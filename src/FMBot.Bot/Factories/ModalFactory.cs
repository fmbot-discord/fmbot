using System;
using System.Collections.Generic;
using System.Linq;
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

    public static ModalProperties CreateRenameArtistModal(string customId, string artistValue) =>
        new(customId, "Editing artist")
        {
            Components =
            [
                new LabelProperties("New artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    Value = artistValue,
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

    public static ModalProperties CreateRenameAlbumModal(string customId, string artistValue, string albumValue) =>
        new(customId, "Editing album")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    Value = artistValue,
                    MinLength = 1
                }),
                new LabelProperties("Album name", new TextInputProperties("album_name", TextInputStyle.Short)
                {
                    Placeholder = "Abbey Road",
                    Value = albumValue,
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

    public static ModalProperties CreateRenameTrackModal(string customId, string artistValue, string trackValue) =>
        new(customId, "Editing track")
        {
            Components =
            [
                new LabelProperties("Artist name", new TextInputProperties("artist_name", TextInputStyle.Short)
                {
                    Placeholder = "The Beatles",
                    Value = artistValue,
                    MinLength = 1
                }),
                new LabelProperties("Track name", new TextInputProperties("track_name", TextInputStyle.Short)
                {
                    Placeholder = "Yesterday",
                    Value = trackValue,
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
                new LabelProperties("Type 'confirm' to delete", new TextInputProperties("confirmation", TextInputStyle.Short)
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

    public static ModalProperties CreateModifyShortcutModal(string customId, string inputValue, string outputValue) =>
        new(customId, "Modify shortcut")
        {
            Components =
            [
                new LabelProperties("Input (what you'll type)", new TextInputProperties("input", TextInputStyle.Short)
                {
                    Value = inputValue,
                    MinLength = 1,
                    MaxLength = 50
                }),
                new LabelProperties("Output (command to run)", new TextInputProperties("output", TextInputStyle.Paragraph)
                {
                    Value = outputValue,
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
                new LabelProperties("Enter Last.fm username", new TextInputProperties("username_lastfm", TextInputStyle.Short)
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

    public static ModalProperties CreateTemplateViewScriptModal(string customId, string title, string contentValue) =>
        new(customId, title)
        {
            Components =
            [
                new LabelProperties("Content", new TextInputProperties("content", TextInputStyle.Paragraph)
                {
                    Value = contentValue
                })
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

    public static ModalProperties CreateTemplateNameModal(string customId, string title, string nameValue) =>
        new(customId, title)
        {
            Components =
            [
                new LabelProperties("Name", new TextInputProperties("name", TextInputStyle.Short)
                {
                    Value = nameValue,
                    MaxLength = 32
                })
            ]
        };

    public static ModalProperties CreateCustomColorModal(string customId) =>
        new(customId, "Set custom accent color")
        {
            Components =
            [
                new LabelProperties("Hex color (e.g. #FF5733)",
                    new TextInputProperties("hex_color", TextInputStyle.Short)
                {
                    Placeholder = "#FF5733",
                    MinLength = 4,
                    MaxLength = 7
                })
            ]
        };

    private static StringMenuProperties GetTimePeriodMenu(string customId, string currentTimePeriod)
    {
        var now = DateTime.UtcNow;
        var currentMonthName = now.ToString("MMMM");
        var previousMonthName = now.AddMonths(-1).ToString("MMMM");
        var currentYear = now.Year.ToString();
        var previousYear = (now.Year - 1).ToString();

        var options = new List<StringMenuSelectOptionProperties>
        {
            new("Weekly", "weekly") { Default = currentTimePeriod == "weekly" },
            new("Monthly", "monthly") { Default = currentTimePeriod == "monthly" },
            new("Quarterly", "quarterly") { Default = currentTimePeriod == "quarterly" },
            new("Half-yearly", "half-yearly") { Default = currentTimePeriod == "half-yearly" },
            new("Yearly", "yearly") { Default = currentTimePeriod == "yearly" },
            new("All time", "overall") { Default = currentTimePeriod == "overall" },
            new(currentMonthName, currentMonthName.ToLower()) { Default = currentTimePeriod.Equals(currentMonthName, StringComparison.OrdinalIgnoreCase) },
            new(previousMonthName, previousMonthName.ToLower()) { Default = currentTimePeriod.Equals(previousMonthName, StringComparison.OrdinalIgnoreCase) },
            new(currentYear, currentYear) { Default = currentTimePeriod == currentYear },
            new(previousYear, previousYear) { Default = currentTimePeriod == previousYear },
        };

        if (options.All(o => !o.Default) && !string.IsNullOrWhiteSpace(currentTimePeriod))
        {
            options.Add(new StringMenuSelectOptionProperties(currentTimePeriod, currentTimePeriod) { Default = true });
        }

        var menu = new StringMenuProperties(customId);
        menu.AddOptions(options);
        return menu;
    }

    public static ModalProperties CreateAlbumChartSettingsModal(
        string customId, int width, int height, string timePeriod,
        int titleSetting, bool skip, bool sfw, bool rainbow,
        int? yearFilter, int? decadeFilter, bool filterSingles = false) =>
        new(customId, "Edit chart settings")
        {
            Components =
            [
                new LabelProperties("Size (e.g. 3x3)", new TextInputProperties("size", TextInputStyle.Short)
                {
                    Value = $"{width}x{height}",
                    Placeholder = "3x3",
                    MinLength = 3,
                    MaxLength = 5
                }),
                new LabelProperties("Time period", GetTimePeriodMenu("time_period", timePeriod)),
                new LabelProperties("Options", new CheckboxGroupProperties("options")
                {
                    new CheckboxGroupOptionProperties("Show titles", "titles") { Default = titleSetting == 1 },
                    new CheckboxGroupOptionProperties("Skip albums without image", "skip") { Default = skip },
                    new CheckboxGroupOptionProperties("SFW only", "sfw") { Default = sfw },
                    new CheckboxGroupOptionProperties("Rainbow sort", "rainbow") { Default = rainbow },
                    new CheckboxGroupOptionProperties("Hide singles", "hidesingles") { Default = filterSingles },
                }),
                new LabelProperties("Release filter (e.g. 2024 or 1990s)",
                    new TextInputProperties("release_filter", TextInputStyle.Short)
                    {
                        Placeholder = "2024 or 1990s",
                        Required = false,
                        Value = decadeFilter is > 0 ? $"{decadeFilter}s"
                            : yearFilter is > 0 ? yearFilter.ToString()
                            : null,
                        MaxLength = 5
                    })
            ]
        };

    public static ModalProperties CreateArtistChartSettingsModal(
        string customId, int width, int height, string timePeriod,
        int titleSetting, bool skip, bool rainbow) =>
        new(customId, "Edit chart settings")
        {
            Components =
            [
                new LabelProperties("Size (e.g. 3x3)", new TextInputProperties("size", TextInputStyle.Short)
                {
                    Value = $"{width}x{height}",
                    Placeholder = "3x3",
                    MinLength = 3,
                    MaxLength = 5
                }),
                new LabelProperties("Time period", GetTimePeriodMenu("time_period", timePeriod)),
                new LabelProperties("Options", new CheckboxGroupProperties("options")
                {
                    new CheckboxGroupOptionProperties("Show titles", "titles") { Default = titleSetting == 1 },
                    new CheckboxGroupOptionProperties("Skip artists without image", "skip") { Default = skip },
                    new CheckboxGroupOptionProperties("Rainbow sort", "rainbow") { Default = rainbow },
                })
            ]
        };

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
