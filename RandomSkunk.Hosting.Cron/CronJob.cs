using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace RandomSkunk.Hosting.Cron;

/// <summary>
/// Base class for implementing an <see cref="IHostedService"/> that performs work according to a cron schedule. Cron support is
/// provided by the <a href="https://github.com/HangfireIO/Cronos">Cronos</a> nuget package.
/// </summary>
public abstract partial class CronJob : IHostedService, IDisposable
{
    private const string _spacesOrTabsPattern = @"[ \t]+";

    private readonly CronExpression _cronExpression;
    private readonly string? _cronExpressionLiteral;
    private readonly ILogger? _logger;
    private readonly TimeZoneInfo _timeZoneInfo;

    private CancellationTokenSource? _stoppingCts;
    private Task? _currentCronJobTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class with the specified <see cref="CronExpression"/>. See the
    /// <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#usage">Cronos documentation</a> for information about
    /// creating instances of <see cref="CronExpression"/>.
    /// </summary>
    /// <param name="cronExpression">A <see cref="CronExpression"/> that represents the schedule of the service. See the
    ///     <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#usage">Cronos documentation</a> for information
    ///     about creating instances of <see cref="CronExpression"/>.</param>
    /// <param name="logger">An optional <see cref="ILogger"/>.</param>
    /// <param name="timeZoneInfo">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling
    ///     is concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    protected CronJob(CronExpression cronExpression, ILogger? logger = null, TimeZoneInfo? timeZoneInfo = null)
    {
        _cronExpression = cronExpression ?? throw new ArgumentNullException(nameof(cronExpression));
        _logger = logger;
        _timeZoneInfo = timeZoneInfo ?? TimeZoneInfo.Local;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class with the specified cron expression. See the
    /// <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for information
    /// about the format of cron expressions.
    /// </summary>
    /// <param name="cronExpression">A cron expression that represents the schedule of the service. See the
    ///     <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for
    ///     information about the format of cron expressions.</param>
    /// <param name="logger">An optional <see cref="ILogger"/>.</param>
    /// <param name="timeZoneInfo">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling
    ///     is concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    protected CronJob(string cronExpression, ILogger? logger = null, TimeZoneInfo? timeZoneInfo = null)
        : this(ParseCronExpression(cronExpression), logger, timeZoneInfo)
    {
        _cronExpressionLiteral = cronExpression;
    }

    /// <inheritdoc/>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        // Create linked token to allow cancelling the cron job task from the provided token.
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob(_stoppingCts.Token);

        // If the task is completed then return it, this will bubble cancellation and failure to the caller.
        if (_currentCronJobTask.IsCompleted)
            return _currentCronJobTask;

        // Otherwise it's running.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop called without start.
        if (_currentCronJobTask == null)
            return;

        try
        {
            // Signal cancellation to the executing method.
            _stoppingCts!.Cancel();
        }
        finally
        {
            // Wait until the task completes or the stop token triggers.
            await Task.WhenAny(_currentCronJobTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        _stoppingCts?.Cancel();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// This method is called each time work is scheduled to run. The implementation should return a task that represents the
    /// asynchronous scheduled operation.
    /// </summary>
    /// <param name="cancellationToken">Triggered when the service is stopping or the start process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous scheduled operation.</returns>
    protected abstract Task DoWork(CancellationToken cancellationToken);

    private static CronExpression ParseCronExpression(string cronExpression)
    {
        if (string.IsNullOrEmpty(cronExpression))
            throw new ArgumentNullException(nameof(cronExpression));

        return CronExpression.Parse(cronExpression, GetCronFormat(cronExpression));
    }

    private static CronFormat GetCronFormat(string cronExpression) =>
        SpacesOrTabsRegex().Matches(cronExpression).Count == 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;

    private async Task ExecuteNextCronJob(CancellationToken cancellationToken)
    {
        // Do the small amount of cpu-bound housekeeping work first, before any await calls.
        var nextOccurrence = _cronExpression.GetNextOccurrence(DateTimeOffset.Now, _timeZoneInfo);
        if (nextOccurrence is null)
        {
            _logger?.LogWarning(
                "The cron expression '{CronExpression}' is unreachable and the '{Type}' cron job will never be scheduled.",
                (object?)_cronExpressionLiteral ?? _cronExpression,
                GetType());

            return;
        }

        var delay = (int)(nextOccurrence.Value - DateTimeOffset.Now).TotalMilliseconds;

        // Last chance to gracefully handle cancellation before the end of the synchronous section.
        if (cancellationToken.IsCancellationRequested)
            return;

        if (delay >= 1)
        {
            // Ensure that we only call Task.Delay with a positive number, otherwise it returns a completed task and the await
            // will not yield back to the caller. If this happens enough times and DoWork is implemented synchronously, then the
            // service will eventually throw an unrecoverable stack overflow exception.
            try
            {
                // Wait until the delay time is over or the stop token triggers.
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // The service is stopping or the start process was aborted; return immediately.
                return;
            }
        }
        else
        {
            // If there is no delay, directly yield to the caller instead. This prevents the stack overflow exception described
            // above.
            await Task.Yield();
        }

        try
        {
            // Do the actual work of the cron job.
            await DoWork(cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // The service is stopping or the start process was aborted; return immediately.
            return;
        }
        catch (Exception ex)
        {
            // Log the exception if a logger was provided.
            _logger?.LogError(
                ex,
                "An exception was thrown while running the scheduled '{Type}' cron job.",
                GetType());
        }

        // Last chance to gracefully handle cancellation before making the recursive call.
        if (cancellationToken.IsCancellationRequested)
            return;

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob(cancellationToken);
    }

#pragma warning disable SA1204 // Static elements should appear before instance elements
#if NET7_0_OR_GREATER
    [GeneratedRegex(_spacesOrTabsPattern)]
    private static partial Regex SpacesOrTabsRegex();
#else
    private static Regex SpacesOrTabsRegex() => SpacesOrTabs_Regex.Instance;

    private static class SpacesOrTabs_Regex
    {
        public static readonly Regex Instance = new(_spacesOrTabsPattern, RegexOptions.Compiled);
    }
#endif
#pragma warning restore SA1204 // Static elements should appear before instance elements
}
