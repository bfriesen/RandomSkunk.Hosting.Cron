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
    /// Add an <see cref="IHostedService"/> registration for the given cron job type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(this IServiceCollection services)
        where TCronJob : CronJob =>
        services.AddHostedService<TCronJob>();

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="implementationFactory">A factory to create new instances of the cron job implementation.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Func<IServiceProvider, TCronJob> implementationFactory)
        where TCronJob : CronJob =>
        services.AddHostedService(implementationFactory);

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by the <paramref name="configureOptions"/> function.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="cronJobOptionsName">The name used to configure the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="configureOptions">A delegate to set the values of the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        string cronJobOptionsName,
        Action<CronJobOptions> configureOptions)
        where TCronJob : CronJob
    {
        services.AddHostedService<TCronJob>();

        if (configureOptions is not null)
            services.Configure(cronJobOptionsName ?? string.Empty, configureOptions);

        return services;
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by binding them to <paramref name="configuration"/>.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="cronJobOptionsName">The name used to configure the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        string cronJobOptionsName,
        IConfiguration configuration)
        where TCronJob : CronJob
    {
        services.AddHostedService<TCronJob>();

        if (configuration is not null)
            services.Configure<CronJobOptions>(cronJobOptionsName ?? string.Empty, configuration);

        return services;
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by the <paramref name="configureOptions"/> function.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="implementationFactory">A factory to create new instances of the cron job implementation.</param>
    /// <param name="cronJobOptionsName">The name used to configure the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="configureOptions">A delegate to set the values of the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Func<IServiceProvider, TCronJob> implementationFactory,
        string cronJobOptionsName,
        Action<CronJobOptions> configureOptions)
        where TCronJob : CronJob
    {
        services.AddHostedService(implementationFactory);

        if (configureOptions is not null)
            services.Configure(cronJobOptionsName ?? string.Empty, configureOptions);

        return services;
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by binding them to <paramref name="configuration"/>.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="implementationFactory">A factory to create new instances of the cron job implementation.</param>
    /// <param name="cronJobOptionsName">The name used to configure the cron job's <see cref="CronJobOptions"/>.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Func<IServiceProvider, TCronJob> implementationFactory,
        string cronJobOptionsName,
        IConfiguration configuration)
        where TCronJob : CronJob
    {
        services.AddHostedService(implementationFactory);

        if (configuration is not null)
            services.Configure<CronJobOptions>(cronJobOptionsName ?? string.Empty, configuration);

        return services;
    }

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by the <paramref name="configureOptions"/> function. The name used to configure the cron job's
    /// <see cref="CronJobOptions"/> is the name of the <typeparamref name="TCronJob"/> type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configureOptions">A delegate to set the values of the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Action<CronJobOptions> configureOptions)
        where TCronJob : CronJob =>
        services.AddCronJob<TCronJob>(typeof(TCronJob).Name, configureOptions);

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by binding them to <paramref name="configuration"/>. The name used to configure the cron job's
    /// <see cref="CronJobOptions"/> is the name of the <typeparamref name="TCronJob"/> type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TCronJob : CronJob =>
        services.AddCronJob<TCronJob>(typeof(TCronJob).Name, configuration);

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by the <paramref name="configureOptions"/> function. The name used to configure the cron job's
    /// <see cref="CronJobOptions"/> is the name of the <typeparamref name="TCronJob"/> type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="implementationFactory">A factory to create new instances of the cron job implementation.</param>
    /// <param name="configureOptions">A delegate to set the values of the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Func<IServiceProvider, TCronJob> implementationFactory,
        Action<CronJobOptions> configureOptions)
        where TCronJob : CronJob =>
        services.AddCronJob(implementationFactory, typeof(TCronJob).Name, configureOptions);

    /// <summary>
    /// Add an <see cref="IHostedService"/> registration for the given cron job type. The cron job's <see cref="CronJobOptions"/>
    /// are configured by binding them to <paramref name="configuration"/>. The name used to configure the cron job's
    /// <see cref="CronJobOptions"/> is the name of the <typeparamref name="TCronJob"/> type.
    /// </summary>
    /// <typeparam name="TCronJob">A <see cref="CronJob"/> to register.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to register with.</param>
    /// <param name="implementationFactory">A factory to create new instances of the cron job implementation.</param>
    /// <param name="configuration">A configuration to bind to the cron job's <see cref="CronJobOptions"/>.</param>
    /// <returns>The original <see cref="IServiceCollection"/>.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddCronJob<TCronJob>(
        this IServiceCollection services,
        Func<IServiceProvider, TCronJob> implementationFactory,
        IConfiguration configuration)
        where TCronJob : CronJob =>
        services.AddCronJob(implementationFactory, typeof(TCronJob).Name, configuration);
}
