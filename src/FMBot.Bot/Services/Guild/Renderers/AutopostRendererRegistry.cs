using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Bot.Services.Guild.Renderers;

public class AutopostRendererRegistry
{
    private readonly Dictionary<AutopostType, IAutopostRenderer> _renderers;

    public AutopostRendererRegistry(IEnumerable<IAutopostRenderer> renderers)
    {
        this._renderers = renderers.ToDictionary(d => d.Type);
    }

    public IAutopostRenderer Get(AutopostType type)
    {
        return this._renderers[type];
    }
}
