using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace RandomSkunk.Hosting.Cron;

/// <summary>
/// Base class for implementing an <see cref="IHostedService"/> that performs work according to a cron schedule. Cron support is
/// provided by the <a href="https://github.com/HangfireIO/Cronos">Cronos</a> nuget package.
/// </summary>
public abstract partial class CronJob : IHostedService, IDisposable
{
    [StringSyntax(StringSyntaxAttribute.Regex)]
    private const string _spacesOrTabsPattern = @"[ \t]+";

    private readonly string? _cronJobOptionsName;
    private readonly IDisposable? _optionsReloadToken;
    private readonly ILogger? _logger;

    private CronExpression _cronExpression;
    private string? _cronExpressionLiteral;
    private TimeZoneInfo _timeZone;

    private CancellationTokenSource? _stoppingCts;
    private Task? _currentCronJobTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class using the <see cref="CronJobOptions"/> named
    /// <paramref name="cronJobOptionsName"/> that are monitored by <paramref name="optionsMonitor"/>.
    /// </summary>
    /// <param name="optionsMonitor">The <see cref="IOptionsMonitor{TOptions}"/> that monitors the <see cref="CronJobOptions"/>
    ///     for the cron job. The options it monitors are named <paramref name="cronJobOptionsName"/>.</param>
    /// <param name="cronJobOptionsName">The name of the <see cref="CronJobOptions"/> that <paramref name="optionsMonitor"/>
    ///     monitors.</param>
    /// <param name="logger">An optional logger.</param>
    protected CronJob(IOptionsMonitor<CronJobOptions> optionsMonitor, string cronJobOptionsName, ILogger? logger = null)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _cronJobOptionsName = cronJobOptionsName;
        SetCronExpressionAndTimeZone(optionsMonitor.Get(_cronJobOptionsName));
        _optionsReloadToken = optionsMonitor.OnChange(ReloadCronExpressionAndTimeZone);
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class using the <see cref="CronJobOptions"/> named
    /// '<c>this.GetType().Name</c>' that are monitored by <paramref name="optionsMonitor"/>.
    /// </summary>
    /// <param name="optionsMonitor">The <see cref="IOptionsMonitor{TOptions}"/> that monitors the <see cref="CronJobOptions"/>
    ///     for the cron job. The options it monitors are named after this instance's type name.</param>
    /// <param name="logger">An optional logger.</param>
    protected CronJob(IOptionsMonitor<CronJobOptions> optionsMonitor, ILogger? logger = null)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _cronJobOptionsName = GetType().Name;
        SetCronExpressionAndTimeZone(optionsMonitor.Get(_cronJobOptionsName));
        _optionsReloadToken = optionsMonitor.OnChange(ReloadCronExpressionAndTimeZone);
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class with the specified <see cref="CronExpression"/>. See the
    /// <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#usage">Cronos documentation</a> for information about
    /// creating instances of <see cref="CronExpression"/>.
    /// </summary>
    /// <param name="cronExpression">A <see cref="CronExpression"/> that represents the schedule of the service. See the
    ///     <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#usage">Cronos documentation</a> for information
    ///     about creating instances of <see cref="CronExpression"/>.</param>
    /// <param name="logger">An optional <see cref="ILogger"/>.</param>
    /// <param name="timeZone">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling is
    ///     concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    protected CronJob(CronExpression cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        _cronJobOptionsName = null;
        _optionsReloadToken = null;

        _cronExpression = cronExpression ?? throw new ArgumentNullException(nameof(cronExpression));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger;
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
    /// <param name="timeZone">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling is
    ///     concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    protected CronJob(string cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        if (cronExpression.IsNullOrEmpty())
            throw new ArgumentNullException(nameof(cronExpression));

        _cronJobOptionsName = null;
        _optionsReloadToken = null;

