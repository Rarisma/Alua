using Serilog;

namespace Alua.Services;

/// <summary>
/// Provides semaphore-based throttling for API calls with configurable concurrency.
/// Based on the existing HLTB fetching pattern in GameList.xaml.cs.
/// </summary>
public class RateLimitedExecutor : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly string _name;
    private bool _disposed;

    public RateLimitedExecutor(int maxConcurrency, string name = "RateLimitedExecutor")
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _name = name;
    }

    /// <summary>
    /// Executes a single operation with rate limiting.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Executes multiple operations in parallel with rate limiting.
    /// </summary>
    public async Task<T[]> ExecuteAllAsync<TSource, T>(
        IEnumerable<TSource> items,
        Func<TSource, CancellationToken, Task<T>> operation,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var itemList = items.ToList();
        var totalCount = itemList.Count;
        var completedCount = 0;

        var tasks = itemList.Select(async item =>
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await operation(item, cancellationToken);
                var current = Interlocked.Increment(ref completedCount);
                progressCallback?.Invoke(current, totalCount);
                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Executes multiple operations in parallel with rate limiting, allowing null results.
    /// </summary>
    public async Task<T?[]> ExecuteAllWithNullableAsync<TSource, T>(
        IEnumerable<TSource> items,
        Func<TSource, CancellationToken, Task<T?>> operation,
        Action<int, int>? progressCallback = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var itemList = items.ToList();
        var totalCount = itemList.Count;
        var completedCount = 0;

        var tasks = itemList.Select(async item =>
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await operation(item, cancellationToken);
                var current = Interlocked.Increment(ref completedCount);
                progressCallback?.Invoke(current, totalCount);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warning(ex, "Operation failed in {Executor}", _name);
                Interlocked.Increment(ref completedCount);
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
