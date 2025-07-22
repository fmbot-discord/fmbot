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
    private const int MaxConcurrentTasks = 2;
    private const int EmptyQueueDelayMs = 200;

    private readonly ConcurrentQueue<UpdateUserQueueItem> _queue = new();
    private readonly SemaphoreSlim _semaphore = new(MaxConcurrentTasks, MaxConcurrentTasks);
    private readonly UpdateService _updateService;
    private readonly TimeSpan _delayBetweenOperations;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

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

    public async Task ProcessQueueAsync()
    {
        await ProcessQueueAsync(_cancellationTokenSource.Token);
    }

    public async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        Log.Information("ProcessQueueAsync started - initial queue size: {QueueSize}", _queue.Count);
        var tasks = new List<Task>(MaxConcurrentTasks);
        var processedCount = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_queue.TryDequeue(out var item))
                {
                    Log.Debug("Dequeued user {UserId} for processing. Remaining in queue: {QueueSize}", item.UserId, _queue.Count);
                    tasks.Add(ProcessItemAsync(item, cancellationToken));
                    processedCount++;

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
                    if (processedCount > 0)
                    {
                        Log.Debug("Queue empty after processing {ProcessedCount} items. Waiting for new items...", processedCount);
                        processedCount = 0;
                    }
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
            Log.Information("ProcessQueueAsync ending - waiting for {TaskCount} remaining tasks", tasks.Count);
            await Task.WhenAll(tasks);
            Log.Information("ProcessQueueAsync completed");
        }
    }

    private static async Task CompletionTaskAsync(List<Task> tasks)
    {
        await Task.WhenAny(tasks);
        tasks.RemoveAll(t => t.IsCompleted);
    }

    private async Task ProcessItemAsync(UpdateUserQueueItem item, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            Log.Debug("Processing user {UserId}", item.UserId);
            await _updateService.UpdateUser(item);
            Log.Debug("Successfully processed user {UserId}", item.UserId);
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
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _semaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}
