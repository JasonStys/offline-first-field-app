// File: ConflictReview.cs
// Purpose: Represent reviewable base/local/remote evidence and explicit per-field resolution choices.
// Symbols and line locations: see docs/code-index.md.
// Important state: a missing Baseline is unknown, never an invented previous server record.

namespace FieldOps.Core;

/// <summary>Immutable evidence shown to a reviewer before creating a replacement mutation.</summary>
public sealed record ConflictReview(
    Guid MutationId,
    InspectionSnapshot? Baseline,
    InspectionSnapshot Local,
    InspectionSnapshot Remote,
    DateTimeOffset DetectedAtUtc);

/// <summary>Only one of the two recorded values may be deliberately selected.</summary>
public enum ResolutionSide
{
    Local,
    Remote,
}

/// <summary>Builds a validated replacement while retaining remote identity and optimistic version.</summary>
public static class ConflictResolutionPolicy
{
    public static IReadOnlyList<string> FieldNames { get; } =
        Array.AsReadOnly(new[] { "TemplateId", "Title", "Notes", "Status" });

    /// <summary>Resolve all four fields explicitly; missing/unknown choices fail without a mutation.</summary>
    public static InspectionSnapshot Resolve(
        ConflictReview review,
        IReadOnlyDictionary<string, ResolutionSide> choices,
        DateTimeOffset resolvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(choices);
        if (review.Local.Id != review.Remote.Id
            || review.Baseline is { } baseline && baseline.Id != review.Local.Id)
        {
            throw new ArgumentException("Conflict evidence describes different inspections.");
        }

        if (choices.Count != FieldNames.Count
            || FieldNames.Any(field => !choices.TryGetValue(field, out var side) || !Enum.IsDefined(side)))
        {
            throw new ArgumentException("Choose the local or remote value for every field.");
        }

        var result = InspectionSnapshot.Create(
            review.Remote.Id,
            Select("TemplateId", review.Local.TemplateId, review.Remote.TemplateId),
            Select("Title", review.Local.Title, review.Remote.Title),
            Select("Notes", review.Local.Notes, review.Remote.Notes),
            Select("Status", review.Local.Status, review.Remote.Status),
            review.Remote.Version,
            resolvedAtUtc);
        return result.Value ?? throw new ArgumentException(string.Join(" ", result.Errors));

        T Select<T>(string field, T local, T remote) =>
            choices[field] == ResolutionSide.Local ? local : remote;
    }
}
