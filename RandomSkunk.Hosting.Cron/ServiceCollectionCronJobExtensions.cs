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
    /// <param name="configuration">An optional configuration to bind to the cron job's <see cref="CronJobOptions"/>. Applied
    ///     <em>after</em> the <paramref name="configureOptions"/> parameter when provided.</param>
    /// <param name="implementationFactory">An optional factory to create new instances of the cron job implementation.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> or <paramref name="configureOptions"/> are
    ///     <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Action<CronJobOptions> configureOptions,
        IConfiguration? configuration = null,
        Func<IServiceProvider, TCronJob>? implementationFactory = null)
        where TCronJob : CronJob
    {
        if (implementationFactory is null)
            services.AddHostedService<TCronJob>();
        else
            services.AddHostedService(implementationFactory);

        var optionsName = typeof(TCronJob).GetFullName();
        services.Configure(optionsName, configureOptions);

        if (configuration is not null)
            services.Configure<CronJobOptions>(optionsName, configuration);

        return services;
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the <typeparamref name="TCronJob"/> type and bind its
    /// <see cref="CronJobOptions"/> to the given <paramref name="configuration"/>. The name of the registered options instance
    /// is the <see cref="Type.FullName"/> of type <typeparamref name="TCronJob"/>.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="configureOptions">An optional delegate to set the values of the cron job's <see cref="CronJobOptions"/>.
    ///     Applied <em>after</em> the <paramref name="configuration"/> parameter when provided.</param>
    /// <param name="implementationFactory">An optional factory to create new instances of the cron job implementation.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> or <paramref name="configuration"/> are
    ///     <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<CronJobOptions>? configureOptions = null,
        Func<IServiceProvider, TCronJob>? implementationFactory = null)
        where TCronJob : CronJob
    {
        if (implementationFactory is null)
            services.AddHostedService<TCronJob>();
        else
            services.AddHostedService(implementationFactory);

        var optionsName = typeof(TCronJob).GetFullName();
        services.Configure<CronJobOptions>(optionsName, configuration);

        if (configureOptions is not null)
            services.Configure(optionsName, configureOptions);

        return services;
    }
}
