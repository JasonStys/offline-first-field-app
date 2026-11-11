// File: NativeLaunch.cs
// Purpose: Select a byte-marked disposable native-acceptance profile before any default AppData access.
// Symbols and line locations: see docs/code-index.md.
// State: no normal profile, credential, endpoint or token is accepted from the test marker.

namespace FieldOps.App;

public sealed record NativeLaunch(string? AcceptanceRoot, bool Reopen)
{
    public static NativeLaunch Read(string[] arguments)
    {
        string? root = null;
        var reopen = false;
        for (var index = 1; index < arguments.Length; index++)
        {
            if (arguments[index] == "--native-acceptance-root" && index + 1 < arguments.Length && root is null)
                root = Path.GetFullPath(arguments[++index]);
            else if (arguments[index] == "--acceptance-reopen" && !reopen) reopen = true;
            else throw new ArgumentException("Unknown or incomplete native launch argument.");
        }
        if (reopen && root is null) throw new ArgumentException("Reopen requires a marked synthetic root.");
        if (root is not null)
        {
            var marker = Path.Combine(root, "acceptance.marker");
            if ((File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Acceptance marker must not be linked.");
            using var stream = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read);
            var expected = "fieldops-owned-native-acceptance-v1\n"u8.ToArray();
            var actual = new byte[expected.Length + 1];
            var count = stream.Read(actual);
            if (count != expected.Length || !actual.AsSpan(0, count).SequenceEqual(expected)
                || Directory.Exists(Path.Combine(root, "profile")) != reopen)
                throw new InvalidDataException("Use an exact marked fresh profile or its explicit synthetic reopen.");
        }
        return new(root, reopen);
    }
}
