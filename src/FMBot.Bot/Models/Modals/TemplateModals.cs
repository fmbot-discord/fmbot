using Discord;
using Discord.Interactions;

namespace FMBot.Bot.Models.Modals;

public class TemplateViewScriptModal : IModal
{
    public string Title { get; set; }

    [InputLabel("Content")]
    [ModalTextInput("content", TextInputStyle.Paragraph)]
    public string Content { get; set; }
}
