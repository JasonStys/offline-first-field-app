// File: InspectionModels.cs
// Purpose: Define validated inspection, attachment, and synchronization domain values.
// Symbols and line locations: see docs/code-index.md.
// Important state: Version is the last accepted server version; UpdatedAtUtc is always UTC.

namespace FieldOps.Core;

/// <summary>Represents the deliberate lifecycle of an inspection record.</summary>
public enum InspectionStatus
{
    Draft,
    ReadyForReview,
    Completed,
}

/// <summary>Represents the durable lifecycle of an outbound mutation.</summary>
public enum QueueState
{
    Pending,
    InFlight,
    Conflict,
    Rejected,
}

/// <summary>A validated local/server inspection snapshot with optimistic version metadata.</summary>
/// <param name="Id">Stable client-generated identifier.</param>
/// <param name="TemplateId">Synthetic template identifier, at most 64 characters.</param>
/// <param name="Title">Human-readable title, at most 120 characters.</param>
/// <param name="Notes">User-authored notes, at most 4,000 characters.</param>
/// <param name="Status">Explicit workflow state.</param>
/// <param name="Version">Last accepted non-negative server version.</param>
/// <param name="UpdatedAtUtc">UTC update timestamp used for display, never conflict ordering.</param>
public sealed record InspectionSnapshot(
    Guid Id,
    string TemplateId,
    string Title,
    string Notes,
    InspectionStatus Status,
    long Version,
    DateTimeOffset UpdatedAtUtc)
{
    public const int MaximumTemplateIdLength = 64;
    public const int MaximumTitleLength = 120;
    public const int MaximumNotesLength = 4_000;

    /// <summary>Create a normalized snapshot or return every validation error.</summary>
    /// <remarks>Runtime validation is required because records cross storage and network boundaries.</remarks>
    public static ValidationResult<InspectionSnapshot> Create(
        Guid id,
        string? templateId,
        string? title,
        string? notes,
        InspectionStatus status,
        long version,
        DateTimeOffset updatedAtUtc)
    {
        var errors = new List<string>();
        var normalizedTemplate = templateId?.Trim() ?? string.Empty;
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedNotes = notes?.Trim() ?? string.Empty;

        if (id == Guid.Empty)
        {
            errors.Add("Inspection ID must not be empty.");
        }

        ValidateRequiredLength(
            normalizedTemplate,
            MaximumTemplateIdLength,
            "Template ID",
            errors);
        ValidateRequiredLength(normalizedTitle, MaximumTitleLength, "Title", errors);

        if (normalizedNotes.Length > MaximumNotesLength)
        {
            errors.Add($"Notes must not exceed {MaximumNotesLength} characters.");
        }

        if (!Enum.IsDefined(status))
        {
            errors.Add("Inspection status is invalid.");
        }

        if (version < 0)
        {
            errors.Add("Version must be non-negative.");
        }

        if (updatedAtUtc.Offset != TimeSpan.Zero)
        {
            errors.Add("Updated timestamp must use UTC.");
        }

        if (errors.Count > 0)
        {
            return ValidationResult<InspectionSnapshot>.Failure(errors);
        }

        return ValidationResult<InspectionSnapshot>.Success(
            new InspectionSnapshot(
                id,
                normalizedTemplate,
                normalizedTitle,
                normalizedNotes,
                status,
                version,
                updatedAtUtc));
    }

    private static void ValidateRequiredLength(
        string value,
        int maximumLength,
        string label,
        ICollection<string> errors)
    {
        if (value.Length == 0)
        {
            errors.Add($"{label} is required.");
        }
        else if (value.Length > maximumLength)
        {
            errors.Add($"{label} must not exceed {maximumLength} characters.");
        }
    }
}

/// <summary>Metadata for a streamed synthetic attachment stored outside SQLite.</summary>
public sealed record AttachmentDescriptor(
    Guid Id,
    Guid InspectionId,
    string FileName,
    string ContentType,
    long LengthBytes,
    string Sha256Hex,
    string RelativePath);

/// <summary>A typed validation result that never returns an invalid value.</summary>
public sealed record ValidationResult<T>(T? Value, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static ValidationResult<T> Success(T value) => new(value, Array.Empty<string>());

    public static ValidationResult<T> Failure(IEnumerable<string> errors) =>
        new(default, errors.ToArray());
}
