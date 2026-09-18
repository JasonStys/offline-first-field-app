# Research references

Primary documentation informed the implementation; the repository does not copy sample code.

- [.NET 10 overview](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview):
  selected the current LTS runtime and C# generation.
- [.NET MAUI local SQLite databases](https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0):
  confirmed shared-code local persistence options and WAL considerations.
- [Store local data with SQLite in MAUI](https://learn.microsoft.com/en-us/training/modules/store-local-data/):
  reinforced asynchronous local access to keep UI work responsive.
- [.NET resilient application development](https://learn.microsoft.com/en-us/dotnet/core/resilience/):
  informed bounded retries/timeouts and the separation between transient failures and conflicts.
- [SQLite transaction documentation](https://www.sqlite.org/lang_transaction.html): transaction
  and atomicity semantics.
- [SQLite query planner](https://www.sqlite.org/queryplanner.html): index and query-plan reasoning.
- [ASP.NET Core error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling):
  safe problem responses and exception boundaries.

The choice not to add automatic HTTP retry middleware in version one is deliberate: mutation
idempotency makes retry safe, but explicit operator-triggered retry keeps failure state visible in
the portfolio demo. A background service would use the current Microsoft resilience packages with
bounded exponential backoff and jitter.
