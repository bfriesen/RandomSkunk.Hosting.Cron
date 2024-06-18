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

    private readonly IDisposable? _optionsReloadToken;
    private readonly ILogger? _logger;

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

        LoadSettings(optionsMonitor.Get(GetType().GetFullName()));
        _optionsReloadToken = optionsMonitor.OnChange(ReloadSettingsAndRestartBackgroundTask);
        _logger = logger;
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
    /// <exception cref="ArgumentException">If <paramref name="cronExpression"/> is invalid.</exception>
    protected CronJob(string cronExpression, ILogger? logger = null, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrEmpty(cronExpression))
            throw new ArgumentNullException(nameof(cronExpression));

        LoadSettings(new CronJobOptions { CronExpression = cronExpression, TimeZone = timeZone?.Id });
        _logger = logger;

        // Opt out of reloading for this constructor.
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
        var now = DateTimeOffset.Now;
        var nextOccurrence = _cronExpressions
            .Select(cronExpression => cronExpression.GetNextOccurrence(now, _timeZone))
            .Where(occurrence => occurrence.HasValue)
            .OrderBy(occurrence => occurrence)
            .FirstOrDefault();
        if (nextOccurrence is null)
        {
            _logger?.LogWarning(
                -1749327138,
                "The cron expression '{CronExpression}' is unreachable and the cron job will never be scheduled.",
                _rawExpression);

            return;
        }

        _logger?.LogDebug(-159904559, "The cron job is scheduled to run next at {NextOccurrence:G}.", nextOccurrence);

        // Last chance to gracefully handle cancellation before the end of the synchronous section.
        if (_reloadingCts!.Token.IsCancellationRequested)
            return;

        // In order to increase overall the accuracy of the delay, perform the bulk of the waiting one second at a time.
        while ((nextOccurrence.Value - DateTimeOffset.Now).TotalMilliseconds > 1000)
        {
            _logger?.LogTrace(-1922763774, "Delaying one second...");

            try
            {
                await Task.Delay(1000, _reloadingCts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // The service is stopping or the start process was aborted; return immediately.
                return;
            }
        }

        // Wait for the last fraction of a second.
        var delay = (int)Math.Round((nextOccurrence.Value - DateTimeOffset.Now).TotalMilliseconds);
        if (delay > 0)
        {
            _logger?.LogTrace(-148452585, "Delaying remaining {DelayMilliseconds} milliseconds...", delay);

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
            // If there was no delay, make sure we yield back to the caller. This prevents a potential stack overflow exception.
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
            _logger?.LogError(-631620540, ex, "An exception was thrown while running the scheduled cron job.");
        }

        // Last chance to gracefully handle cancellation before making the recursive call.
        if (_reloadingCts.Token.IsCancellationRequested)
            return;

        // Start and store (but don't await) the next cron job task.
        _currentCronJobTask = ExecuteNextCronJob();
    }

    [MemberNotNull(nameof(_cronExpressions), nameof(_rawExpression), nameof(_timeZone))]
    private bool LoadSettings(CronJobOptions options)
    {
        var settingsChanged = false;

        if (IsNullOrWhiteSpace(options.CronExpression))
        {
            if (_cronExpressions is null || _rawExpression is null)
                throw new ArgumentException("The 'CronExpression' setting must not be null or empty.", nameof(options));

            _logger?.LogWarning(
                1942793153,
                "Unable to reload the cron expression: the 'CronExpression' setting is null or empty. The current value, '{CurrentCronExpression}', remains unchanged.",
                _rawExpression);
        }
        else if (_cronExpressions is null || _rawExpression is null || options.CronExpression != _rawExpression)
        {
            try
            {
                var previousRawExpression = _rawExpression;

                _cronExpressions = options.CronExpression
#if NET6_0_OR_GREATER
                    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
#else
                    .Split(';')
                    .Select(expression => expression.Trim())
                    .Where(expression => expression != string.Empty)
#endif
                    .Select(expression => CronExpression.Parse(expression, GetCronFormat(expression)))
                    .ToArray();
                _rawExpression = options.CronExpression;
                settingsChanged = true;

                if (previousRawExpression is null)
                    _logger?.LogDebug(-583760094, "Cron expression set to '{Expression}'.", _rawExpression);
                else
                    _logger?.LogInformation(1197508750, "Cron expression changed from '{PreviousExpression}' to '{NewExpression}'.", previousRawExpression, _rawExpression);
            }
            catch (Exception ex)
            {
                if (_cronExpressions is null || _rawExpression is null)
                    throw new ArgumentException($"The 'CronExpression' setting contains an invalid value, '{options.CronExpression}'.", nameof(options), ex);

                _logger?.LogWarning(
                    1884538533,
                    ex,
                    "Unable to reload the cron expression: the 'CronExpression' setting contains an invalid value, '{InvalidCronExpression}'. The current value, '{CurrentCronExpression}', remains unchanged.",
                    options.CronExpression,
                    _rawExpression);
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

        // This exists to fix a compiler warning in .NET Standard 2.0 and .NET Framework 4.6.2.
        static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);
    }

    private async void ReloadSettingsAndRestartBackgroundTask(CronJobOptions options, string? optionsName)
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
