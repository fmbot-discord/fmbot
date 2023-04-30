using Discord.Interactions;

namespace FMBot.Bot.Models.Modals;


public class PrefixModal : IModal
{
    public string Title => "Set .fmbot text command prefix";

    [InputLabel("Enter new prefix")]
    [ModalTextInput("new_prefix", placeholder: ".", minLength: 1, maxLength: 15)]
    public string NewPrefix { get; set; }
}

public class SetActivityThresholdModal : IModal
{
    public string Title => "Set .fmbot activity threshold";

    [InputLabel("Enter amount of days before someone is marked as inactive")]
    [ModalTextInput("day_amount", placeholder: "30", minLength: 1, maxLength: 3)]
    public string DayAmount { get; set; }
}

public class RemoveDisabledChannelCommandModal : IModal
{
    public string Title => "Enable command in channel";

    [InputLabel("Enter command")]
    [ModalTextInput("command", placeholder: "whoknows", minLength: 1, maxLength: 40)]
    public string Command { get; set; }
}

public class AddDisabledChannelCommandModal : IModal
{
    public string Title => "Disable command in channel";

    [InputLabel("Enter command")]
    [ModalTextInput("command", placeholder: "whoknows", minLength: 1, maxLength: 40)]
    public string Command { get; set; }
}

public class RemoveDisabledGuildCommandModal : IModal
{
    public string Title => "Enable command server-wide";

    [InputLabel("Enter command")]
    [ModalTextInput("command", placeholder: "whoknows", minLength: 1, maxLength: 40)]
    public string Command { get; set; }
}

public class AddDisabledGuildCommandModal : IModal
{
    public string Title => "Disable command server-wide";

    [InputLabel("Enter command")]
    [ModalTextInput("command", placeholder: "whoknows", minLength: 1, maxLength: 40)]
    public string Command { get; set; }
}
