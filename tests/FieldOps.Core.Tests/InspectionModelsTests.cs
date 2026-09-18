// File: InspectionModelsTests.cs
// Purpose: Verify boundary validation, normalization, and deterministic three-way merge behavior.
// Symbols and line locations: see docs/code-index.md.
// Important fixtures: Baseline is immutable and every randomized property run uses seed 2207.

using FieldOps.Core;

namespace FieldOps.Core.Tests;

public sealed class InspectionModelsTests
{
    private static readonly InspectionSnapshot Baseline = Create(
        title: "Pump station",
        notes: "Baseline",
        status: InspectionStatus.Draft);

    [Fact]
    public void Create_NormalizesValidInput()
    {
        var result = InspectionSnapshot.Create(
            Guid.NewGuid(),
            " template ",
            " title ",
            " notes ",
            InspectionStatus.Draft,
            0,
            DateTimeOffset.UnixEpoch);

        Assert.True(result.IsValid);
        Assert.Equal("template", result.Value!.TemplateId);
        Assert.Equal("title", result.Value.Title);
        Assert.Equal("notes", result.Value.Notes);
    }

    [Fact]
    public void Create_ReturnsAllRelevantBoundaryErrors()
    {
        var result = InspectionSnapshot.Create(
            Guid.Empty,
            string.Empty,
            new string('x', InspectionSnapshot.MaximumTitleLength + 1),
            new string('n', InspectionSnapshot.MaximumNotesLength + 1),
            (InspectionStatus)99,
            -1,
            DateTimeOffset.Now);

        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 6);
    }

    [Fact]
    public void Merge_CombinesDisjointChanges()
    {
        var local = Baseline with { Title = "Local title" };
        var remote = Baseline with { Notes = "Remote notes", Version = 2 };

        var result = ConflictMergePolicy.Merge(Baseline, local, remote);

        Assert.False(result.HasConflict);
        Assert.Equal("Local title", result.Merged!.Title);
        Assert.Equal("Remote notes", result.Merged.Notes);
        Assert.Equal(2, result.Merged.Version);
    }

    [Fact]
    public void Merge_ReportsConcurrentTextConflictWithoutChoosingAWinner()
    {
        var local = Baseline with { Notes = "Local notes" };
        var remote = Baseline with { Notes = "Remote notes", Version = 2 };

        var result = ConflictMergePolicy.Merge(Baseline, local, remote);

        Assert.True(result.HasConflict);
        Assert.Null(result.Merged);
        Assert.Equal([nameof(InspectionSnapshot.Notes)], result.ConflictingFields);
    }

    [Fact]
    public void Merge_RejectsMismatchedRecordIdentities()
    {
        var unrelated = Baseline with { Id = Guid.NewGuid() };

        Assert.Throws<ArgumentException>(
            () => ConflictMergePolicy.Merge(Baseline, Baseline, unrelated));
    }

    [Fact]
    public void Merge_Property_OneSidedChangesNeverConflict()
    {
        var random = new Random(2207);
        for (var index = 0; index < 1_000; index++)
        {
            var title = "Local-" + random.NextInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var local = Baseline with { Title = title };

            var result = ConflictMergePolicy.Merge(Baseline, local, Baseline);

            Assert.False(result.HasConflict);
            Assert.Equal(title, result.Merged!.Title);
        }
    }

    private static InspectionSnapshot Create(
        string title,
        string notes,
        InspectionStatus status) =>
        InspectionSnapshot.Create(
            Guid.Parse("2eb0cb0c-c558-4ab6-8fe6-7d66667e38a1"),
            "fixture-v1",
            title,
            notes,
            status,
            1,
            DateTimeOffset.UnixEpoch).Value!;
}
