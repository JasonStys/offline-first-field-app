// File: ConflictMergePolicy.cs
// Purpose: Perform deterministic three-way merges without silently overwriting user-authored text.
// Symbols and line locations: see docs/code-index.md.
// Important state: ConflictingFields is non-empty only when both writers changed a field differently.

namespace FieldOps.Core;

/// <summary>The explicit outcome of comparing a base, local, and remote record.</summary>
public sealed record MergeOutcome(
    InspectionSnapshot? Merged,
    IReadOnlyList<string> ConflictingFields)
{
    public bool HasConflict => ConflictingFields.Count > 0;
}

/// <summary>Implements a deterministic, field-aware optimistic-concurrency merge policy.</summary>
public static class ConflictMergePolicy
{
    /// <summary>
    /// Merge disjoint field changes and report fields changed differently on both sides.
    /// </summary>
    /// <remarks>
    /// Inputs must describe the same record. The method is O(f) time and O(f) space where f is the
    /// fixed number of mergeable fields; it throws for mismatched identities.
    /// </remarks>
    public static MergeOutcome Merge(
        InspectionSnapshot baseline,
        InspectionSnapshot local,
        InspectionSnapshot remote)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(remote);

        if (baseline.Id != local.Id || baseline.Id != remote.Id)
        {
            throw new ArgumentException("All snapshots must describe the same inspection.");
        }

        var conflicts = new List<string>();
        var templateId = MergeField(
            nameof(InspectionSnapshot.TemplateId),
            baseline.TemplateId,
            local.TemplateId,
            remote.TemplateId,
            conflicts);
        var title = MergeField(
            nameof(InspectionSnapshot.Title),
            baseline.Title,
            local.Title,
            remote.Title,
            conflicts);
        var notes = MergeField(
            nameof(InspectionSnapshot.Notes),
            baseline.Notes,
            local.Notes,
            remote.Notes,
            conflicts);
        var status = MergeField(
            nameof(InspectionSnapshot.Status),
            baseline.Status,
            local.Status,
            remote.Status,
            conflicts);

        if (conflicts.Count > 0)
        {
            return new MergeOutcome(null, conflicts);
        }

        return new MergeOutcome(
            remote with
            {
                TemplateId = templateId,
                Title = title,
                Notes = notes,
                Status = status,
                UpdatedAtUtc = Max(local.UpdatedAtUtc, remote.UpdatedAtUtc),
            },
            Array.Empty<string>());
    }

    private static T MergeField<T>(
        string field,
        T baseline,
        T local,
        T remote,
        ICollection<string> conflicts)
        where T : notnull
    {
        var comparer = EqualityComparer<T>.Default;
        var localChanged = !comparer.Equals(local, baseline);
        var remoteChanged = !comparer.Equals(remote, baseline);

        if (localChanged && remoteChanged && !comparer.Equals(local, remote))
        {
            conflicts.Add(field);
        }

        return localChanged ? local : remote;
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;
}
