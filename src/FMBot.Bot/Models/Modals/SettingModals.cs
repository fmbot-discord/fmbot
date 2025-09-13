using Discord.Interactions;

namespace FMBot.Bot.Models.Modals;


public class RemoveAccountConfirmModal : IModal
{
    public string Title => "Confirm account deletion";

    [InputLabel("Type 'confirm' to delete")]
    [ModalTextInput("confirm", minLength: 1, maxLength: 7)]
    public string Confirmation { get; set; }
}

public class SetPrefixModal : IModal
{
    public string Title => "Set .fmbot text command prefix";

    [InputLabel("Enter new prefix")]
    [ModalTextInput("new_prefix", placeholder: ".", minLength: 1, maxLength: 15)]
    public string NewPrefix { get; set; }
}

public class SetFmbotActivityThresholdModal : IModal
{
    public string Title => "Set .fmbot activity threshold";

    [InputLabel("Enter amount of days")]
    [ModalTextInput("amount", placeholder: "30", minLength: 1, maxLength: 3)]
    public string Amount { get; set; }
}

public class SetGuildActivityThresholdModal : IModal
{
    public string Title => "Set server activity threshold";

    [InputLabel("Enter amount of days")]
    [ModalTextInput("amount", placeholder: "30", minLength: 1, maxLength: 3)]
    public string Amount { get; set; }
}

public class SetCrownActivityThresholdModal : IModal
{
    public string Title => "Set .fmbot crown activity threshold";

    [InputLabel("Enter amount of days")]
    [ModalTextInput("amount", placeholder: "30", minLength: 1, maxLength: 3)]
    public string Amount { get; set; }
}

public class SetCrownMinPlaycountModal : IModal
{
    public string Title => "Set .fmbot crown minimum playcount";

    [InputLabel("Enter minimum amount of plays")]
    [ModalTextInput("amount", placeholder: "30", minLength: 1, maxLength: 3)]
    public string Amount { get; set; }
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

public class CreateShortcutModal : IModal
{
    public string Title => "Create new shortcut";

    [InputLabel("Input (what you'll type)")]
    [ModalTextInput("input", placeholder: "np", minLength: 1, maxLength: 50)]
    public string Input { get; set; }

    [InputLabel("Output (command to run)")]
    [ModalTextInput("output", placeholder: "fm", minLength: 1, maxLength: 200)]
    public string Output { get; set; }
}

public class ModifyShortcutModal : IModal
{
    public string Title => "Modify shortcut";

    [InputLabel("Input (what you'll type)")]
    [ModalTextInput("input", minLength: 1, maxLength: 50)]
    public string Input { get; set; }

    [InputLabel("Output (command to run)")]
    [ModalTextInput("output",  minLength: 1, maxLength: 200)]
    public string Output { get; set; }
}
