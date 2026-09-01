using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Serilog;

namespace FMBot.Bot.Extensions;

public static class TaskExtensions
{
    public static Task ObserveFaults(this Task task, [CallerMemberName] string caller = null)
    {
        _ = task.ContinueWith(
            t => Log.Warning(t.Exception?.GetBaseException(), "Background task faulted in {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    public static Task<T> ObserveFaults<T>(this Task<T> task, [CallerMemberName] string caller = null)
    {
        _ = task.ContinueWith(
            t => Log.Warning(t.Exception?.GetBaseException(), "Background task faulted in {Caller}", caller),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }
}
