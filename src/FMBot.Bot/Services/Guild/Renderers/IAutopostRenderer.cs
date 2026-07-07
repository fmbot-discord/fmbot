using System;
using System.Threading.Tasks;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using NetCord.Rest;

namespace FMBot.Bot.Services.Guild.Renderers;

public interface IAutopostRenderer
{
    AutopostType Type { get; }

    Task<AutopostRenderResult> RenderAsync(AutopostRenderContext context);
}

public class AutopostRenderContext
{
    public GuildAutopost Autopost { get; set; }

    public string GuildName { get; set; }

    public int[] RoleUserIds { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime? NextPost { get; set; }

    public AutopostSnapshot PreviousSnapshot { get; set; }
}

public class AutopostRenderResult
{
    public ComponentContainerProperties Container { get; set; }

    public AutopostSnapshot Snapshot { get; set; }

    public string Footer { get; set; }

    public bool HasMoreEntries { get; set; }
}
