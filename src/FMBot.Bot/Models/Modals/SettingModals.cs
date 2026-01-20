namespace FMBot.Bot.Models.Modals;

public class RemoveAccountConfirmModal
{
    public string Confirmation { get; set; }
}

public class SetPrefixModal
{
    public string NewPrefix { get; set; }
}

public class SetFmbotActivityThresholdModal
{
    public string Amount { get; set; }
}

public class SetGuildActivityThresholdModal
{
    public string Amount { get; set; }
}

public class SetCrownActivityThresholdModal
{
    public string Amount { get; set; }
}

public class SetCrownMinPlaycountModal
{
    public string Amount { get; set; }
}

public class RemoveDisabledChannelCommandModal
{
    public string Command { get; set; }
}

public class AddDisabledChannelCommandModal
{
    public string Command { get; set; }
}

public class RemoveDisabledGuildCommandModal
{
    public string Command { get; set; }
}

public class AddDisabledGuildCommandModal
{
    public string Command { get; set; }
}

public class CreateShortcutModal
{
    public string Input { get; set; }
    public string Output { get; set; }
}

public class ModifyShortcutModal
{
    public string Input { get; set; }
    public string Output { get; set; }
}
