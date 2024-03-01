using Cronos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private readonly string? _cronJobOptionsName;
    private readonly IDisposable? _optionsReloadToken;
    private readonly ILogger? _logger;

    private CronExpression _cronExpression;
    private TimeZoneInfo _timeZone;

    /// <summary>
    /// Triggered when stopping the service. Linked to the <see cref="StartAsync"/> method's cancellation token.
    /// </summary>
    private CancellationTokenSource? _stoppingCts;

    /// <summary>
    /// Triggered when reloading the cron job's settings. Linked to <see cref="_stoppingCts"/> and the <see cref="StartAsync"/>
    /// method's cancellation token.
    /// </summary>
    private CancellationTokenSource? _reloadingCts;

    private Task? _currentCronJobTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class using the monitored <see cref="CronJobOptions"/> named
    /// <paramref name="cronJobOptionsName"/>.
    /// </summary>
    /// <param name="optionsMonitor">The <see cref="IOptionsMonitor{TOptions}"/> that monitors the cron job's
    ///     <see cref="CronJobOptions"/>. The options it monitors are named <paramref name="cronJobOptionsName"/>.</param>
    /// <param name="cronJobOptionsName">The name of the <see cref="CronJobOptions"/> that <paramref name="optionsMonitor"/>
    ///     monitors.</param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="optionsMonitor"/> is null.</exception>
    protected CronJob(IOptionsMonitor<CronJobOptions> optionsMonitor, string cronJobOptionsName, ILogger? logger = null)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _cronJobOptionsName = cronJobOptionsName;
        LoadSettings(optionsMonitor.Get(_cronJobOptionsName));
        _optionsReloadToken = optionsMonitor.OnChange(ReloadSettingsAndRestartBackgroundTask);
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CronJob"/> class using the monitored <see cref="CronJobOptions"/> named this
    /// instance's type name.
    /// </summary>
    /// <param name="optionsMonitor">The <see cref="IOptionsMonitor{TOptions}"/> that monitors the cron job's
    ///     <see cref="CronJobOptions"/>. The options it monitors are named this instance's type name.</param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="optionsMonitor"/> is null.</exception>
    /// <exception cref="ArgumentException">If the <see cref="CronJobOptions.CronExpression"/> or
    ///     <see cref="CronJobOptions.TimeZone"/> properties are invalid.</exception>
    protected CronJob(IOptionsMonitor<CronJobOptions> optionsMonitor, ILogger? logger = null)
    {
        if (optionsMonitor is null)
            throw new ArgumentNullException(nameof(optionsMonitor));

        _cronJobOptionsName = GetType().Name;
        LoadSettings(optionsMonitor.Get(_cronJobOptionsName));
        _optionsReloadToken = optionsMonitor.OnChange(ReloadSettingsAndRestartBackgroundTask);
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
    /// <exception cref="ArgumentNullException">If <paramref name="cronExpression"/> is null.</exception>
    protected CronJob(CronExpression cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        _cronExpression = cronExpression ?? throw new ArgumentNullException(nameof(cronExpression));
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger;

        // Opt out of options and reloading for this constructor.
        _cronJobOptionsName = null;
        _optionsReloadToken = null;
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
    /// <exception cref="ArgumentNullException">If <paramref name="cronExpression"/> is null or empty.</exception>
    /// <exception cref="ArgumentException">If <paramref name="cronExpression"/> is invalid.</exception>
    protected CronJob(string cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrEmpty(cronExpression))
            throw new ArgumentNullException(nameof(cronExpression));

        LoadSettings(new CronJobOptions { CronExpression = cronExpression, TimeZone = timeZone?.Id });
        _logger = logger;

        // Opt out of options and reloading for this constructor.
        _cronJobOptionsName = null;
        _optionsReloadToken = null;
    }

    [MemberNotNullWhen(true, nameof(_currentCronJobTask), nameof(_stoppingCts), nameof(_reloadingCts))]
    private bool IsStarted => _currentCronJobTask is not null;

    /// <inheritdoc/>
    public virtual Task StartAsync(CancellationToken cancellationToken)
    {
        // Link the stopping token to the provided token.
        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Link the reloading token to the stopping token, which is linked to the provided token.
        _reloadingCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();

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
        if (!IsStarted)
            return;

        try
        {
            // Signal stopping cancellation to the executing method.
            _stoppingCts.Cancel();
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

    private async Task ExecuteNextCronJob()
    {
        Debug.Assert((_stoppingCts, _reloadingCts) is (not null, not null), $"{nameof(_stoppingCts)} and {nameof(_reloadingCts)} must be initialized before calling {nameof(ExecuteNextCronJob)}.");

        // Do the small amount of cpu-bound housekeeping work first, before any await calls.
        var nextOccurrence = _cronExpression.GetNextOccurrence(DateTimeOffset.Now, _timeZone);
        if (nextOccurrence is null)
        {
            _logger?.LogWarning(
                -1749327138,
                "The cron expression '{CronExpression}' is unreachable and the '{Type}' cron job will never be scheduled.",
                _cronExpression,
                GetType());

            return;
        }

        var delay = (int)(nextOccurrence.Value - DateTimeOffset.Now).TotalMilliseconds;

        // Last chance to gracefully handle cancellation before the end of the synchronous section.
        if (_reloadingCts!.Token.IsCancellationRequested)
            return;

        if (delay >= 1)
        {
            // Ensure that we only call Task.Delay with a positive number, otherwise it returns a completed task and the await
            // will not yield back to the caller. If this happens enough times and DoWork is implemented synchronously, then the
            // service will eventually throw an unrecoverable stack overflow exception.
            try
            {
                // Wait until the delay time is over or the stop or reload token triggers.
                await Task.Delay(delay, _reloadingCts.Token).ConfigureAwait(false);
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
            await DoWork(_stoppingCts!.Token).ConfigureAwait(false);
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
                -631620540,
                ex,
                "An exception was thrown while running the scheduled '{Type}' cron job.",
                GetType());
        }

        // Last chance to gracefully handle cancellation before making the recursive call.
        if (_reloadingCts.Token.IsCancellationRequested)
            return;

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();
    }

    [MemberNotNull(nameof(_cronExpression), nameof(_timeZone))]
    private bool LoadSettings(CronJobOptions options)
    {
        var settingsChanged = false;

        if (string.IsNullOrEmpty(options.CronExpression))
        {
            if (_cronExpression is null)
                throw new ArgumentException("The 'CronExpression' setting must not be null or empty.", nameof(options));

            _logger?.LogWarning(1942793153, "Unable to reload the cron expression: the 'CronExpression' setting is null or empty.");
        }
        else if (_cronExpression is null || options.CronExpression != _cronExpression.ToString())
        {
            try
            {
                var previousCronExpression = _cronExpression;

                var cronFormat = options.CronFormat ?? GetCronFormat(options.CronExpression!);
                _cronExpression = CronExpression.Parse(options.CronExpression, cronFormat);
                settingsChanged = true;

                if (previousCronExpression is null)
                    _logger?.LogDebug(-583760094, "Cron expression set to '{Expression}'.", _cronExpression);
                else
                    _logger?.LogInformation(1197508750, "Cron expression changed from '{PreviousExpression}' to '{NewExpression}'.", previousCronExpression, _cronExpression);
            }
            catch (Exception ex)
            {
                if (_cronExpression is null)
                    throw new ArgumentException($"The 'CronExpression' setting contains an invalid value, '{options.CronExpression}'.", nameof(options), ex);

                _logger?.LogWarning(
                    1884538533,
                    ex,
                    "Unable to reload the cron expression: the 'CronExpression' setting contains an invalid value, '{InvalidCronExpression}'. The current value, '{CurrentCronExpression}', remains unchanged.",
                    options.CronExpression,
                    _cronExpression);
            }
        }

        if (_timeZone is null || options.HasDifferentTimeZoneThan(_timeZone))
        {
            try
            {
                var previousTimeZone = _timeZone;

                _timeZone = options.GetTimeZone();
                settingsChanged = true;

                if (previousTimeZone is null)
                    _logger?.LogDebug(-679945320, "Cron time zone set to '{TimeZone}'.", _timeZone.Id);
                else
                    _logger?.LogInformation(827525297, "Cron time zone changed from '{PreviousTimeZone}' to '{NewTimeZone}'.", previousTimeZone.Id, _timeZone.Id);
            }
            catch (Exception ex)
            {
                if (_timeZone is null)
                    throw new ArgumentException($"The 'TimeZone' setting contains an invalid value, '{options.TimeZone}'.", nameof(options), ex);

                _logger?.LogWarning(
                    -1218376460,
                    ex,
                    "Unable to reload the cron time zone: the 'TimeZone' setting contains an invalid value, '{InvalidTimeZone}'. The current value, '{CurrentTimeZone}', remains unchanged.",
                    options.TimeZone,
                    _timeZone.Id);
            }
        }

        return settingsChanged;

        static CronFormat GetCronFormat(string cronExpression) =>
            SpacesOrTabsRegex().Matches(cronExpression).Count == 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;
    }

    private async void ReloadSettingsAndRestartBackgroundTask(CronJobOptions options, string? optionsName)
    {
        // Make sure we're looking at the right options.
        if (optionsName != _cronJobOptionsName)
            return;

        // Reload the settings. If nothing changed, don't restart the background task.
        if (!LoadSettings(options))
            return;

        // If the options change before the service starts, don't restart the background task.
        if (!IsStarted)
            return;

        // Signal reloading cancellation to the executing method.
        _reloadingCts.Cancel();

        // Wait until the task completes.
        await _currentCronJobTask.ConfigureAwait(false);

        // Recreate the reloading token.
        _reloadingCts.Dispose();
        _reloadingCts = CancellationTokenSource.CreateLinkedTokenSource(_stoppingCts.Token);

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();
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
