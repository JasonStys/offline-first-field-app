// File: ConflictFieldChoice.cs
// Purpose: Present immutable evidence and a deliberate local/remote selection for one field.
// Symbols and line locations: see docs/code-index.md.
// Important state: SelectedIndex is -1 when divergent changes or an unknown base require a choice.

using System.ComponentModel;
using FieldOps.Core;

namespace FieldOps.App;

/// <summary>A portable MAUI binding model with conservative three-way recommendations.</summary>
public sealed class ConflictFieldChoice : INotifyPropertyChanged
{
    private int selectedIndex;

    /// <summary>Capture evidence and recommend only equal or provably disjoint field changes.</summary>
    public ConflictFieldChoice(string name, string? baseline, string local, string remote)
    {
        Name = name;
        BaselineValue = baseline ?? "Unavailable — previous snapshot was not recorded.";
        LocalValue = local;
        RemoteValue = remote;
        selectedIndex = local == remote || baseline is not null && local == baseline ? 1
            : baseline is not null && remote == baseline ? 0 : -1;
        RequiresChoice = selectedIndex < 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; }
    public string BaselineValue { get; }
    public string LocalValue { get; }
    public string RemoteValue { get; }
    public bool RequiresChoice { get; }
    public IReadOnlyList<string> Options { get; } = Array.AsReadOnly(new[] { "Keep local value", "Use remote value" });

    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == selectedIndex) return;
            selectedIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedIndex)));
        }
    }

    /// <summary>Return a deliberate selection; an unresolved field is never silently guessed.</summary>
    public ResolutionSide GetSelection() => selectedIndex switch
    {
        0 => ResolutionSide.Local,
        1 => ResolutionSide.Remote,
        _ => throw new ArgumentException($"Choose the local or remote value for {Name}."),
    };
}
