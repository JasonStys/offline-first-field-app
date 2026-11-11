// File: NativeCheckpoint.cs
// Purpose: Retain the original reference server's state across explicit local-companion restarts.
// Symbols and line locations: see docs/code-index.md.
// State: one bounded atomic checkpoint; paths are trusted native composition, never HTTP fields.

using System.Text.Json;
using System.Text.Json.Serialization;
using FieldOps.Core;

namespace FieldOps.Api;

/// <summary>Durable optional state for a single native owner's isolated companion service.</summary>
public sealed class NativeCheckpoint
{
    public const int MaximumEntries = 10_000;
    public const int MaximumBytes = 8 * 1024 * 1024;
    private readonly string path;
    private static readonly JsonSerializerOptions Options = new()
    {
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public NativeCheckpoint(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(this.path)!;
        Directory.CreateDirectory(parent);
    }

    /// <summary>Read only the fixed bounded checkpoint; corrupt or ambiguous state is never reset.</summary>
    public NativeServerState? Load()
    {
        FileStream source;
        try
        {
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        using (source)
        {
            if (source.Length > MaximumBytes)
                throw new InvalidDataException("Native companion checkpoint exceeds its byte budget.");
            var bytes = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = source.Read(bytes.AsSpan(count));
                if (read == 0) break;
                count += read;
            }

            if (count > MaximumBytes)
                throw new InvalidDataException("Native companion checkpoint grew beyond its budget.");
            try
            {
                var state = JsonSerializer.Deserialize<NativeServerState>(bytes.AsSpan(0, count), Options)
                    ?? throw new InvalidDataException("Native companion state is absent.");
                Validate(state);
                return state;
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("Native companion checkpoint is invalid; no reset attempted.", error);
            }
        }
    }

    /// <summary>Flush a fresh same-directory file, then atomically replace the prior coherent checkpoint.</summary>
    public void Save(NativeServerState state)
    {
        Validate(state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException("Native companion checkpoint capacity reached.");
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".fieldops-state-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            // Only this call's freshly generated sibling is removed, never a caller-selected file.
            File.Delete(temporary);
        }
    }

    private static void Validate(NativeServerState state)
    {
        if (state.Schema != 1 || state.Records is null || state.Results is null || state.Changes is null
            || state.Records.Length > MaximumEntries || state.Results.Length > MaximumEntries
            || state.Changes.Length > MaximumEntries || state.NextCursor != state.Changes.LongLength
            || state.Records.Any(record => record is null) || state.Results.Any(result => result is null)
            || state.Changes.Any(change => change is null)
            || state.Records.Select(record => record.Id).Distinct().Count() != state.Records.Length
            || state.Results.Select(result => result.MutationId).Distinct().Count() != state.Results.Length)
            throw new InvalidDataException("Native companion checkpoint identity/capacity is invalid.");
        var latest = new Dictionary<Guid, InspectionSnapshot>();
        long expected = 0;
        foreach (var change in state.Changes)
        {
            if (change.Cursor != ++expected || !ValidRecord(change.Record)
                || change.Record.Version != (latest.GetValueOrDefault(change.Record.Id)?.Version ?? 0) + 1)
                throw new InvalidDataException("Native companion change history is inconsistent.");
            latest[change.Record.Id] = change.Record;
        }
        if (latest.Count != state.Records.Length || state.Records.Any(record => !ValidRecord(record)
            || !latest.TryGetValue(record.Id, out var value) || value != record))
            throw new InvalidDataException("Native companion records disagree with their change history.");
        foreach (var result in state.Results)
        {
            if (result.Applied)
            {
                if (result.Code != "applied" || result.AcceptedVersion is null or < 1 || result.ServerRecord is not null)
                    throw new InvalidDataException("Native companion idempotency result is inconsistent.");
            }
            else if (result.AcceptedVersion is not null
                || (result.Code == "conflict" ? result.ServerRecord is null || !ValidRecord(result.ServerRecord)
                    : result.ServerRecord is not null || result.Code is not ("invalid_mutation" or "invalid_record" or "unknown_record_version")))
                throw new InvalidDataException("Native companion rejection result is inconsistent.");
        }
    }

    private static bool ValidRecord(InspectionSnapshot record) => record is not null && record.Version > 0
        && InspectionSnapshot.Create(record.Id, record.TemplateId, record.Title, record.Notes,
            record.Status, record.Version, record.UpdatedAtUtc).IsValid;
}

/// <summary>Versioned native persistence preserves full idempotency and ordered pull semantics.</summary>
public sealed record NativeServerState(int Schema, InspectionSnapshot[] Records, PushItemResult[] Results,
    NativeServerChange[] Changes, long NextCursor);

/// <summary>One actual accepted server version with its original monotonically increasing cursor.</summary>
public sealed record NativeServerChange(long Cursor, InspectionSnapshot Record);
