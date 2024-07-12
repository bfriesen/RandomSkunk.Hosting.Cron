# RandomSkunk.Hosting.Cron [![NuGet](https://img.shields.io/nuget/v/RandomSkunk.Hosting.Cron.svg)](https://www.nuget.org/packages/RandomSkunk.Hosting.Cron)

*An IHostedService base class that triggers on a cron schedule.*

---

RandomSkunk.Hosting.Cron provides a `IHostedService` base class that schedules work according to a cron schedule. Cron support is provided by the [Cronos](https://github.com/HangfireIO/Cronos) nuget package.

## Usage

To implement a cron job that loads its settings from configuration, inherit from the `CronJob` abstract class and create a constructor with an `IOptionsMonitor<CronJobOptions>` parameter. Pass this through to the base constructor.

*Note that a cron job created with options will automatically reload itself when the option's configuration reloads.*

```c#
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RandomSkunk.Hosting.Cron;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyNamespace;

public class MyCronJob : CronJob
{
    private readonly ILogger<MyCronJob> _logger;

    public MyCronJob(
        IOptionsMonitor<CronJobOptions> optionsMonitor,
        ILogger<MyCronJob> logger)
        : base(optionsMonitor, logger)
    {
        _logger = logger;
    }

    protected override async Task DoWork(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cron job work starting at: {time:G}", DateTimeOffset.Now);

        // Simulate work.
        await Task.Delay(Random.Shared.Next(250, 1000), cancellationToken);

        _logger.LogInformation("Cron job work finished at: {time:G}", DateTimeOffset.Now);
    }
}
```

Add a configuration section for the cron job. The section must have a 'CronExpression' setting.

```json
{
  "MyCronJob": {
    "CronExpression": "0 1 * * MON", // Scheduled for 1 AM EST every Monday.
    "TimeZone": "Eastern Standard Time" // Other values are "Local" and "UTC".
  }
}
```

Add the cron job the an application with the `AddCronJob` extension method. Pass it the configuration section that defines the
cron job.

```c#
services.AddCronJob<MyCronJob>(configuration.GetSection("MyCronJob"));
```

That's it. When you run your application, the cron job will fire every Monday at 1 AM.

---

The next example show a cron job that loads its settings directly from constructor parameters.

```c#
using Microsoft.Extensions.Logging;
using RandomSkunk.Hosting.Cron;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace MyNamespace;

public class AnotherCronJob : CronJob
{
    private readonly ILogger<AnotherCronJob> _logger;

    public AnotherCronJob(ILogger<AnotherCronJob> logger) // Scheduled for 8 AM PST Monday through Friday.
        : base("0 8 * * MON-FRI", logger, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
    {
        _logger = logger;
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Starting cron job at: {StartTime:G}", DateTime.Now);

        // Simulate work.
        await Task.Delay(Random.Shared.Next(250, 1000), stoppingToken);

        _logger.LogInformation("Cron job finished at: {EndTime:G}", DateTime.Now);
    }
}
```

To add this cron job to an application, use the regular `AddHostedService` extension method.

```c#
builder.Services.AddHostedService<AnotherCronJob>();
```
