# Performance baseline

## Environment

- Date: September 18, 2026
- .NET SDK: 10.0.401
- Runtime: 10.0.12, Windows x64
- Configuration: Release
- Fixture: 100 generated SQLite inspection/outbox transactions, then one prefix search

## Results

| Metric | Measured | Budget | Result |
| --- | ---: | ---: | --- |
| Queue write p95 | 6.049 ms | 250 ms | Pass |
| Prefix search | 25.471 ms | 500 ms | Pass |
| Managed allocations | 1,559,696 bytes | 67,108,864 bytes | Pass |
| Returned records | 100 | 100 | Pass |

The guardrails are intentionally generous for heterogeneous hosted runners. They catch accidental
quadratic work, unbounded buffering, and severe storage regressions; they do not predict mobile flash
latency, energy use, cold startup, or large production datasets.

Reproduce with:

```bash
dotnet run --project src/FieldOps.Demo -c Release -- --benchmark
```
