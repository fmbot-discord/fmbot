using System.Net.Mime;

namespace FMBot.Domain.Models;

public enum ResponseType
{
    Text = 1,
    Embed = 2,
    Paginator = 3,
    ImageWithEmbed = 4,
    ImageOnly = 5,
    PagedSelection = 6
}
