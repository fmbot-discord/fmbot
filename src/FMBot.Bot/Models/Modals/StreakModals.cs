using Discord.Interactions;
using Discord;

namespace FMBot.Bot.Models.Modals;

public class DeleteStreakModal : IModal
{
    public string Title => "Enter Streak ID to delete";

    [InputLabel("Deletion ID")]
    [ModalTextInput("ID", placeholder: "1234", maxLength: 9)]
    public string StreakId { get; set; }
}
