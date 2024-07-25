using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FMBot.Bot.Services;
using FMBot.Domain.Models;
using Serilog;

namespace FMBot.Bot.Handlers;

public class UpdateQueueHandler : IDisposable
{
    private const int MaxConcurrentTasks = 3;
    private const int EmptyQueueDelayMs = 100;

    private readonly ConcurrentQueue<UpdateUserQueueItem> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrentTasks, MaxConcurrentTasks);
    private readonly UpdateService _updateService;
    private readonly TimeSpan _delayBetweenOperations;

    public UpdateQueueHandler(UpdateService updateService, TimeSpan delayBetweenOperations)
    {
        _updateService = updateService;
        _delayBetweenOperations = delayBetweenOperations;
    }

    public void EnqueueUser(UpdateUserQueueItem item)
    {
        _queue.Enqueue(item);
        Log.Debug("User enqueued. Queue size: {QueueSize}", _queue.Count);
    }

    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(MaxConcurrentTasks);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var item))
                {
                    tasks.Add(ProcessItemAsync(item, cancellationToken));
                    if (tasks.Count >= MaxConcurrentTasks)
                    {
                        await CompletionTaskAsync(tasks);
                    }
                }
                else if (tasks.Count > 0)
                {
                    await CompletionTaskAsync(tasks);
                }
                else
                {
                    await Task.Delay(EmptyQueueDelayMs, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("Queue processing cancelled");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while processing the queue");
        }
        finally
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task CompletionTaskAsync(List<Task> tasks)
    {
        var completionSource = new TaskCompletionSource<bool>();
        foreach (var task in tasks)
        {
            _ = task.ContinueWith(t =>
            {
                completionSource.TrySetResult(true);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        await completionSource.Task;
        tasks.RemoveAll(t => t.IsCompleted);
    }

    private async Task ProcessItemAsync(UpdateUserQueueItem item, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await _updateService.UpdateUser(item);
            await Task.Delay(_delayBetweenOperations, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing item for user {UserId}", item.UserId);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public int GetQueueSize() => _queue.Count;

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
