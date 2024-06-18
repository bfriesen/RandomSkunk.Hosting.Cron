using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RandomSkunk.Hosting.Cron;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for adding cron jobs to an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionCronJobExtensions
{
    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the <typeparamref name="TCronJob"/> type and configure its
    /// <see cref="CronJobOptions"/> with the given <paramref name="configureOptions"/> function. The name of the registered
    /// options instance is the <see cref="Type.FullName"/> of type <typeparamref name="TCronJob"/>.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configureOptions">A delegate to set the values of the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="implementationFactory">An optional factory to create new instances of the cron job implementation.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> or <paramref name="configureOptions"/> are
    ///     <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Action<CronJobOptions> configureOptions,
        Func<IServiceProvider, TCronJob>? implementationFactory = null)
        where TCronJob : CronJob
    {
        if (implementationFactory is null)
            services.AddHostedService<TCronJob>();
        else
            services.AddHostedService(implementationFactory);

        return services.Configure(typeof(TCronJob).GetFullName(), configureOptions);
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the <typeparamref name="TCronJob"/> type and bind its
    /// <see cref="CronJobOptions"/> to the given <paramref name="configuration"/>. The name of the registered options instance
    /// is the <see cref="Type.FullName"/> of type <typeparamref name="TCronJob"/>.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="implementationFactory">An optional factory to create new instances of the cron job implementation.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> or <paramref name="configuration"/> are
    ///     <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<IServiceProvider, TCronJob>? implementationFactory = null)
        where TCronJob : CronJob
    {
        if (implementationFactory is null)
            services.AddHostedService<TCronJob>();
        else
            services.AddHostedService(implementationFactory);

        return services.Configure<CronJobOptions>(typeof(TCronJob).GetFullName(), configuration);
    }
}
