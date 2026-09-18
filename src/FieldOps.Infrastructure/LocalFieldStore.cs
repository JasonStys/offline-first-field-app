// File: LocalFieldStore.cs
// Purpose: Persist inspections, outbox commands, conflicts, and cursor state transactionally in SQLite.
// Symbols and line locations: see docs/code-index.md.
// Important state: databasePath identifies one client store; JSON is validated on every read boundary.

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FieldOps.Core;
using Microsoft.Data.Sqlite;

namespace FieldOps.Infrastructure;

/// <summary>Direct, parameterized SQLite implementation of the offline field store.</summary>
public sealed class LocalFieldStore(string databasePath) : ILocalFieldStore
{
    private const int MaximumSearchResults = 200;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = Path.GetFullPath(databasePath),
        ForeignKeys = true,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false,
    }.ToString();

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = ReadMigration();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveDraftAsync(
        InspectionSnapshot record,
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var normalizedDeviceId = deviceId?.Trim() ?? string.Empty;
        if (normalizedDeviceId.Length is < 1 or > 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceId),
                "Device ID must contain between 1 and 64 characters.");
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await UpsertInspectionAsync(connection, transaction, record, cancellationToken)
            .ConfigureAwait(false);

        var mutation = new InspectionMutation(
            Guid.NewGuid(),
            normalizedDeviceId,
            record.Version,
            record);
        await using var enqueue = connection.CreateCommand();
        enqueue.Transaction = (SqliteTransaction)transaction;
        enqueue.CommandText = """
            INSERT INTO outbox_commands(
                mutation_id, record_id, device_id, expected_version, record_json, state, attempts)
            VALUES ($mutationId, $recordId, $deviceId, $expectedVersion, $recordJson, $state, 0);
            """;
        enqueue.Parameters.AddWithValue("$mutationId", mutation.MutationId.ToString("D"));
        enqueue.Parameters.AddWithValue("$recordId", record.Id.ToString("D"));
        enqueue.Parameters.AddWithValue("$deviceId", normalizedDeviceId);
        enqueue.Parameters.AddWithValue("$expectedVersion", record.Version);
        enqueue.Parameters.AddWithValue("$recordJson", Serialize(record));
        enqueue.Parameters.AddWithValue("$state", (int)QueueState.Pending);
        await enqueue.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueuedMutation>> StartBatchAsync(
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > PushRequest.MaximumBatchSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<QueuedMutation>(maximumCount);
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText = """
                SELECT sequence, mutation_id, device_id, expected_version, record_json, attempts, last_error
                FROM outbox_commands
                WHERE state = $pending
                ORDER BY sequence
                LIMIT $limit;
                """;
            select.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
            select.Parameters.AddWithValue("$limit", maximumCount);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = Deserialize<InspectionSnapshot>(reader.GetString(4));
                var mutation = new InspectionMutation(
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    record);
                entries.Add(
                    new QueuedMutation(
                        reader.GetInt64(0),
                        mutation,
                        QueueState.InFlight,
                        reader.GetInt32(5) + 1,
                        reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        foreach (var entry in entries)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE outbox_commands
                SET state = $inFlight, attempts = attempts + 1, last_error = NULL
                WHERE mutation_id = $mutationId AND state = $pending;
                """;
            update.Parameters.AddWithValue("$inFlight", (int)QueueState.InFlight);
            update.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
            update.Parameters.AddWithValue("$mutationId", entry.Mutation.MutationId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entries;
    }

    /// <inheritdoc />
    public async Task MarkAppliedAsync(
        Guid mutationId,
        long acceptedVersion,
        CancellationToken cancellationToken = default)
    {
        if (acceptedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedVersion));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using (var update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE inspections
                SET version = $version
                WHERE id = (
                    SELECT record_id FROM outbox_commands WHERE mutation_id = $mutationId
                );
                """;
            update.Parameters.AddWithValue("$version", acceptedVersion);
            update.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM outbox_commands WHERE mutation_id = $mutationId;";
            delete.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkConflictAsync(
        Guid mutationId,
        InspectionSnapshot remote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        string localJson;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            select.CommandText =
                "SELECT record_json FROM outbox_commands WHERE mutation_id = $mutationId;";
            select.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
            localJson = (string?)await select.ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Conflict mutation does not exist locally.");
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO conflicts(mutation_id, local_json, remote_json, detected_at_utc)
                VALUES ($mutationId, $local, $remote, $detected)
                ON CONFLICT(mutation_id) DO UPDATE SET
                    remote_json = excluded.remote_json,
                    detected_at_utc = excluded.detected_at_utc;
                """;
            insert.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
            insert.Parameters.AddWithValue("$local", localJson);
            insert.Parameters.AddWithValue("$remote", Serialize(remote));
            insert.Parameters.AddWithValue(
                "$detected",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await UpdateQueueStateAsync(
                connection,
                (SqliteTransaction)transaction,
                mutationId,
                QueueState.Conflict,
                "Version conflict requires review.",
                cancellationToken)
            .ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkRejectedAsync(
        Guid mutationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await UpdateQueueStateAsync(
                connection,
                null,
                mutationId,
                QueueState.Rejected,
                BoundReason(reason),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReturnToPendingAsync(
        IReadOnlyCollection<Guid> mutationIds,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutationIds);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var mutationId in mutationIds)
        {
            await UpdateQueueStateAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    mutationId,
                    QueueState.Pending,
                    BoundReason(reason),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyRemoteAsync(
        IReadOnlyList<InspectionSnapshot> records,
        long cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count > PullResponse.MaximumPullSize || cursor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(records));
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var record in records)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO inspections(id, template_id, title, notes, status, version, updated_at_utc)
                SELECT $id, $templateId, $title, $notes, $status, $version, $updatedAtUtc
                WHERE NOT EXISTS (
                    SELECT 1 FROM outbox_commands
                    WHERE record_id = $id AND state IN ($pending, $inFlight, $conflict)
                )
                ON CONFLICT(id) DO UPDATE SET
                    template_id = excluded.template_id,
                    title = excluded.title,
                    notes = excluded.notes,
                    status = excluded.status,
                    version = excluded.version,
                    updated_at_utc = excluded.updated_at_utc
                WHERE excluded.version > inspections.version;
                """;
            AddRecordParameters(command, record);
            command.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
            command.Parameters.AddWithValue("$inFlight", (int)QueueState.InFlight);
            command.Parameters.AddWithValue("$conflict", (int)QueueState.Conflict);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var checkpoint = connection.CreateCommand();
        checkpoint.Transaction = (SqliteTransaction)transaction;
        checkpoint.CommandText =
            "UPDATE sync_checkpoint SET cursor = $cursor WHERE singleton_id = 1;";
        checkpoint.Parameters.AddWithValue("$cursor", cursor);
        await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetCursorAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cursor FROM sync_checkpoint WHERE singleton_id = 1;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Sync checkpoint is missing."));
    }

    /// <inheritdoc />
    public async Task<int> RecoverInterruptedCommandsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE outbox_commands
            SET state = $pending, last_error = 'Recovered after interrupted synchronization.'
            WHERE state = $inFlight;
            """;
        command.Parameters.AddWithValue("$pending", (int)QueueState.Pending);
        command.Parameters.AddWithValue("$inFlight", (int)QueueState.InFlight);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InspectionSnapshot>> SearchAsync(
        string? query,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > MaximumSearchResults)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        var normalized = query?.Trim() ?? string.Empty;
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = normalized.Length == 0
            ? """
                SELECT id, template_id, title, notes, status, version, updated_at_utc
                FROM inspections
                ORDER BY updated_at_utc DESC, id
                LIMIT $limit;
                """
            : """
                SELECT id, template_id, title, notes, status, version, updated_at_utc
                FROM inspections
                WHERE title GLOB $prefix
                ORDER BY updated_at_utc DESC, id
                LIMIT $limit;
                """;
        if (normalized.Length > 0)
        {
            command.Parameters.AddWithValue("$prefix", EscapeGlob(normalized) + "*");
        }

        command.Parameters.AddWithValue("$limit", maximumCount);
        var records = new List<InspectionSnapshot>(maximumCount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task UpsertInspectionAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        InspectionSnapshot record,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO inspections(id, template_id, title, notes, status, version, updated_at_utc)
            VALUES ($id, $templateId, $title, $notes, $status, $version, $updatedAtUtc)
            ON CONFLICT(id) DO UPDATE SET
                template_id = excluded.template_id,
                title = excluded.title,
                notes = excluded.notes,
                status = excluded.status,
                updated_at_utc = excluded.updated_at_utc;
            """;
        AddRecordParameters(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddRecordParameters(SqliteCommand command, InspectionSnapshot record)
    {
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$templateId", record.TemplateId);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$notes", record.Notes);
        command.Parameters.AddWithValue("$status", (int)record.Status);
        command.Parameters.AddWithValue("$version", record.Version);
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            record.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    private static InspectionSnapshot ReadRecord(SqliteDataReader reader) =>
        InspectionSnapshot.Create(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            (InspectionStatus)reader.GetInt32(4),
            reader.GetInt64(5),
            DateTimeOffset.Parse(
                reader.GetString(6),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind)).Value
        ?? throw new InvalidDataException("Stored inspection failed validation.");

    private static async Task UpdateQueueStateAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid mutationId,
        QueueState state,
        string error,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE outbox_commands
            SET state = $state, last_error = $error
            WHERE mutation_id = $mutationId;
            """;
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$mutationId", mutationId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string ReadMigration()
    {
        const string suffix = "Migrations.001_initial.sql";
        var assembly = typeof(LocalFieldStore).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(resource => resource.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Embedded migration was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidDataException("Stored JSON payload was empty or invalid.");

    private static string EscapeGlob(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal)
            .Replace("*", "[*]", StringComparison.Ordinal)
            .Replace("?", "[?]", StringComparison.Ordinal);

    private static string BoundReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "Synchronization failed without a reason."
            : reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
}
