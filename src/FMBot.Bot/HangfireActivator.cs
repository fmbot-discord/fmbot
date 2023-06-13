using System;
using Hangfire;

namespace FMBot.Bot;

public class HangfireActivator : JobActivator
{
    private readonly IServiceProvider _serviceProvider;

    public HangfireActivator(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    public override object ActivateJob(Type type)
    {
        return this._serviceProvider.GetService(type);
    }
}
