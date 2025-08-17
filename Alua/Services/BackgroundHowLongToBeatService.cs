using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alua.Models;
using Alua.Services.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Dispatching;
using Serilog;

namespace Alua.Services;

/// <summary>
/// Background service for fetching HowLongToBeat data without blocking game scanning
/// </summary>
public class BackgroundHowLongToBeatService : IDisposable
{
    private readonly ConcurrentQueue<Game> _gameQueue = new();
    private readonly SemaphoreSlim _semaphore = new(3); // Limit concurrent requests to 3
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Serilog.ILogger _logger;
    private Task? _processingTask;
    private static BackgroundHowLongToBeatService? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Singleton instance of the background service
    /// </summary>
    public static BackgroundHowLongToBeatService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new BackgroundHowLongToBeatService();
                }
            }
            return _instance;
        }
    }

    private BackgroundHowLongToBeatService()
    {
        _logger = Log.ForContext<BackgroundHowLongToBeatService>();
        StartProcessing();
    }

    /// <summary>
    /// Queues a game for background HowLongToBeat data fetching
    /// </summary>
    public void QueueGame(Game game)
    {
        if (game == null || string.IsNullOrWhiteSpace(game.Name))
            return;

        // Skip if we already have recent data
        if (game.HowLongToBeatLastFetched.HasValue &&
            (DateTime.UtcNow - game.HowLongToBeatLastFetched.Value).TotalDays < 7)
        {
            _logger.Debug($"Skipping {game.Name} - already has recent HLTB data");
            return;
        }

        _gameQueue.Enqueue(game);
        _logger.Debug($"Queued {game.Name} for HLTB data fetching. Queue size: {_gameQueue.Count}");
    }

    /// <summary>
    /// Queues multiple games for background processing
    /// </summary>
    public void QueueGames(IEnumerable<Game> games)
    {
        foreach (var game in games)
        {
            QueueGame(game);
        }
    }

    private void StartProcessing()
    {
        _processingTask = Task.Run(async () =>
        {
            _logger.Information("Background HowLongToBeat processing started");
            
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_gameQueue.TryDequeue(out var game))
                    {
                        await _semaphore.WaitAsync(_cancellationTokenSource.Token);
                        
                        // Process in background without awaiting
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessGameAsync(game);
                            }
                            finally
                            {
                                _semaphore.Release();
                            }
                        }, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        // No games in queue, wait a bit
                        await Task.Delay(1000, _cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in background HLTB processing loop");
                    await Task.Delay(5000, _cancellationTokenSource.Token); // Wait before retrying
                }
            }
            
            _logger.Information("Background HowLongToBeat processing stopped");
        }, _cancellationTokenSource.Token);
    }

    private async Task ProcessGameAsync(Game game)
    {
        try
        {
            _logger.Information($"Fetching HLTB data for {game.Name}");
            
            using var hltbService = new HowLongToBeatService();
            await hltbService.FetchAndUpdateGameData(game);
            
            // Save the updated game data - dispatch to UI thread
            var settingsVm = Ioc.Default.GetService<SettingsVM>();
            if (settingsVm != null)
            {
                // Get the dispatcher from the main window
                var dispatcher = Microsoft.UI.Xaml.Window.Current?.DispatcherQueue;
                if (dispatcher != null)
                {
                    // Update on UI thread
                    dispatcher.TryEnqueue(() =>
                    {
                        settingsVm.AddOrUpdateGame(game);
                    });
                }
                else
                {
                    // Fallback if no dispatcher available
                    settingsVm.AddOrUpdateGame(game);
                }
                
                // Save can happen on background thread
                await settingsVm.Save();
            }
            
            _logger.Information($"Successfully fetched HLTB data for {game.Name}");
            
            // Small delay to avoid hitting rate limits
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to fetch HLTB data for {game.Name}");
        }
    }

    /// <summary>
    /// Gets the current queue size
    /// </summary>
    public int QueueSize => _gameQueue.Count;

    /// <summary>
    /// Waits for all queued games to be processed (with timeout)
    /// </summary>
    public async Task WaitForCompletionAsync(TimeSpan? timeout = null)
    {
        var timeoutTime = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));
        
        while (_gameQueue.Count > 0 && DateTime.UtcNow < timeoutTime)
        {
            await Task.Delay(500);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _processingTask?.Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource.Dispose();
        _semaphore.Dispose();
        _instance = null;
    }
}