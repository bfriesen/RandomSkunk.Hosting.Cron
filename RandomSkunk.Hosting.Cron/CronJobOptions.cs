using Cronos;
using System.Diagnostics.CodeAnalysis;

namespace RandomSkunk.Hosting.Cron;

/// <summary>
/// Defines options for cron jobs.
/// </summary>
public class CronJobOptions
{
    /// <summary>
    /// Gets or sets the cron expression of the cron job. This property must be initialized to a non-empty value before the cron
    /// job starts. See the <a href="https://github.com/HangfireIO/Cronos?tab=readme-ov-file#cron-format">Cronos
    /// documentation</a> for information about the format of cron expressions.
    /// <para>
    /// To use more than one cron expression for the cron job, this value should consist of a semicolon delimited list of cron
    /// expressions, e.g. <c>"0 23 * * SUN-THU; 0 1 * * SAT-SUN"</c>. In this case, when the cron job determines its next
    /// occurence time, each cron expression is evaluated for its next occurence time and one closest to "now" is selected.
    /// </para>
    /// </summary>
    [DisallowNull]
    public string? CronExpression { get; set; }

    /// <summary>
    /// Gets or sets the time zone of the cron job. When setting this property, valid values are null, "UTC", "Local", or the ID
    /// of a system time zone.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Sets the time zone of the cron job.
    /// </summary>
    /// <param name="timeZone">The <see cref="TimeZoneInfo"/> of the cron job.</param>
    /// <returns>The same instance of <see cref="CronJobOptions"/>.</returns>
    public CronJobOptions SetTimeZone(TimeZoneInfo? timeZone)
    {
        TimeZone = timeZone?.Id;
        return this;
    }

    internal bool HasDifferentTimeZoneThan(TimeZoneInfo otherTimeZone)
    {
        try
        {
            return !GetTimeZone().Equals(otherTimeZone);
        }
        catch
        {
            return true;
        }
    }

    internal TimeZoneInfo GetTimeZone() =>
        TimeZone?.ToUpperInvariant() switch
        {
            "UTC" => TimeZoneInfo.Utc,
            "LOCAL" or null => TimeZoneInfo.Local,
            _ => TimeZoneInfo.FindSystemTimeZoneById(TimeZone),
        };
}
