# Crash-recovery report

## Automated drill

The SQLite integration test performs this deterministic sequence:

1. Initialize the migration twice to verify idempotency.
2. Save a generated inspection and outbox mutation in one transaction.
3. Select the mutation, moving it from pending to in-flight.
4. Simulate process interruption by calling recovery before a result is applied.
5. Verify exactly one row returns to pending.
6. Select it again and verify the same mutation ID is retained while attempt count becomes two.
7. Apply server version three and verify the command is removed and the local record version updates.

Result: pass on September 18, 2026. The HTTP-failure coordinator test separately verifies that a
transport exception returns every selected mutation to pending before propagating the failure.

## Remaining manual drill

Terminating a packaged application at the exact in-flight boundary remains a release-candidate
manual exercise. Its procedure is documented in [operations](../operations.md).
