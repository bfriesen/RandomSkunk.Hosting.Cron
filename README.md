# RandomSkunk.Hosting.Cron [![NuGet](https://img.shields.io/nuget/v/RandomSkunk.Hosting.Cron.svg)](https://www.nuget.org/packages/RandomSkunk.Hosting.Cron)

*An IHostedService base class that triggers on a cron schedule.*

---

RandomSkunk.Hosting.Cron provides a `IHostedService` base class that schedules work according to a cron schedule. Cron support is provided by the [Cronos](https://github.com/HangfireIO/Cronos) nuget package.

## Usage

To implement a cron job, inherit from the `CronJob` abstract class:

```c#
using RandomSkunk.Hosting.Cron;

public class MyCronJob : CronJob
{
    public MyCronJob()
        : base("*/30 * * * * *") // Run every 30 seconds.
    {
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        Console.WriteLine("Starting cron job at: {DateTimeOffset.Now:G}");

        // Simulate work.
        await Task.Delay(Random.Shared.Next(250, 1000), stoppingToken);

        Console.WriteLine("Cron job finished at: {DateTimeOffset.Now:G}");
    }
}
```

Add the cron job to an application like any other hosted service:

```c#
services.AddHostedService<MyCronJob>();
```

---

The next example shows a more involved cron job, which is initialized with configuration and has logging.

```c#
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RandomSkunk.Hosting.Cron;

public class AnotherCronJob : CronJob
{
    private const string _cronExpressionSettingName = "AnotherCronJob.CronExpression";

    private readonly ILogger<AnotherCronJob> _logger;

    public AnotherCronJob(IConfiguration configuration, ILogger<AnotherCronJob> logger)
        : base(GetCronExpression(configuration), logger)
    {
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cron job starting at: {time:G}", DateTimeOffset.Now);
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cron job stopping at: {time:G}", DateTimeOffset.Now);
        return base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _logger.LogInformation("Disposing cron job at: {time:G}", DateTimeOffset.Now);
        base.Dispose();
    }

    protected override async Task DoWork(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cron job work starting at: {time:G}", DateTimeOffset.Now);

        // Simulate work.
        await Task.Delay(Random.Shared.Next(250, 1000), stoppingToken);

        _logger.LogInformation("Cron job work finished at: {time:G}", DateTimeOffset.Now);
    }

    private static string GetCronExpression(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration[_cronExpressionSettingName]
            ?? throw new ArgumentException($"Must contain a value for '{_cronExpressionSettingName}' setting.", nameof(configuration));
    }
}
```

An application running this cron job would need to have a configuration that looked something like this (this example is configured to run nightly at 2 am):

```json
{
  "AnotherCronJob": {
    "CronExpression": "0 2 * * *"
  }
}
```
