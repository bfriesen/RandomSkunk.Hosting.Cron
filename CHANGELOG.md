
# RandomSkunk.Hosting.Cron

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog],
and this project adheres to [Semantic Versioning].

## [Unreleased]

### Changed

- Wait more efficiently for the next job.
- Use high-performance logging when executing jobs.

## [2.0.0] - 2024-06-21

### Changed

- Add support for multiple cron expressions.
- Always use the full name of the concrete implementation type as the name of the options (the `cronJobOptionsName` and `cronJobName` parameters no longer exist).

### Removed

- Simplify and reduce the number of `CronJob` constructors and `AddCronJob` extension method overloads.
- Remove `CronJobOptions.CronFormat` setting.

## [1.1.0] - 2024-06-14

### Added

- Add options and hot-reloading.
- Add `IServiceCollection.AddCronJob<TCronJob>` extension methods.

### Changed

- Improvements to logging.
- Update Cronos package to latest version.
- Improve the accuracy of delay between scheduled jobs.

## [1.0.0] - 2023-12-11

### Added

- Add initial project, solution, and package structures.
- Add `CronJob` base class.

[Keep a Changelog]: https://keepachangelog.com/
[Semantic Versioning]: https://semver.org/

[Unreleased]: https://github.com/bfriesen/RandomSkunk.Hosting.Cron/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/bfriesen/RandomSkunk.Hosting.Cron/compare/v1.1.0...v2.0.0
[1.1.0]: https://github.com/bfriesen/RandomSkunk.Hosting.Cron/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/bfriesen/RandomSkunk.Hosting.Cron/compare/v0.0.0...v1.0.0
