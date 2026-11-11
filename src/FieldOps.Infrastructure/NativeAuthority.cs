// File: NativeAuthority.cs
// Purpose: Bind the unchanged client database to its explicit local-companion generation.
// Symbols and line locations: see docs/code-index.md.
// State: a public local identity, not a credential/attestation or permission to reset legacy data.

namespace FieldOps.Infrastructure;

/// <summary>Refuse silent remote-authority changes and detectable partial-checkpoint rollback.</summary>
public sealed class NativeAuthority(string profileDirectory)
{
    private readonly string path = Path.Combine(Path.GetFullPath(profileDirectory), "native-authority.txt");

    public async Task<Guid?> ReadExpectedAsync(LocalFieldStore store)
    {
        Guid? expected = null;
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked companion pairings are unsupported.");
            await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var raw = new byte[34];
            var count = await source.ReadAsync(raw);
            if (count != 33 || source.Length != 33 || raw[32] != (byte)'\n'
                || !Guid.TryParseExact(System.Text.Encoding.ASCII.GetString(raw, 0, 32), "N", out var value)
                || value == Guid.Empty)
                throw new InvalidDataException("Native authority identity is invalid; no reset attempted.");
            expected = value;
        }
        catch (FileNotFoundException)
        {
            if (await store.HasRemoteStateAsync().ConfigureAwait(false))
                throw new InvalidOperationException("This existing database contains accepted remote state. It cannot be silently paired to a new local server. Preserve the database and use its original configured source service.");
        }
        return expected;
    }

    public async Task BindAsync(LocalFieldStore store, Guid? expected, Guid instance, long serverCursor)
    {
        if (instance == Guid.Empty || (expected is not null && expected != instance)
            || serverCursor < await store.GetCursorAsync().ConfigureAwait(false))
            throw new InvalidDataException("Companion generation/cursor differs. Restore the complete matching profile; client state was not reset.");
        if (expected is null)
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(System.Text.Encoding.ASCII.GetBytes(instance.ToString("N") + "\n"));
            output.Flush(flushToDisk: true);
        }
    }
}
