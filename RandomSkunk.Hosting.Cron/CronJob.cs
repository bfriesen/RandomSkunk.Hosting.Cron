using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;
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

    private readonly IDisposable? _optionsReloadToken;
    private readonly ILogger _logger;
    private readonly bool _runAtStartup;

    private CronExpression[] _cronExpressions;
    private string _rawExpression;
    private TimeZoneInfo _timeZone;

    /// <summary>
    /// Triggered when stopping the service. Linked to the <see cref="StartAsync"/> method's cancellation token.
    /// </summary>
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    /// Triggered when reloading the cron job's settings. Linked to <see cref="_stoppingCts"/> (triggered when stopping the
    /// service) and the <see cref="StartAsync"/> method's cancellation token.
    /// </summary>
    private CancellationTokenSource? _reloadingCts;

    private Task? _currentCronJobTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class. The name of its monitored <see cref="CronJobOptions"/> is
    /// the <see cref="Type.FullName"/> of the <see cref="Type"/> of the new instance.
    /// </summary>
    /// <param name="optionsMonitor">The <see cref="IOptionsMonitor{TOptions}"/> that monitors the cron job's
    ///     <see cref="CronJobOptions"/>.</param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="optionsMonitor"/> is null.</exception>
    /// <exception cref="InvalidOperationException">If either of the configured 'CronExpression' or 'TimeZone' settings is
    ///     invalid.</exception>
    protected CronJob(IOptionsMonitor<CronJobOptions> optionsMonitor, ILogger? logger = null)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _logger = logger ?? new NullLogger();
        var cronJobOptions = optionsMonitor.Get(GetType().GetFullName());
        _runAtStartup = cronJobOptions.RunAtStartup;
        LoadSettings(cronJobOptions);
        _optionsReloadToken = optionsMonitor.OnChange(ReloadSettingsAndRestartBackgroundTask);

        async void ReloadSettingsAndRestartBackgroundTask(CronJobOptions options, string? optionsName)
        {
            // Make sure we're looking at the right options.
            if (optionsName != GetType().GetFullName())
                return;

            // Reload the settings. If nothing changed, don't restart the background task.
            if (!LoadSettings(options))
                return;

            // If the options change before the service starts, don't restart the background task.
            if (!IsStarted)
                return;

            // Signal "reloading" cancellation to the currently running ExecuteNextCronJob method.
            _reloadingCts.Cancel();

            // Wait until the current cron job task completes.
            await _currentCronJobTask.ConfigureAwait(false);

            // Recreate the reloading token.
            _reloadingCts.Dispose();
            _reloadingCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);

            // Start and store (but don't await) the next cron job task.
            _currentCronJobTask = ExecuteNextCronJob();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class with the specified cron expression. See the
    /// <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for information
    /// about the format of cron expressions.
    /// </summary>
    /// <param name="cronExpression">A cron expression that represents the schedule of the service. See the
    ///     <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for
    ///     information about the format of cron expressions.
    ///     <para>
    ///     To use more than one cron expression for the cron job, this value should consist of a semicolon delimited list of
    ///     cron expressions, e.g. <c>"0 23 * * SUN-THU; 0 1 * * SAT-SUN"</c>. In this case, when the cron job determines its
    ///     next occurence time, each cron expression is evaluated for its next occurence time and one closest to "now" is
    ///     selected.
    ///     </para>
    /// </param>
    /// <param name="logger">An optional <see cref="ILogger"/>.</param>
    /// <param name="timeZone">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling is
    ///     concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    /// <param name="runAtStartup">Whether the cron job will run immediately at startup, regardless of the next scheduled time.
    ///     </param>
    /// <exception cref="ArgumentNullException">If <paramref name="cronExpression"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">If <paramref name="cronExpression"/> is invalid.</exception>
    protected CronJob(string cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null, bool runAtStartup = false)
    {
        if (string.IsNullOrEmpty(cronExpression))
            throw new ArgumentNullException(nameof(cronExpression));

        _logger = logger ?? new NullLogger();
        _runAtStartup = runAtStartup;
        LoadSettings(new CronJobOptions { CronExpression = cronExpression, TimeZone = timeZone?.Id });

        // Opt out of reloading for this constructor.
        _optionsReloadToken = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class with the specified cron expression. See the
    /// <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for information
    /// about the format of cron expressions.
    /// </summary>
    /// <param name="cronExpression">A cron expression that represents the schedule of the service. See the
    ///     <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos documentation</a> for
    ///     information about the format of cron expressions.
    ///     <para>
    ///     To use more than one cron expression for the cron job, this value should consist of a semicolon delimited list of
    ///     cron expressions, e.g. <c>"0 23 * * SUN-THU; 0 1 * * SAT-SUN"</c>. In this case, when the cron job determines its
    ///     next occurence time, each cron expression is evaluated for its next occurence time and one closest to "now" is
    ///     selected.
    ///     </para>
    /// </param>
    /// <param name="logger">An optional <see cref="ILogger"/>.</param>
    /// <param name="timeZone">An optional <see cref="TimeZoneInfo"/> the defines when a day starts as far as cron scheduling is
    ///     concerned. Default value is <see cref="TimeZoneInfo.Local"/>.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="cronExpression"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">If <paramref name="cronExpression"/> is invalid.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    protected CronJob(string cronExpression, ILogger? logger, TimeZoneInfo? timeZone)
        : this(cronExpression, logger, timeZone, false)
    {
    }

    [MemberNotNullWhen(true, nameof(_currentCronJobTask), nameof(_stoppingCts), nameof(_reloadingCts))]
    private bool IsStarted => _currentCronJobTask is not null;

    /// <inheritdoc/>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug(843635087, "Starting cron job service...");

        // Link the stopping token to the provided token.
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Link the reloading token to the stopping token, which is linked to the provided token.
        _reloadingCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);

        // Start and store (but don't await) the next cron job task.
        if (_runAtStartup)
            _currentCronJobTask = ExecuteCronJobAtStartup(_reloadingCts.Token);
        else
            _currentCronJobTask = ExecuteNextCronJob();

        _logger.LogInformation(843635087, "Cron job service started.");

        // If the task is completed then return it, this will bubble cancellation and failure to the caller.
        if (_currentCronJobTask.IsCompleted)
            return _currentCronJobTask;

        // Otherwise it's running.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual async Task StopAsync(CancellationToken cancellationToken)
    {
        // Nothing to do if we're not running.
        if (!IsStarted)
            return;

        try
        {
            _logger.LogDebug(2034495701, "Stopping cron job service...");

            // Signal "stopping" cancellation to the currently running ExecuteNextCronJob method.
            _stoppingCts.Cancel();
        }
        finally
        {
            // Wait until the current cron job task completes or the StopAsync cancellation token triggers.
            await Task.WhenAny(_currentCronJobTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            _logger.LogInformation(369419253, "Cron job service stopped.");
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

    private async Task ExecuteCronJobAtStartup(CancellationToken cancellationToken)
    {
        await Task.Yield();

        _logger.LogDebug(314633121, "Executing startup job...");

        try
        {
            // Do the work of the cron job.
            await DoWork(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(1060256648, "Startup job complete.");
        }
        catch (TaskCanceledException)
        {
            // The service is stopping or the start process was aborted; return immediately.
            return;
        }
        catch (Exception ex)
        {
            LogExceptionThrownWhileRunningCronJob(ex);
        }

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();
    }

    private async Task ExecuteNextCronJob()
    {
        Debug.Assert((_stoppingCts, _reloadingCts) is (not null, not null), $"{nameof(_stoppingCts)} and {nameof(_reloadingCts)} must be initialized before calling {nameof(ExecuteNextCronJob)}.");

        // Do the small amount of cpu-bound housekeeping work first, before any await calls.
        var now = DateTimeOffset.Now;
        var nextOccurrence = _cronExpressions
            .Select(cronExpression => cronExpression.GetNextOccurrence(now, _timeZone))
            .Where(occurrence => occurrence.HasValue)
            .OrderBy(occurrence => occurrence)
            .FirstOrDefault();

        if (nextOccurrence is null)
        {
            LogCronJobWillNeverBeScheduled(_rawExpression);
            return;
        }

        LogCronJobIsScheduledToRunNextAt(nextOccurrence.Value);

        // Last chance to gracefully handle cancellation before the end of the synchronous section.
        if (_reloadingCts!.Token.IsCancellationRequested)
            return;

        try
        {
            await WaitForNextOccurrence(nextOccurrence.Value, _reloadingCts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // The service is stopping or the start process was aborted; return immediately.
            return;
        }

        try
        {
            // Do the actual work of the cron job.
            await DoWork(_stoppingCts!.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // The service is stopping or the start process was aborted; return immediately.
            return;
        }
        catch (Exception ex)
        {
            LogExceptionThrownWhileRunningCronJob(ex);
        }

        // Last chance to gracefully handle cancellation before making the recursive call.
        if (_reloadingCts.Token.IsCancellationRequested)
            return;

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();

        async Task WaitForNextOccurrence(DateTimeOffset nextOccurrence, CancellationToken cancellationToken)
        {
            const double timeThreshold = 1.2;
            var delayed = false;

            while ((nextOccurrence - DateTimeOffset.Now).TotalDays > timeThreshold)
            {
                LogDelayingOneDay(nextOccurrence);
                await Task.Delay(TimeSpan.FromDays(1), cancellationToken).ConfigureAwait(false);
                delayed = true;
            }

            while ((nextOccurrence - DateTimeOffset.Now).TotalHours > timeThreshold)
            {
                LogDelayingOneHour(nextOccurrence);
                await Task.Delay(TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
                delayed = true;
            }

            while ((nextOccurrence - DateTimeOffset.Now).TotalMinutes > timeThreshold)
            {
                LogDelayingOneMinute(nextOccurrence);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                delayed = true;
            }

            while ((nextOccurrence - DateTimeOffset.Now).TotalSeconds > timeThreshold)
            {
                LogDelayingOneSecond(nextOccurrence);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                delayed = true;
            }

            var delayMilliseconds = (int)Math.Round((nextOccurrence - DateTimeOffset.Now).TotalMilliseconds);
            if (delayMilliseconds > 0)
            {
                LogDelayingFinalMilliseconds(delayMilliseconds, nextOccurrence);
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                delayed = true;
            }

            if (!delayed)
            {
                // If there was no delay, make sure we yield back to the caller. This prevents a potential stack overflow exception.
                await Task.Yield();
            }
        }
    }

    [MemberNotNull(nameof(_cronExpressions), nameof(_rawExpression), nameof(_timeZone))]
    private bool LoadSettings(CronJobOptions options) => LoadCronExpression(options) | LoadTimeZone(options);

    [MemberNotNull(nameof(_cronExpressions), nameof(_rawExpression))]
    private bool LoadCronExpression(CronJobOptions options)
    {
        if (IsNullOrWhiteSpace(options.CronExpression))
        {
            if (_cronExpressions is null || _rawExpression is null)
                throw new InvalidOperationException("The configured 'CronExpression' setting must not be null or whitespace.");

            _logger.LogWarning(
                1942793153,
                "Unable to reload the cron expression: the 'CronExpression' setting is null or empty. The current value, '{CurrentCronExpression}', remains unchanged.",
                _rawExpression);
        }
        else if (_cronExpressions is null || _rawExpression is null || options.CronExpression != _rawExpression)
        {
            CronExpression[]? cronExpressions;

            try
            {
                cronExpressions = options.CronExpression.Split(';')
                    .Select(expression => expression.Trim())
                    .Where(expression => expression != string.Empty)
                    .Select(expression => CronExpression.Parse(expression, GetCronFormat(expression)))
                    .ToArray();
            }
            catch (Exception ex)
            {
                if (_cronExpressions is null || _rawExpression is null)
                    throw new InvalidOperationException($"There was a problem with the configured 'CronExpression' setting, '{options.CronExpression}'. See the inner exception for details.", ex);

                _logger.LogWarning(
                    1884538533,
                    ex,
                    "There was a problem with the new 'CronExpression' setting, '{InvalidCronExpression}'. The current value, '{CurrentCronExpression}', remains unchanged.",
                    options.CronExpression,
                    _rawExpression);

                return false;
            }

            if (cronExpressions.Length > 0)
            {
                var previousRawExpression = _rawExpression;
                _cronExpressions = cronExpressions;
                _rawExpression = options.CronExpression;

                if (previousRawExpression is null)
                    _logger.LogDebug(583760094, "Cron expression set to '{Expression}'.", _rawExpression);
                else
                    _logger.LogInformation(1197508750, "Cron expression changed from '{PreviousExpression}' to '{NewExpression}'.", previousRawExpression, _rawExpression);

                return true;
            }

            if (_cronExpressions is null || _rawExpression is null)
                throw new InvalidOperationException("The configured 'CronExpression' setting does not actually contain any cron expressions.");

            _logger.LogWarning(
                785492755,
                "Unable to reload the cron expression: the 'CronExpression' setting does not actually contain any cron expressions. The current value, '{CurrentCronExpression}', remains unchanged.",
                _rawExpression);
        }

        return false;

        static CronFormat GetCronFormat(string cronExpression) =>
            SpacesOrTabsRegex().Matches(cronExpression).Count >= 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;

        // This exists to fix a compiler warning in .NET Standard 2.0 and .NET Framework 4.6.2.
        static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value) =>
            string.IsNullOrWhiteSpace(value);
    }

    [MemberNotNull(nameof(_timeZone))]
    private bool LoadTimeZone(CronJobOptions options)
    {
        if (_timeZone is null || options.HasDifferentTimeZoneThan(_timeZone))
        {
            try
            {
                var previousTimeZone = _timeZone;

                _timeZone = options.GetTimeZone();

                if (previousTimeZone is null)
                    _logger.LogDebug(679945320, "Cron time zone set to '{TimeZone}'.", _timeZone.Id);
                else
                    _logger.LogInformation(827525297, "Cron time zone changed from '{PreviousTimeZone}' to '{NewTimeZone}'.", previousTimeZone.Id, _timeZone.Id);

                return true;
            }
            catch (Exception ex)
            {
                if (_timeZone is null)
                    throw new InvalidOperationException($"The 'TimeZone' setting contains an invalid value, '{options.TimeZone}'.", ex);

                _logger.LogWarning(
                    1218376460,
                    ex,
                    "Unable to reload the cron time zone: the 'TimeZone' setting contains an invalid value, '{InvalidTimeZone}'. The current value, '{CurrentTimeZone}', remains unchanged.",
                    options.TimeZone,
                    _timeZone.Id);
            }
        }

        return false;
    }

    [LoggerMessage(LogLevel.Debug, "The cron job is scheduled to run next at {NextOccurrence:G}.")]
    private partial void LogCronJobIsScheduledToRunNextAt(DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Warning, "The cron expression '{CronExpression}' is unreachable and the cron job will never be scheduled.")]
    private partial void LogCronJobWillNeverBeScheduled(string cronExpression);

    [LoggerMessage(LogLevel.Trace, "Delaying one day while waiting for the next job at {NextOccurrence:G}.")]
    private partial void LogDelayingOneDay(DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Trace, "Delaying one hour while waiting for the next job at {NextOccurrence:G}.")]
    private partial void LogDelayingOneHour(DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Trace, "Delaying one minute while waiting for the next job at {NextOccurrence:G}.")]
    private partial void LogDelayingOneMinute(DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Trace, "Delaying one second while waiting for the next job at {NextOccurrence:G}.")]
    private partial void LogDelayingOneSecond(DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Trace, "Delaying final {DelayMilliseconds} milliseconds while waiting for the next job at {NextOccurrence:G}.")]
    private partial void LogDelayingFinalMilliseconds(int delayMilliseconds, DateTimeOffset nextOccurrence);

    [LoggerMessage(LogLevel.Error, "An exception was thrown while running the cron job.")]
    private partial void LogExceptionThrownWhileRunningCronJob(Exception exception);

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

    private sealed class NullLogger : ILogger
    {
        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        bool ILogger.IsEnabled(LogLevel logLevel) => false;

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