        SetCronExpressionAndTimeZone(new CronJobOptions { CronExpression = cronExpression, TimeZone = timeZone?.Id });
        _logger = logger;
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
        _optionsReloadToken?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// This method is called each time work is scheduled to run. The implementation should return a task that represents the
    /// asynchronous scheduled operation.
    /// </summary>
    /// <param name="cancellationToken">Triggered when the service is stopping or the start process has been aborted.</param>
    /// <returns>A <see cref="Task"/> that represents the asynchronous scheduled operation.</returns>
    protected abstract Task DoWork(CancellationToken cancellationToken);

    private async Task ExecuteNextCronJob(CancellationToken cancellationToken)
    {
        // Do the small amount of cpu-bound housekeeping work first, before any await calls.
        var nextOccurrence = _cronExpression.GetNextOccurrence(DateTimeOffset.Now, _timeZone);
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

    [MemberNotNull(nameof(_cronExpression), nameof(_timeZone))]
    private void SetCronExpressionAndTimeZone(CronJobOptions options)
    {
        if (options.CronExpression.IsNullOrEmpty())
        {
            if (_cronExpression is null)
                throw new ArgumentException("The 'CronExpression' setting must not be null or empty.", nameof(options));

            _logger?.LogWarning("Unable to reload the cron expression: the 'CronExpression' setting is null or empty.");
        }
        else if (_cronExpression is null || options.CronExpression != _cronExpressionLiteral)
        {
            try
            {
                var previousCronExpressionLiteral = _cronExpressionLiteral;

                var cronFormat = options.CronFormat ?? GetCronFormat(options.CronExpression);
                _cronExpression = CronExpression.Parse(options.CronExpression, cronFormat);
                _cronExpressionLiteral = options.CronExpression;

                if (previousCronExpressionLiteral is null)
                    _logger?.LogInformation("Cron expression set to '{Expression}'.", _cronExpressionLiteral);
                else
                    _logger?.LogInformation("Cron expression changed from '{PreviousExpression}' to '{NewExpression}'.", previousCronExpressionLiteral, _cronExpressionLiteral);
            }
            catch (Exception ex)
            {
                if (_cronExpression is null)
                    throw new ArgumentException($"The 'CronExpression' setting contains an invalid value, '{options.CronExpression}'.", nameof(options), ex);

                _logger?.LogWarning(
                    ex,
                    "Unable to reload the cron expression: the 'CronExpression' setting contains an invalid value, '{InvalidCronExpression}'. The current value, '{CurrentCronExpression}', remains unchanged.",
                    options.CronExpression,
                    _cronExpressionLiteral);
            }
        }

        if (_timeZone is null || options.HasDifferentTimeZoneThan(_timeZone))
        {
            try
            {
                var previousTimeZone = _timeZone;

                _timeZone = options.GetTimeZone();

                if (previousTimeZone is null)
                    _logger?.LogInformation("Cron time zone set to '{TimeZone}'.", _timeZone.Id);
                else
                    _logger?.LogInformation("Cron time zone changed from '{PreviousTimeZone}' to '{NewTimeZone}'.", previousTimeZone.Id, _timeZone.Id);
            }
            catch (Exception ex)
            {
                if (_timeZone is null)
                    throw new ArgumentException($"The 'TimeZone' setting contains an invalid value, '{options.TimeZone}'.", nameof(options), ex);

                _logger?.LogWarning(
                    ex,
                    "Unable to reload the cron time zone: the 'TimeZone' setting contains an invalid value, '{InvalidTimeZone}'. The current value, '{CurrentTimeZone}', remains unchanged.",
                    options.TimeZone,
                    _timeZone.Id);
            }
        }

        static CronFormat GetCronFormat(string cronExpression) =>
            SpacesOrTabsRegex().Matches(cronExpression).Count == 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;
    }

    private void ReloadCronExpressionAndTimeZone(CronJobOptions options, string? optionsName)
    {
        if (optionsName == _cronJobOptionsName)
            SetCronExpressionAndTimeZone(options);
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
