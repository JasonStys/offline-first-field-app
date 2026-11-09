// File: ConflictWorkflows.cs
// Purpose: Load bounded conflict evidence and atomically replace reviewed conflicts with pending work.
// Symbols and line locations: see docs/code-index.md.
// Important state: reviewed evidence is compared again in the transaction; the new mutation uses remote version.

using System.Data.Common;
using System.Globalization;
using FieldOps.Core;
using Microsoft.Data.Sqlite;

namespace FieldOps.Infrastructure;

public sealed partial class LocalFieldStore
{
    private const string ConflictSelect = """
        SELECT c.mutation_id, b.baseline_json, c.local_json, c.remote_json, c.detected_at_utc
        FROM conflicts c
        JOIN outbox_commands o ON o.mutation_id = c.mutation_id
        LEFT JOIN mutation_baselines b ON b.mutation_id = c.mutation_id
        WHERE o.state = $conflict
        """;

    /// <summary>List at most 200 conflicts with optional original baseline and exact recorded values.</summary>
    public async Task<IReadOnlyList<ConflictReview>> GetConflictsAsync(
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumSearchResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ConflictSelect + " ORDER BY c.detected_at_utc, c.mutation_id LIMIT $limit;";
        command.Parameters.AddWithValue("$conflict", (int)QueueState.Conflict);
        command.Parameters.AddWithValue("$limit", maximumCount);
        var conflicts = new List<ConflictReview>(maximumCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            conflicts.Add(ReadConflict(reader));
        }

        return conflicts;
    }

    /// <summary>
    /// Recheck evidence, replace the blocked command and persist chosen values in one transaction.
    /// The isolated reference API still validates and version-checks the new command during sync.
    /// </summary>
    public async Task<Guid> ResolveConflictAsync(
        ConflictReview reviewed,
        IReadOnlyDictionary<string, ResolutionSide> choices,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        var replacement = ConflictResolutionPolicy.Resolve(reviewed, choices, DateTimeOffset.UtcNow);
        var normalizedDevice = deviceId?.Trim() ?? string.Empty;
        if (normalizedDevice.Length is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(deviceId));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = ConflictSelect + " AND c.mutation_id = $mutationId;";
            select.Parameters.AddWithValue("$conflict", (int)QueueState.Conflict);
            select.Parameters.AddWithValue("$mutationId", reviewed.MutationId.ToString("D"));
            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false) || ReadConflict(reader) != reviewed)
            {
                throw new InvalidOperationException("Conflict evidence changed. Reload and review the latest values.");
            }
        }

        await EnsureNoOtherWorkAsync(connection, transaction, replacement.Id, reviewed.MutationId, cancellationToken)
            .ConfigureAwait(false);
        // Deleting and inserting inside the same transaction keeps queue capacity unchanged.
        await using (var remove = connection.CreateCommand())
        {
            remove.Transaction = (SqliteTransaction)transaction;
            remove.CommandText = "DELETE FROM conflicts WHERE mutation_id = $id; DELETE FROM outbox_commands WHERE mutation_id = $id;";
            remove.Parameters.AddWithValue("$id", reviewed.MutationId.ToString("D"));
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpsertInspectionAsync(connection, transaction, replacement, cancellationToken).ConfigureAwait(false);
        await using (var version = connection.CreateCommand())
        {
            version.Transaction = (SqliteTransaction)transaction;
            version.CommandText = "UPDATE inspections SET version = $version WHERE id = $id;";
            version.Parameters.AddWithValue("$version", replacement.Version);
            version.Parameters.AddWithValue("$id", replacement.Id.ToString("D"));
            await version.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var mutationId = Guid.NewGuid();
        await using (var enqueue = connection.CreateCommand())
        {
            enqueue.Transaction = (SqliteTransaction)transaction;
            enqueue.CommandText = """
                INSERT INTO outbox_commands(mutation_id, record_id, device_id, expected_version, record_json, state, attempts)
                VALUES ($mutationId, $recordId, $deviceId, $version, $record, $pending, 0);
                """;
            enqueue.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
            enqueue.Parameters.AddWithValue("$recordId", replacement.Id.ToString("D"));
            enqueue.Parameters.AddWithValue("$deviceId", normalizedDevice);
            enqueue.Parameters.AddWithValue("$version", replacement.Version);
            enqueue.Parameters.AddWithValue("$record", Serialize(replacement));
            enqueue.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
            await enqueue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await SaveBaselineAsync(connection, transaction, mutationId, reviewed.Remote, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return mutationId;
    }

    /// <summary>Read one stored evidence tuple without synthesizing an absent baseline.</summary>
    private static ConflictReview ReadConflict(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.IsDBNull(1) ? null : Deserialize<InspectionSnapshot>(reader.GetString(1)),
        Deserialize<InspectionSnapshot>(reader.GetString(2)),
        Deserialize<InspectionSnapshot>(reader.GetString(3)),
        DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    /// <summary>Bind an optional original snapshot to the new mutation in its existing transaction.</summary>
    private static async Task SaveBaselineAsync(
        SqliteConnection connection, DbTransaction transaction, Guid mutationId,
        InspectionSnapshot? baseline, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "INSERT INTO mutation_baselines(mutation_id, baseline_json) VALUES ($id, $baseline);";
        command.Parameters.AddWithValue("$id", mutationId.ToString("D"));
        command.Parameters.AddWithValue("$baseline", baseline is null ? DBNull.Value : Serialize(baseline));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Find a cached snapshot by indexed identity before a draft modifies it.</summary>
    private static async Task<InspectionSnapshot?> FindInspectionAsync(
        SqliteConnection connection, DbTransaction transaction, Guid recordId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "SELECT id, template_id, title, notes, status, version, updated_at_utc FROM inspections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", recordId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecord(reader) : null;
    }

    /// <summary>Refuse competing pending work while optionally excluding the reviewed command.</summary>
    private static async Task EnsureNoOtherWorkAsync(
        SqliteConnection connection, DbTransaction transaction, Guid recordId,
        Guid? excludedMutation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT count(*) FROM outbox_commands
            WHERE record_id = $id AND mutation_id <> $excluded AND state IN ($pending, $inFlight, $conflict);
            """;
        command.Parameters.AddWithValue("$id", recordId.ToString("D"));
        command.Parameters.AddWithValue("$excluded", excludedMutation?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
        command.Parameters.AddWithValue("$inFlight", (int)QueueState.InFlight);
        command.Parameters.AddWithValue("$conflict", (int)QueueState.Conflict);
        if ((long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0)
        {
            throw new InvalidOperationException("This inspection already has unsynchronized work. Sync or resolve it first.");
        }
    }
}
