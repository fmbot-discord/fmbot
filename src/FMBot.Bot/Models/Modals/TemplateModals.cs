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

public class TemplateNameModal : IModal
{
    public string Title { get; set; }

    [InputLabel("Name")]
    [ModalTextInput("name", maxLength: 32)]
    public string Name { get; set; }
}
